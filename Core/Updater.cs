using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShopDeck.Core;

// Auto-update via GitHub Releases. Deux formats d'asset coexistent :
//  - ShopDeck-win-x64.zip : publish DOSSIER (prefere). Sur PC compagnie, le single-file non signe etait
//    rescanne par l'EDR a chaque chargement de DLL (10 min de boot) ; le dossier n'expose que des DLL
//    signees Microsoft + ShopDeck.dll, zero extraction.
//  - ShopDeck.exe : single-file, garde comme PONT pour les clients <= 1.3.5 qui ne savent lire que ".exe".
//    Il embarque ce nouvel updater, qui proposera ensuite la conversion vers le dossier a version egale.
public class Updater
{
    public const string Owner = "JulienF2024";
    public const string Repo = "ShopDeck";

    // tests/harnais : force le dossier d'app au lieu de le deduire du process courant
    public static string? AppDirOverride;

    public record ReleaseInfo(Version Version, string Tag, string Notes, string ExeUrl, long Size,
                              string ZipUrl = "", long ZipSize = 0);

    private ReleaseInfo? _stagedRelease;

    public Version LocalVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);

    public static string AppDir
    {
        get
        {
            if (!string.IsNullOrEmpty(AppDirOverride)) return AppDirOverride;
            var exePath = Environment.ProcessPath ?? Assembly.GetEntryAssembly()!.Location;
            return Path.GetDirectoryName(exePath)!;
        }
    }

    // single-file = pas de ShopDeck.dll a cote de l'exe (tout est dans le bundle)
    public static bool IsFolderMode => File.Exists(Path.Combine(AppDir, "ShopDeck.dll"));

    public static bool HasZip(ReleaseInfo rel) => !string.IsNullOrEmpty(rel.ZipUrl);

    public bool IsNewer(ReleaseInfo rel) => rel.Version > LocalVersion;

    // meme version, mais on tourne encore en single-file et la release offre le dossier
    public bool IsFolderConversion(ReleaseInfo rel) =>
        !IsNewer(rel) && rel.Version == LocalVersion && !IsFolderMode && HasZip(rel);

    public bool ShouldOffer(ReleaseInfo rel) => IsNewer(rel) || IsFolderConversion(rel);

    private static HttpClient NewHttp(TimeSpan timeout, bool octetStream = false)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ShopDeck-Updater");
        var tok = Environment.GetEnvironmentVariable("GH_TOKEN") ?? Environment.GetEnvironmentVariable("SHOPDECK_GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(tok))
            http.DefaultRequestHeaders.Authorization = new("Bearer", tok);
        http.DefaultRequestHeaders.Accept.ParseAdd(octetStream ? "application/octet-stream" : "application/vnd.github+json");
        return http;
    }

    public async Task<ReleaseInfo?> CheckLatestAsync()
    {
        // proxy corporate qui ne repond pas -> on ne veut pas un check qui traine indefiniment
        using var http = NewHttp(TimeSpan.FromSeconds(20));

        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var notes = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "";

        var num = new string(tag.SkipWhile(c => !char.IsDigit(c)).TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        if (!Version.TryParse(Normalize(num), out var ver)) return null;

        string exeUrl = "", zipUrl = ""; long exeSize = 0, zipSize = 0;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var aurl = a.GetProperty("url").GetString() ?? a.GetProperty("browser_download_url").GetString() ?? "";
                var asize = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                if (exeUrl == "" && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { exeUrl = aurl; exeSize = asize; }
                if (zipUrl == "" && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { zipUrl = aurl; zipSize = asize; }
            }
        if (exeUrl == "" && zipUrl == "") return null;

        // Size = ce qui sera reellement telecharge (affiche dans l'UI)
        var shown = zipUrl != "" ? zipSize : exeSize;
        return new ReleaseInfo(ver, tag, notes, exeUrl, shown, zipUrl, zipSize);
    }

    private static string Normalize(string v)
    {
        var parts = v.Trim('.').Split('.');
        while (parts.Length < 3) parts = parts.Append("0").ToArray();
        return string.Join('.', parts);
    }

    public record PendingNews(string Version, string Tag, string Notes);

    private static string NewsFlagPath()
    {
        var dataDir = Path.Combine(AppDir, "data");
        Directory.CreateDirectory(dataDir);
        return Path.Combine(dataDir, "update_pending.json");
    }

    private void WriteNewsFlag(ReleaseInfo rel)
    {
        try
        {
            var news = new PendingNews(rel.Version.ToString(3), rel.Tag, rel.Notes);
            File.WriteAllText(NewsFlagPath(), JsonSerializer.Serialize(news));
        }
        catch { }
    }

    public PendingNews? ConsumePendingNews()
    {
        try
        {
            var path = NewsFlagPath();
            if (!File.Exists(path)) return null;
            var news = JsonSerializer.Deserialize<PendingNews>(File.ReadAllText(path));
            File.Delete(path);
            if (news == null) return null;
            if (Version.TryParse(Normalize(news.Version), out var v) && v == LocalVersion)
                return news;
        }
        catch { }
        return null;
    }

    // Retourne le chemin "stage" : un DOSSIER <AppDir>.new (mode zip) ou un fichier ShopDeck.new.exe (legacy).
    public async Task<string> DownloadAndStageAsync(ReleaseInfo rel, IProgress<double>? progress = null)
    {
        _stagedRelease = rel;
        var appDir = AppDir.TrimEnd('\\', '/');

        if (HasZip(rel))
        {
            var newDir = appDir + ".new";
            var zipPath = appDir + ".new.zip";
            await DownloadToAsync(rel.ZipUrl, rel.ZipSize, zipPath, progress);
            ExtractZipSkippingData(zipPath, newDir);
            try { File.Delete(zipPath); } catch { }
            return newDir;
        }

        var newExe = Path.Combine(appDir, "ShopDeck.new.exe");
        await DownloadToAsync(rel.ExeUrl, rel.Size, newExe, progress);
        return newExe;
    }

    private static async Task DownloadToAsync(string url, long expected, string dest, IProgress<double>? progress)
    {
        using var http = NewHttp(TimeSpan.FromMinutes(60), octetStream: true);
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? expected;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(dest);
        var buf = new byte[81920];
        long read = 0; int n;
        while ((n = await src.ReadAsync(buf)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n));
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
    }

    // data\ de l'utilisateur (DB, json flotte, profil webview2, logs) est preserve par le .bat :
    // on n'extrait donc jamais data\ du zip, sinon conflit au move.
    public static void ExtractZipSkippingData(string zipPath, string destDir)
    {
        if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        Directory.CreateDirectory(destDir);
        var destFull = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries)
        {
            var rel = e.FullName.Replace('\\', '/');
            if (rel.Length == 0 || rel.EndsWith("/")) continue;
            if (rel.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) continue;

            var target = Path.GetFullPath(Path.Combine(destDir, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Entree zip hors du dossier cible : " + rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: true);
        }
    }

    public void ApplyAndRestart(string staged)
    {
        var appDir = AppDir.TrimEnd('\\', '/');
        var pid = Environment.ProcessId;

        WriteNewsFlag(_stagedRelease!);

        string bat, script, workDir;
        if (Directory.Exists(staged))
        {
            // le bat vit dans le PARENT : il ne peut pas etre dans le dossier qu'il renomme
            workDir = Path.GetDirectoryName(appDir)!;
            bat = Path.Combine(workDir, "_update.bat");
            script = BuildDirSwapScript(pid, appDir, staged);
        }
        else
        {
            workDir = appDir;
            bat = Path.Combine(appDir, "_update.bat");
            script = BuildExeSwapScript(pid, Path.Combine(appDir, "ShopDeck.exe"), staged);
        }
        File.WriteAllText(bat, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{bat}\"",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = workDir
        });

        System.Windows.Application.Current?.Shutdown();
    }

    // Swap de DOSSIER : APP -> APP.old, APP.new -> APP, en deplacant data\ dans le nouveau.
    // Retries : WebView2/Sumatra enfants ou l'AV peuvent tenir un verrou quelques secondes apres la fermeture.
    // En cas d'echec on remet data\ en place et on relance l'ancienne version (jamais de cle morte).
    public static string BuildDirSwapScript(int pid, string appDir, string newDir)
    {
        var oldDir = appDir + ".old";
        var exe = Path.Combine(appDir, "ShopDeck.exe");
        return $@"@echo off
echo Mise a jour de ShopDeck...
:wait
tasklist /fi ""PID eq {pid}"" | find ""{pid}"" >nul 2>&1
if not errorlevel 1 (
  ping -n 2 127.0.0.1 >nul
  goto wait
)
ping -n 3 127.0.0.1 >nul
if exist ""{oldDir}"" rmdir /s /q ""{oldDir}""
if exist ""{newDir}\data"" rmdir /s /q ""{newDir}\data""
set tries=0
:mvdata
if not exist ""{appDir}\data"" goto mvdataok
move /y ""{appDir}\data"" ""{newDir}\data"" >nul 2>&1
if not errorlevel 1 goto mvdataok
set /a tries+=1
if %tries% GEQ 40 goto fail_nodata
ping -n 2 127.0.0.1 >nul
goto mvdata
:mvdataok
set tries=0
:mvold
move /y ""{appDir}"" ""{oldDir}"" >nul 2>&1
if not errorlevel 1 goto mvoldok
set /a tries+=1
if %tries% GEQ 40 goto fail_restore
ping -n 2 127.0.0.1 >nul
goto mvold
:mvoldok
move /y ""{newDir}"" ""{appDir}"" >nul 2>&1
if errorlevel 1 goto fail_rollback
start """" /d ""{appDir}"" ""{exe}""
ping -n 3 127.0.0.1 >nul
rmdir /s /q ""{oldDir}"" >nul 2>&1
(goto) 2>nul & del /f /q ""%~f0"" & exit /b 0

:fail_rollback
move /y ""{oldDir}"" ""{appDir}"" >nul 2>&1
:fail_restore
if exist ""{newDir}\data"" move /y ""{newDir}\data"" ""{appDir}\data"" >nul 2>&1
:fail_nodata
rmdir /s /q ""{newDir}"" >nul 2>&1
echo echec swap dossier %date% %time% > ""{appDir}\data\update_failed.txt""
start """" /d ""{appDir}"" ""{exe}""
(goto) 2>nul & del /f /q ""%~f0"" & exit /b 1
";
    }

    // Legacy single-file : remplace un seul exe (release sans zip)
    public static string BuildExeSwapScript(int pid, string exePath, string newExe)
    {
        var bak = Path.Combine(Path.GetDirectoryName(exePath)!, "ShopDeck.old.exe");
        return $@"@echo off
echo Mise a jour de ShopDeck...
:wait
tasklist /fi ""PID eq {pid}"" | find ""{pid}"" >nul 2>&1
if not errorlevel 1 (
  ping -n 2 127.0.0.1 >nul
  goto wait
)
ping -n 2 127.0.0.1 >nul
if exist ""{bak}"" del /f /q ""{bak}""
move /y ""{exePath}"" ""{bak}"" >nul
move /y ""{newExe}"" ""{exePath}"" >nul
start """" ""{exePath}""
del /f /q ""%~f0""
";
    }
}
