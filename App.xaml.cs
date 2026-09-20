using System;
using System.IO;
using System.Linq;
using System.Windows;
using ShopDeck.Data;
using ShopDeck.Core;

namespace ShopDeck;

public partial class App : Application
{
    public static Db Database = null!;
    public static string AppDir = "";
    public static string DataDir = "";
    public static string FilesRoot = "";
    public static string SumatraPath = "";
    public static string SumatraDataDir = "";
    public static string PythonPath = "";
    public static string PdfJsDir = "";
    public static string FixCalloutsScript = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // single-file self-contained: AppContext.BaseDirectory pointe vers le dossier
        // d'extraction Temp, PAS vers l'exe reel -> ProcessPath donne le vrai G:\app\ShopDeck\ShopDeck.exe
        var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()!.Location;
        AppDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        DataDir = Path.Combine(AppDir, "data");
        Directory.CreateDirectory(DataDir);

        // trouve le dossier files en remontant (gere prod G:\app\ ET dev bin\Debug\...)
        FilesRoot = ResolveFilesRoot(AppDir);

        // Sumatra portable pour le viewer PDF embarque: on remonte chercher runtime\sumatra
        SumatraPath = ResolveRuntimeFile(AppDir, Path.Combine("sumatra", "SumatraPDF.exe"));

        // pdf.js (WebView2) est le viewer principal: seul moteur qui applique le zoom des /XYZ et /FitR
        // au clic sur un callout. Sumatra reste le fallback si le runtime WebView2 manque sur le PC.
        PdfJsDir = Path.GetDirectoryName(Path.GetDirectoryName(
            ResolveRuntimeFile(AppDir, Path.Combine("pdfjs", "web", "viewer.html")))!)!;

        // python embedded + PyMuPDF sur la cle: convertit les boutons JS des schemas CAT en liens GoTo
        // a l'import, 100% offline. tools\ est frere de runtime\ (G:\app\tools)
        PythonPath = ResolveRuntimeFile(AppDir, Path.Combine("python", "python.exe"));
        var toolsDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(PythonPath)!)!, "..", "tools");
        FixCalloutsScript = Path.GetFullPath(Path.Combine(toolsDir, "fix_callouts.py"));

        // zero trace: -appdata force Sumatra a ecrire settings/thumbnails SUR LA CLE, pas dans %LOCALAPPDATA%
        if (!string.IsNullOrEmpty(SumatraPath))
        {
            SumatraDataDir = Path.Combine(Path.GetDirectoryName(SumatraPath)!, "data");
            Directory.CreateDirectory(SumatraDataDir);
        }

        Database = new Db(Path.Combine(DataDir, "shopdeck.sqlite"));
        Database.Init();
        Database.RelativizeLaunchers();   // ancienne DB avec chemins G:\ absolus -> relatifs, idempotent

        // reimport si le json change: hash du contenu vs meta. Upsert idempotent -> pas de doublons.
        var fleetJson = Path.Combine(DataDir, "equipment_fleet.json");
        if (File.Exists(fleetJson))
        {
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fleetJson)));
            if (Database.GetMeta("fleet_hash") != hash)
            {
                var n = new FleetImporter(Database).Import(fleetJson);
                if (n > 0) Database.SetMeta("fleet_hash", hash);
            }
        }
    }

    private static string ResolveFilesRoot(string start)
    {
        // la vraie base est <racine cle>\files (ex: G:\files). On prend le "files" au niveau
        // racine du volume en priorite, pas le premier trouve en remontant: sinon un dossier
        // "files" parasite pres de l'exe (ex: G:\app\files) intercepte et casse TOUS les chemins.
        var rootFiles = Path.Combine(Path.GetPathRoot(start) ?? "", "files");
        if (Directory.Exists(rootFiles)) return rootFiles;

        // fallback: remontee, mais on ignore un "files" vide pour ne pas se faire piÃ©ger
        var dir = new DirectoryInfo(start);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "files");
            if (Directory.Exists(candidate) &&
                Directory.EnumerateFileSystemEntries(candidate).Any())
                return candidate;
            dir = dir.Parent;
        }
        return rootFiles; // dernier recours: le chemin racine attendu
    }

    // remonte l'arbre en cherchant runtime\<rel> (ex: sumatra\SumatraPDF.exe)
    private static string ResolveRuntimeFile(string start, string rel)
    {
        var dir = new DirectoryInfo(start);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "runtime", rel);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine(start, "runtime", rel); // fallback
    }
}

