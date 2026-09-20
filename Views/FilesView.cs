using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ShopDeck.Views;

// Explorateur des fichiers de la cle (racine = files\). Ajout de fichiers/dossiers en bloc,
// nouveau dossier, ouvrir, renommer, supprimer. Tout reste sous files\ -> portable, jamais de chemin absolu affiche.
public class FilesView : UserControl
{
    private readonly string _root = App.FilesRoot;
    private string _cur;                       // dossier courant (absolu, toujours sous _root)
    private readonly WrapPanel _breadcrumb = new() { Margin = new Thickness(2) };
    private readonly ListView _list = new();
    private readonly TextBlock _empty = new();
    private Point? _dragOrigin;                // ecran; null = pas de drag arme

    public FilesView()
    {
        _cur = _root;
        Directory.CreateDirectory(_root);
        Content = Build();
        // Attendre l'attachement a l'arbre visuel: sinon RenderList/FindResource tournent trop tot et les rows ne se generent pas
        Loaded += (_, _) => Navigate(_cur);
    }

    private UIElement Build()
    {
        var grid = new Grid { Background = (Brush)FindResource("Bg") };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // barre outils
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // fil d'ariane
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // --- barre d'outils ---
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Background = (Brush)FindResource("Surface"),
        };
        bar.Children.Add(Tool("⬆ Remonter", () => { if (!PathEq(_cur, _root)) Navigate(Directory.GetParent(_cur)!.FullName); }));
        bar.Children.Add(Sep());
        bar.Children.Add(Tool("📂 Ajouter fichiers…", AddFiles, accent: true));
        bar.Children.Add(Tool("📁 Ajouter dossier…", AddFolder, accent: true));
        bar.Children.Add(Tool("➕ Nouveau dossier", NewFolder));
        bar.Children.Add(Sep());
        bar.Children.Add(Tool("🔄 Rafraîchir", () => Navigate(_cur)));
        var barBorder = new Border
        {
            Child = bar, Background = (Brush)FindResource("Surface"),
            BorderBrush = (Brush)FindResource("Border"), BorderThickness = new Thickness(0, 0, 0, 1)
        };
        Grid.SetRow(barBorder, 0);
        grid.Children.Add(barBorder);

        // --- fil d'ariane ---
        var bcBorder = new Border { Padding = new Thickness(10, 6, 10, 6), Child = _breadcrumb };
        Grid.SetRow(bcBorder, 1);
        grid.Children.Add(bcBorder);

        // --- liste ---
        _list.BorderThickness = new Thickness(0);
        _list.Background = (Brush)FindResource("Bg");
        _list.MouseDoubleClick += (_, _) => OpenSelected();
        _list.AllowDrop = true;
        _list.Drop += OnDrop;
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete) DeleteSelected(); if (e.Key == Key.F2) RenameSelected(); if (e.Key == Key.Enter) OpenSelected(); };
        _list.MouseRightButtonUp += OnContext;
        // Drag sortant (vers Explorateur / bureau): Preview pour passer avant la selection du ListViewItem
        _list.PreviewMouseLeftButtonDown += OnListDown;
        _list.PreviewMouseMove += OnListMove;
        _list.PreviewMouseLeftButtonUp += (_, _) => _dragOrigin = null;
        _list.DragEnter += OnDragOver;
        _list.DragOver += OnDragOver;

        _list.Foreground = (Brush)FindResource("Text");
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)FindResource("Text")));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2)));
        _list.ItemContainerStyle = itemStyle;
        _list.View = BuildGridView();   // vue construite UNE fois (la recreer a chaque render casse la generation de rows)

        _empty.Text = "Dossier vide. « Ajouter fichiers… », « Ajouter dossier… » ou glisse tes trucs ici.";
        _empty.Foreground = (Brush)FindResource("TextDim");
        _empty.Margin = new Thickness(20);
        _empty.VerticalAlignment = VerticalAlignment.Top;

        var host = new Grid();
        host.Children.Add(_list);
        host.Children.Add(_empty);
        host.AllowDrop = true;
        host.Drop += OnDrop;
        Grid.SetRow(host, 2);
        grid.Children.Add(host);

        return grid;
    }

    // Proprietes obligatoires: le Binding WPF ignore les champs -> cellules vides
    private class Entry
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsDir { get; set; }
        public string Size { get; set; } = "";
        public string Modified { get; set; } = "";
        public string Glyph { get; set; } = "";
    }

    private void Navigate(string dir)
    {
        if (!dir.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) dir = _root;
        _cur = dir;
        RenderBreadcrumb();
        RenderList();
    }

    private void RenderBreadcrumb()
    {
        _breadcrumb.Children.Clear();
        _breadcrumb.Children.Add(Crumb("📁 files", _root));
        var rel = Path.GetRelativePath(_root, _cur);
        if (rel == ".") return;
        var acc = _root;
        foreach (var part in rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            acc = Path.Combine(acc, part);
            _breadcrumb.Children.Add(new TextBlock { Text = " ›", VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextDim") });
            _breadcrumb.Children.Add(Crumb(part, acc));
        }
    }

    private TextBlock Crumb(string text, string target)
    {
        var tb = new TextBlock
        {
            Text = text, Margin = new Thickness(4, 0, 4, 0), Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Accent")
        };
        tb.MouseLeftButtonUp += (_, _) => Navigate(target);
        return tb;
    }

    private void RenderList()
    {
        var items = new System.Collections.Generic.List<Entry>();
        try
        {
            foreach (var d in Directory.GetDirectories(_cur).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var di = new DirectoryInfo(d);
                items.Add(new Entry { Path = d, Name = di.Name, IsDir = true, Glyph = "📁",
                    Modified = di.LastWriteTime.ToString("yyyy-MM-dd HH:mm") });
            }
            foreach (var f in Directory.GetFiles(_cur).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var fi = new FileInfo(f);
                items.Add(new Entry { Path = f, Name = fi.Name, IsDir = false, Glyph = GlyphFor(fi.Extension),
                    Size = HumanSize(fi.Length), Modified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm") });
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }

        _list.ItemsSource = items;
        _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private GridView BuildGridView()
    {
        var gv = new GridView();
        var hStyle = new Style(typeof(GridViewColumnHeader));
        hStyle.Setters.Add(new Setter(Control.BackgroundProperty, (Brush)FindResource("Surface")));
        hStyle.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)FindResource("Text")));
        hStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        hStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        hStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
        gv.ColumnHeaderContainerStyle = hStyle;
        var name = new GridViewColumn { Header = "Nom", Width = 460 };
        var tpl = new System.Windows.DataTemplate();
        var sp = new System.Windows.FrameworkElementFactory(typeof(StackPanel));
        sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var g = new System.Windows.FrameworkElementFactory(typeof(TextBlock));
        g.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Glyph"));
        g.SetValue(FrameworkElement.WidthProperty, 24.0);
        g.SetValue(TextBlock.ForegroundProperty, (Brush)FindResource("Text"));
        var n = new System.Windows.FrameworkElementFactory(typeof(TextBlock));
        n.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
        n.SetValue(TextBlock.ForegroundProperty, (Brush)FindResource("Text"));
        sp.AppendChild(g); sp.AppendChild(n);
        tpl.VisualTree = sp;
        name.CellTemplate = tpl;
        gv.Columns.Add(name);
        gv.Columns.Add(new GridViewColumn { Header = "Taille", Width = 100, DisplayMemberBinding = new System.Windows.Data.Binding("Size") });
        gv.Columns.Add(new GridViewColumn { Header = "Modifié", Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding("Modified") });
        return gv;
    }

    private Entry? Selected => _list.SelectedItem as Entry;

    private void OpenSelected()
    {
        if (Selected is not { } e) return;
        if (e.IsDir) { Navigate(e.Path); return; }
        try { Process.Start(new ProcessStartInfo(e.Path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    // ---------- actions ----------
    private void AddFiles()
    {
        var dlg = new OpenFileDialog { Multiselect = true, Title = "Fichiers à copier ici" };
        if (dlg.ShowDialog() != true) return;
        foreach (var src in dlg.FileNames) CopyInto(src, _cur);
        Navigate(_cur);
    }

    private void AddFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Dossier à copier ici (avec sous-dossiers)" };
        if (dlg.ShowDialog() != true) return;
        var dest = Path.Combine(_cur, new DirectoryInfo(dlg.FolderName).Name);
        try { CopyDir(dlg.FolderName, dest); }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
        Navigate(_cur);
    }

    private void NewFolder()
    {
        var name = Prompt("Nom du nouveau dossier", "Nouveau dossier");
        if (string.IsNullOrWhiteSpace(name)) return;
        try { Directory.CreateDirectory(Path.Combine(_cur, Safe(name))); }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
        Navigate(_cur);
    }

    private void RenameSelected()
    {
        if (Selected is not { } e) return;
        var name = Prompt("Renommer", e.Name);
        if (string.IsNullOrWhiteSpace(name) || name == e.Name) return;
        var dest = Path.Combine(Path.GetDirectoryName(e.Path)!, Safe(name));
        try
        {
            if (e.IsDir) Directory.Move(e.Path, dest); else File.Move(e.Path, dest);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
        Navigate(_cur);
    }

    private void DeleteSelected()
    {
        if (Selected is not { } e) return;
        var what = e.IsDir ? "le dossier et tout son contenu" : "le fichier";
        if (MessageBox.Show($"Supprimer {what} ?\n\n{e.Name}", "Confirmer",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            if (e.IsDir) Directory.Delete(e.Path, true); else File.Delete(e.Path);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
        Navigate(_cur);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        foreach (var p in (string[])e.Data.GetData(DataFormats.FileDrop))
        {
            // drop sur soi-meme (drag sortant qui retombe dans la meme vue) -> rien a faire
            if (PathEq(Path.GetDirectoryName(p.TrimEnd('\\')) ?? "", _cur)) continue;
            try
            {
                if (Directory.Exists(p)) CopyDir(p, Path.Combine(_cur, new DirectoryInfo(p).Name));
                else if (File.Exists(p)) CopyInto(p, _cur);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }
        Navigate(_cur);
    }

    // ---------- drag sortant ----------
    private void OnListDown(object sender, MouseButtonEventArgs e)
    {
        // n'armer que si le clic tombe sur une vraie ligne (pas le fond ni l'en-tete)
        var item = (e.OriginalSource as DependencyObject).FindAncestor<ListViewItem>();
        _dragOrigin = item != null && !(e.OriginalSource as DependencyObject).IsUnder<GridViewColumnHeader>()
            ? PointToScreen(e.GetPosition(this)) : null;
    }

    private void OnListMove(object sender, MouseEventArgs e)
    {
        if (_dragOrigin is not { } origin || e.LeftButton != MouseButtonState.Pressed) return;
        var now = PointToScreen(e.GetPosition(this));
        if (Math.Abs(now.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragOrigin = null;

        var paths = _list.SelectedItems.OfType<Entry>().Select(x => x.Path).Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (paths.Length == 0) return;
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths, true);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
        try { DragDrop.DoDragDrop(_list, data, DragDropEffects.Copy); }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnContext(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not { } sel) return;
        var menu = new ContextMenu();
        var open = new MenuItem { Header = sel.IsDir ? "Ouvrir" : "Ouvrir" }; open.Click += (_, _) => OpenSelected();
        var reveal = new MenuItem { Header = "Afficher dans l'Explorateur Windows" };
        reveal.Click += (_, _) => { try { Process.Start("explorer.exe", $"/select,\"{sel.Path}\""); } catch { } };
        var ren = new MenuItem { Header = "Renommer  (F2)" }; ren.Click += (_, _) => RenameSelected();
        var del = new MenuItem { Header = "Supprimer  (Suppr)" }; del.Click += (_, _) => DeleteSelected();
        menu.Items.Add(open); menu.Items.Add(reveal);
        menu.Items.Add(new Separator());
        menu.Items.Add(ren); menu.Items.Add(del);
        menu.IsOpen = true;
    }

    // ---------- io ----------
    private static void CopyInto(string srcFile, string destDir)
    {
        Directory.CreateDirectory(destDir);
        var name = Path.GetFileName(srcFile);
        var dest = Path.Combine(destDir, name);
        for (int n = 2; File.Exists(dest); n++)
            dest = Path.Combine(destDir, $"{Path.GetFileNameWithoutExtension(name)}_{n}{Path.GetExtension(name)}");
        File.Copy(srcFile, dest);
    }

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
        foreach (var f in Directory.GetFiles(src))
        {
            var target = Path.Combine(dest, Path.GetFileName(f));
            if (!File.Exists(target)) File.Copy(f, target);
        }
    }

    // ---------- helpers ----------
    private static bool PathEq(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static string Safe(string s)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return s.Trim();
    }

    private static string HumanSize(long b) =>
        b >= 1 << 30 ? $"{b / (double)(1 << 30):0.0} Go" :
        b >= 1 << 20 ? $"{b / (double)(1 << 20):0.0} Mo" :
        b >= 1 << 10 ? $"{b / (double)(1 << 10):0.0} Ko" : $"{b} o";

    private static string GlyphFor(string ext) => ext.ToLowerInvariant() switch
    {
        ".pdf" => "📕", ".md" or ".txt" or ".log" => "📄",
        ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" => "🖼",
        ".xls" or ".xlsx" or ".csv" => "📊",
        ".exe" or ".bat" or ".cmd" or ".ps1" => "⚙", ".lnk" => "🔗",
        ".zip" or ".7z" or ".rar" => "🗜", _ => "📄"
    };

    private Button Tool(string text, Action onClick, bool accent = false)
    {
        var b = new Button
        {
            Content = text, Margin = new Thickness(6, 5, 0, 5), Padding = new Thickness(10, 4, 10, 4),
            Cursor = Cursors.Hand, FontSize = 12,
            Background = accent ? (Brush)FindResource("Accent") : Brushes.Transparent,
            Foreground = accent ? Brushes.White : (Brush)FindResource("Text"),
            BorderThickness = new Thickness(accent ? 0 : 1),
            BorderBrush = (Brush)FindResource("Border")
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static UIElement Sep() => new Border
    {
        Width = 1, Margin = new Thickness(6, 8, 0, 8),
        Background = (Brush)Application.Current.Resources["Border"]
    };

    // mini prompt inline (pas de dependance externe)
    private string? Prompt(string title, string initial)
    {
        var win = new Window
        {
            Title = title, Width = 380, Height = 150, Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("Bg")
        };
        var sp = new StackPanel { Margin = new Thickness(16) };
        var tb = new TextBox { Text = initial, Height = 28, Padding = new Thickness(6, 3, 6, 3) };
        tb.SelectAll();
        sp.Children.Add(tb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        string? result = null;
        var ok = new Button { Content = "  OK  ", Height = 30, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => { result = tb.Text; win.DialogResult = true; };
        var cancel = new Button { Content = "Annuler", Height = 30, IsCancel = true };
        row.Children.Add(ok); row.Children.Add(cancel);
        sp.Children.Add(row);
        win.Content = sp;
        tb.Focus();
        return win.ShowDialog() == true ? result : null;
    }
}

internal static class VisualExt
{
    public static T? FindAncestor<T>(this DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }
    public static bool IsUnder<T>(this DependencyObject? d) where T : DependencyObject => d.FindAncestor<T>() != null;
}
