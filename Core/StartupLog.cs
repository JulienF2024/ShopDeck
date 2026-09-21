using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ShopDeck.Core;

// Diagnostic de lenteur au boot (PC compagnie). Ecrit data\startup.log, jamais bloquant.
// Le temps de reference = Process.StartTime : il inclut ce que le host .NET / l'antivirus
// consomment AVANT la 1re ligne de code (extraction single-file, scan EDR), invisible autrement.
public static class StartupLog
{
    private static string _path = "";
    private static DateTime _procStart;
    private static DateTime _last;
    private static readonly StringBuilder _buf = new();

    public static void Begin(string dataDir)
    {
        try
        {
            _path = Path.Combine(dataDir, "startup.log");
            using var p = Process.GetCurrentProcess();
            _procStart = p.StartTime;
            _last = DateTime.Now;
            var preCode = (_last - _procStart).TotalMilliseconds;

            _buf.AppendLine("==== " + _last.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
            _buf.AppendLine($"host+extraction avant 1re ligne de code : {preCode:0} ms");
            _buf.AppendLine("machine=" + Environment.MachineName + " user=" + Environment.UserName
                + " os=" + Environment.OSVersion.VersionString + " x64=" + Environment.Is64BitProcess);
            _buf.AppendLine("exe=" + (Environment.ProcessPath ?? "?"));
            // BaseDirectory != dossier de l'exe => single-file extrait dans TEMP (ou EXTRACT_BASE_DIR)
            _buf.AppendLine("BaseDirectory(extraction)=" + AppContext.BaseDirectory);
            _buf.AppendLine("TEMP=" + Path.GetTempPath()
                + " | DOTNET_BUNDLE_EXTRACT_BASE_DIR=" + (Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR") ?? "(non defini)"));
            try
            {
                var root = Path.GetPathRoot(Environment.ProcessPath ?? "") ?? "";
                if (root.Length > 0) _buf.AppendLine("drive=" + root + " type=" + new DriveInfo(root).DriveType);
                var tempRoot = Path.GetPathRoot(Path.GetTempPath()) ?? "";
                if (tempRoot.Length > 0) _buf.AppendLine("tempDrive=" + tempRoot + " type=" + new DriveInfo(tempRoot).DriveType);
            }
            catch { }
            Flush();
        }
        catch { }
    }

    public static void Mark(string phase)
    {
        try
        {
            var now = DateTime.Now;
            _buf.AppendLine($"{phase,-28} +{(now - _last).TotalMilliseconds,7:0} ms   (total {(now - _procStart).TotalMilliseconds,7:0} ms)");
            _last = now;
            Flush();
        }
        catch { }
    }

    // ecriture incrementale : si l'app se fige plus loin, le log a quand meme ce qui precede
    private static void Flush()
    {
        if (_path.Length == 0) return;
        File.AppendAllText(_path, _buf.ToString());
        _buf.Clear();
    }
}
