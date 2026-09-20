using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShopDeck.Core;

// Auto-update via GitHub Releases: l'exe compile est publie comme asset d'une release,
// le tag (ex: v1.1.0) porte le numero de version compare a la version locale de l'assembly.
public class Updater
{
    public const string Owner = "JulienF2024";
    public const string Repo = "ShopDeck";

    public record ReleaseInfo(Version Version, string Tag, string Notes, string ExeUrl, long Size);

    // retenue entre le download et l'apply pour ecrire le flag "Quoi de neuf"
    private ReleaseInfo? _stagedRelease;

    public Version LocalVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);

    // interroge l'API GitHub. Repo prive -> le token PAT (env GH_TOKEN) est requis pour lire les releases.
    public async Task<ReleaseInfo?> CheckLatestAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ShopDeck-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        var tok = Environment.GetEnvironmentVariable("GH_TOKEN") ?? Environment.GetEnvironmentVariable("SHOPDECK_GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(tok))
            http.DefaultRequestHeaders.Authorization = new("Bearer", tok);

        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var notes = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "";

        // tag "v1.2.0" ou "1.2.0" -> Version. On tolere aussi les suffixes (v1.2.0-beta ignore).
        var num = new string(tag.SkipWhile(c => !char.IsDigit(c)).TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        if (!Version.TryParse(Normalize(num), out var ver)) return null;

        // asset .exe (le single-file publie). On prend le premier .exe de la release.
        string exeUrl = ""; long size = 0;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    // repo prive: url API de l'asset + header Accept octet-stream, sinon browser_download_url (public)
                    exeUrl = a.GetProperty("url").GetString() ?? a.GetProperty("browser_download_url").GetString() ?? "";
                    size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    break;
                }
            }
        if (string.IsNullOrEmpty(exeUrl)) return null;

        return new ReleaseInfo(ver, tag, notes, exeUrl, size);
    }

    // 1.2 -> 1.2.0.0 pour que la comparaison Version soit fiable
    private static string Normalize(string v)
    {
        var parts = v.Trim('.').Split('.');
        while (parts.Length < 3) parts = parts.Append("0").ToArray();
        return string.Join('.', parts);
    }

    public bool IsNewer(ReleaseInfo rel) => rel.Version > LocalVersion;

    // flag ecrit AVANT le redemarrage: au prochain lancement, si la version installee == Version du flag,
    // on affiche la page "Quoi de neuf" une seule fois. Stocke dans data\ (persiste, hors exe remplace).
    public record PendingNews(string Version, string Tag, string Notes);

    private static string NewsFlagPath()
    {
        var exePath = Environment.ProcessPath ?? Assembly.GetEntryAssembly()!.Location;
        var dir = Path.GetDirectoryName(exePath)!;
        var dataDir = Path.Combine(dir, "data");
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
        catch { /* le flag est un bonus, jamais bloquant */ }
    }

    // appele au demarrage: retourne les notes SI une MAJ vient d'etre appliquee (version installee == flag),
    // puis efface le flag. null sinon (rien a montrer). 100% offline.
    public PendingNews? ConsumePendingNews()
    {
        try
        {
            var path = NewsFlagPath();
            if (!File.Exists(path)) return null;
            var news = JsonSerializer.Deserialize<PendingNews>(File.ReadAllText(path));
            File.Delete(path);
            if (news == null) return null;
            // on ne montre que si l'exe qui tourne EST bien la version annoncee (MAJ reussie)
            if (Version.TryParse(Normalize(news.Version), out var v) && v == LocalVersion)
                return news;
        }
        catch { }
        return null;
    }

    // telecharge le nouvel exe a cote de l'actuel puis lance un .bat relais qui:
    // attend la fermeture -> remplace l'exe -> relance. L'exe ne peut pas s'ecraser en tournant.
    public async Task<string> DownloadAndStageAsync(ReleaseInfo rel, IProgress<double>? progress = null)
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Chemin de l'exe introuvable.");
        var dir = Path.GetDirectoryName(exePath)!;
        var newExe = Path.Combine(dir, "ShopDeck.new.exe");
        _stagedRelease = rel;

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ShopDeck-Updater");
        var tok = Environment.GetEnvironmentVariable("GH_TOKEN") ?? Environment.GetEnvironmentVariable("SHOPDECK_GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(tok))
            http.DefaultRequestHeaders.Authorization = new("Bearer", tok);
        // asset d'un repo prive via l'API: force le binaire brut
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");

        using (var resp = await http.GetAsync(rel.ExeUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? rel.Size;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(newExe);
            var buf = new byte[81920];
            long read = 0; int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
        }
        return newExe;
    }

    // ecrit + lance le .bat relais, puis ferme l'app. Le bat tourne hors process ShopDeck.
    public void ApplyAndRestart(string newExe)
    {
        var exePath = Environment.ProcessPath!;
        var dir = Path.GetDirectoryName(exePath)!;
        var bak = Path.Combine(dir, "ShopDeck.old.exe");
        var bat = Path.Combine(dir, "_update.bat");
        var pid = Environment.ProcessId;

        // le flag doit exister AVANT le remplacement: consomme au demarrage de la nouvelle version
        WriteNewsFlag(_stagedRelease!);

        // /f /pid attend proprement; ping = delai sans dependre de timeout.exe (absent sur certains PC compagnie)
        var script = $@"@echo off
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
        File.WriteAllText(bat, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{bat}\"",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = dir
        });

        System.Windows.Application.Current.Shutdown();
    }
}
