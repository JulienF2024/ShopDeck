using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ShopDeck.Data;

namespace ShopDeck.Views;

// Import de fichiers depuis n'importe ou sur le PC -> copie sur la cle (files\import\<dossier>\),
// conversion auto des PDF CAT (boutons JS -> liens GoTo), rattachement a N machines dans un dossier.
// Les dossiers sont hierarchiques: "Électrique/Schémas/Cabine" (separateur /), profondeur libre.
public class ImportDialog : Window
{
    // SubFolder = sous-chemin relatif quand le fichier vient d'un dossier ajoute en bloc (ex: "Schémas/Cabine")
    private class Row { public string Src = ""; public string Title = ""; public string Ext = ""; public string SubFolder = ""; }

    private readonly List<Row> _rows = new();
    private readonly List<Machine> _machines;
    private readonly HashSet<string> _checked = new();
    private readonly StackPanel _fileList = new();
    private readonly ListBox _machineList = new();
    private readonly TextBox _machineFilter = new();
    private readonly ComboBox _folderBox = new() { IsEditable = true };
    private readonly TextBlock _status = new();
    private readonly Button _go;

    public bool Imported { get; private set; }

    public ImportDialog(Window owner, IEnumerable<string>? preselect = null, string? initialFolder = null)
    {
        Owner = owner;
        Title = "Importer des documents";
        Width = 860; Height = 640; MinWidth = 700; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Bg"];

        _machines = App.Database.GetMachines();
        if (preselect != null) foreach (var u in preselect) _checked.Add(u);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- colonne gauche: fichiers ---
        var left = new DockPanel();
        var lh = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var pick = Btn("📂 Fichiers…", true);
        pick.Click += (_, _) => PickFiles();
        var pickDir = Btn("📁 Dossier complet…", true);
        pickDir.Click += (_, _) => PickFolder();
        DockPanel.SetDock(pick, Dock.Right);
        DockPanel.SetDock(pickDir, Dock.Right);
        lh.Children.Add(pick);
        lh.Children.Add(pickDir);
        lh.Children.Add(Label("1. Fichiers  (un dossier conserve sa sous-arborescence sous le dossier cible)"));
        DockPanel.SetDock(lh, Dock.Top);
        left.Children.Add(lh);
        left.Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["Surface"],
            BorderBrush = (Brush)Application.Current.Resources["Border"], BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _fileList }
        });
        _fileList.AllowDrop = true;
        left.AllowDrop = true;
        left.Drop += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            foreach (var p in (string[])e.Data.GetData(DataFormats.FileDrop))
            {
                if (Directory.Exists(p)) AddFolder(p);
                else AddFiles(new[] { p });
            }
        };
        Grid.SetColumn(left, 0); Grid.SetRow(left, 0);
        grid.Children.Add(left);

        // --- colonne droite: machines ---
        var right = new DockPanel();
        var rh = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        rh.Children.Add(Label("2. Machines cibles"));
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var all = Btn("Visibles", false); all.Click += (_, _) => { foreach (var m in Visible()) _checked.Add(m.UnitId); RenderMachines(); };
        var none = Btn("Aucune", false); none.Click += (_, _) => { _checked.Clear(); RenderMachines(); };
        quick.Children.Add(all); quick.Children.Add(none);
        rh.Children.Add(quick);
        _machineFilter.Height = 28; _machineFilter.Padding = new Thickness(6, 3, 6, 3);
        _machineFilter.ToolTip = "Filtre: 793F, 71HTR, 040-1…";
        _machineFilter.TextChanged += (_, _) => RenderMachines();
        rh.Children.Add(_machineFilter);
        DockPanel.SetDock(rh, Dock.Top);
        right.Children.Add(rh);
        _machineList.BorderThickness = new Thickness(1);
        right.Children.Add(_machineList);
        Grid.SetColumn(right, 2); Grid.SetRow(right, 0);
        grid.Children.Add(right);

        // --- dossier ---
        var fold = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        fold.Children.Add(Label("3. Dossier  —  choisis un existant ou tape un chemin, ex: Électrique/Schémas/Cabine (le / crée des sous-dossiers)"));
        _folderBox.Height = 30; _folderBox.Margin = new Thickness(0, 4, 0, 0);
        _folderBox.Text = string.IsNullOrWhiteSpace(initialFolder) ? "Autre" : initialFolder;
        fold.Children.Add(_folderBox);
        Grid.SetColumnSpan(fold, 3); Grid.SetRow(fold, 1);
        grid.Children.Add(fold);

        // --- bas: statut + go ---
        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        _go = Btn("  Importer  ", true);
        _go.Height = 34; _go.FontSize = 13;
        _go.Click += async (_, _) => await RunImport();
        DockPanel.SetDock(_go, Dock.Right);
        bottom.Children.Add(_go);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Foreground = (Brush)Application.Current.Resources["TextDim"];
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        bottom.Children.Add(_status);
        Grid.SetColumnSpan(bottom, 3); Grid.SetRow(bottom, 2);
        grid.Children.Add(bottom);

        Content = grid;
        RenderMachines();
        RefreshFolders();
        UpdateStatus();
    }

    // ---------- fichiers ----------
    private void PickFiles()
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true, Title = "Fichiers à importer",
            Filter = "Documents|*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.txt;*.md;*.csv;*.xlsx;*.xls;*.docx|Tous|*.*"
        };
        if (dlg.ShowDialog(this) == true) AddFiles(dlg.FileNames);
    }

    // OpenFolderDialog (.NET 8 / Win32) — pas d'install, natif
    private void PickFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Dossier à importer (avec sous-dossiers)" };
        if (dlg.ShowDialog(this) == true) AddFolder(dlg.FolderName);
    }

    // ajoute tous les fichiers d'un dossier RECURSIVEMENT, en conservant l'arbo relative dans SubFolder.
    // le nom du dossier racine devient le 1er niveau (ex: dossier "Électrique" -> folder cible/Électrique/...)
    private void AddFolder(string dir)
    {
        var root = new DirectoryInfo(dir);
        var baseName = root.Name;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(f).TrimStart('.').ToLowerInvariant();
            if (SkipExt(ext)) continue;
            if (_rows.Any(r => r.Src.Equals(f, StringComparison.OrdinalIgnoreCase))) continue;
            var rel = Path.GetDirectoryName(Path.GetRelativePath(dir, f)) ?? "";
            var sub = rel.Length == 0 ? baseName : baseName + "/" + rel.Replace('\\', '/');
            _rows.Add(new Row
            {
                Src = f,
                Title = Path.GetFileNameWithoutExtension(f),
                Ext = ext,
                SubFolder = sub
            });
        }
        RenderFiles();
        UpdateStatus();
    }

    // fichiers systeme / temporaires inutiles a embarquer (on garde tout le reste, y compris .bat/.exe)
    private static bool SkipExt(string ext) => ext is "tmp" or "db-shm" or "db-wal";

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (!File.Exists(p) || _rows.Any(r => r.Src.Equals(p, StringComparison.OrdinalIgnoreCase))) continue;
            _rows.Add(new Row
            {
                Src = p,
                Title = Path.GetFileNameWithoutExtension(p),
                Ext = Path.GetExtension(p).TrimStart('.').ToLowerInvariant()
            });
        }
        RenderFiles();
        UpdateStatus();
    }

    private void RenderFiles()
    {
        _fileList.Children.Clear();
        if (_rows.Count == 0)
        {
            _fileList.Children.Add(new TextBlock
            {
                Text = "Aucun fichier. Clique « Choisir des fichiers… » ou glisse-les ici.",
                Margin = new Thickness(12), Foreground = (Brush)Application.Current.Resources["TextDim"]
            });
            return;
        }
        foreach (var r in _rows)
        {
            var row = r;
            var dp = new DockPanel { Margin = new Thickness(8, 4, 8, 4) };
            var x = new Button
            {
                Content = "✕", Width = 24, Height = 24, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                Foreground = (Brush)Application.Current.Resources["TextDim"]
            };
            x.Click += (_, _) => { _rows.Remove(row); RenderFiles(); UpdateStatus(); };
            DockPanel.SetDock(x, Dock.Right);
            dp.Children.Add(x);

            var glyph = new TextBlock { Text = Glyph(row.Ext), Width = 26, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(glyph, Dock.Left);
            dp.Children.Add(glyph);

            var sp = new StackPanel();
            var tb = new TextBox { Text = row.Title, Height = 26, Padding = new Thickness(4, 2, 4, 2) };
            tb.TextChanged += (_, _) => row.Title = tb.Text;
            sp.Children.Add(tb);
            sp.Children.Add(new TextBlock
            {
                Text = (row.SubFolder.Length > 0 ? "→ " + row.SubFolder + "  ·  " : "") + row.Src,
                FontSize = 10, Foreground = (Brush)Application.Current.Resources["TextDim"],
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 1, 0, 0)
            });
            dp.Children.Add(sp);
            _fileList.Children.Add(dp);
        }
    }

    // ---------- machines ----------
    private IEnumerable<Machine> Visible()
    {
        var q = _machineFilter.Text.Trim();
        return string.IsNullOrEmpty(q) ? _machines : _machines.Where(m =>
            Has(m.UnitId, q) || Has(m.LegacyId, q) || Has(m.Model, q) || Has(m.Family, q));
    }
    private static bool Has(string? s, string q) => !string.IsNullOrEmpty(s) && s.Contains(q, StringComparison.OrdinalIgnoreCase);

    private void RenderMachines()
    {
        _machineList.Items.Clear();
        foreach (var m in Visible())
        {
            var unit = m.UnitId;
            var cb = new CheckBox
            {
                IsChecked = _checked.Contains(unit), Margin = new Thickness(4, 2, 4, 2),
                Content = new TextBlock { Text = $"{m.UnitId}   {m.Model}" + (string.IsNullOrEmpty(m.LegacyId) ? "" : $"   ({m.LegacyId})") }
            };
            cb.Checked += (_, _) => { _checked.Add(unit); RefreshFolders(); UpdateStatus(); };
            cb.Unchecked += (_, _) => { _checked.Remove(unit); RefreshFolders(); UpdateStatus(); };
            _machineList.Items.Add(cb);
        }
    }

    // union des dossiers deja utilises par les machines cochees (sinon toutes) pour proposer les existants
    private void RefreshFolders()
    {
        var current = _folderBox.Text;
        var src = _checked.Count > 0 ? _checked.AsEnumerable() : _machines.Select(m => m.UnitId);
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in src.Take(60)) foreach (var f in App.Database.GetFoldersForMachine(u)) set.Add(f);
        _folderBox.Items.Clear();
        foreach (var f in set) _folderBox.Items.Add(f);
        _folderBox.Text = current;
    }

    private void UpdateStatus()
    {
        var pdf = _rows.Count(r => r.Ext == "pdf");
        _status.Text = $"{_rows.Count} fichier(s)" + (pdf > 0 ? $" dont {pdf} PDF (conversion auto des renvois CAT)" : "") +
                       $"   →   {_checked.Count} machine(s)";
        _go.IsEnabled = _rows.Count > 0 && _checked.Count > 0;
    }

    // ---------- import ----------
    private async Task RunImport()
    {
        var baseFolder = NormalizeFolder(_folderBox.Text);
        var machines = _checked.ToList();
        var rows = _rows.ToList();
        _go.IsEnabled = false;
        IsEnabled = false;
        var errors = new List<string>();
        try
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                // dossier effectif = dossier cible + sous-arbo du dossier importe (le / cree les sous-niveaux)
                var folder = r.SubFolder.Length == 0
                    ? baseFolder
                    : NormalizeFolder((baseFolder == "Autre" ? "" : baseFolder) + "/" + r.SubFolder);
                _status.Text = $"[{i + 1}/{rows.Count}] copie {r.Title}…";
                string rel;
                try
                {
                    rel = await Task.Run(() => CopyToKey(r, folder));
                }
                catch (Exception ex) { errors.Add($"{r.Title}: {ex.Message}"); continue; }

                if (r.Ext == "pdf")
                {
                    _status.Text = $"[{i + 1}/{rows.Count}] conversion des renvois {r.Title}…";
                    var res = await Task.Run(() => FixCallouts(Path.Combine(App.FilesRoot, rel)));
                    if (res != null) errors.Add($"{r.Title}: conversion → {res}");
                }

                var full = new FileInfo(Path.Combine(App.FilesRoot, rel));
                var system = folder == "Autre" ? null : folder.Split('/')[0];
                var docId = App.Database.InsertDoc(r.Title.Trim(), rel, r.Ext, full.Length, system);
                foreach (var u in machines)
                {
                    if (folder != "Autre") App.Database.CreateFolder(u, folder);
                    App.Database.AddDocToMachine(docId, u, folder == "Autre" ? null : folder);
                }
                Imported = true;
            }
        }
        finally { IsEnabled = true; }

        if (errors.Count > 0)
            MessageBox.Show(this, string.Join("\n", errors), "Import terminé avec avertissements", MessageBoxButton.OK, MessageBoxImage.Warning);
        Close();
    }

    // files\import\<dossier>\<titre>.<ext>, suffixe _2, _3… si collision de nom
    private static string CopyToKey(Row r, string folder)
    {
        var dir = Path.Combine(App.FilesRoot, "import", folder == "Autre" ? "" : folder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        var baseName = SafeName(r.Title);
        var dest = Path.Combine(dir, $"{baseName}.{r.Ext}");
        for (int n = 2; File.Exists(dest); n++)
            dest = Path.Combine(dir, $"{baseName}_{n}.{r.Ext}");
        File.Copy(r.Src, dest);
        return Path.GetRelativePath(App.FilesRoot, dest);
    }

    // python portable de la cle + fix_callouts.py ; null = OK (ou rien a convertir)
    private static string? FixCallouts(string pdf)
    {
        if (!File.Exists(App.PythonPath) || !File.Exists(App.FixCalloutsScript)) return "python/outil absent de la clé, PDF copié tel quel";
        var psi = new ProcessStartInfo
        {
            FileName = App.PythonPath,
            Arguments = $"\"{App.FixCalloutsScript}\" \"{pdf}\" --apply",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        // zero trace: pas de __pycache__ sur la cle ni ailleurs
        psi.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(180_000)) { try { p.Kill(); } catch { } return "timeout conversion"; }
        return p.ExitCode == 0 ? null : err.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim();
    }

    // "  électrique // schémas / " -> "électrique/schémas"
    public static string NormalizeFolder(string? s)
    {
        var parts = (s ?? "").Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(p => p.Trim()).Where(p => p.Length > 0);
        var r = string.Join("/", parts);
        return r.Length == 0 ? "Autre" : r;
    }

    private static string SafeName(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s.Trim()) sb.Append(bad.Contains(ch) ? '_' : ch);
        var r = sb.ToString().Trim();
        return r.Length == 0 ? "document" : r;
    }

    private static string Glyph(string ext) => ext switch
    {
        "pdf" => "📕", "md" or "txt" => "📄", "jpg" or "jpeg" or "png" or "bmp" => "🖼",
        "xls" or "xlsx" or "csv" => "📊", _ => "📁"
    };

    private static TextBlock Label(string t) => new()
    {
        Text = t, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        Foreground = (Brush)Application.Current.Resources["Text"], TextWrapping = TextWrapping.Wrap
    };

    private static Button Btn(string t, bool accent) => new()
    {
        Content = t, Height = 28, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 0),
        Cursor = Cursors.Hand, FontSize = 12,
        Background = accent ? (Brush)Application.Current.Resources["Accent"] : (Brush)Application.Current.Resources["Surface"],
        Foreground = accent ? Brushes.White : (Brush)Application.Current.Resources["Text"],
        BorderThickness = new Thickness(accent ? 0 : 1),
        BorderBrush = (Brush)Application.Current.Resources["Border"]
    };
}
