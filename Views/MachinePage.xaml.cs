using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShopDeck.Data;

namespace ShopDeck.Views;

// Fiche d'une machine: entete + docs groupes par dossier virtuel. Construit en code (pas de XAML).
// Organisation DB-only: les fichiers physiques ne sont jamais touches, tout passe par doc_machine/folders.
public class MachinePage : UserControl
{
    private readonly Machine _m;
    public event Action<DocItem>? DocOpened;

    public MachinePage(Machine m)
    {
        _m = m;
        Content = Build();
    }

    // reconstruit toute la fiche apres une operation d'organisation
    private void Refresh() => Content = Build();

    private UIElement Build()
    {
        var root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(Header());
        panel.Children.Add(Toolbar());

        var docs = App.Database.GetDocsForMachine(_m.UnitId);
        var folders = App.Database.GetFoldersForMachine(_m.UnitId);

        if (folders.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Aucun document. Utilise « + Document » pour en rattacher, ou « + Dossier » pour organiser.",
                Foreground = (Brush)FindResource("TextDim"), Margin = new Thickness(0, 16, 0, 0)
            });
        }
        else
        {
            var byFolder = docs.GroupBy(d => d.Folder ?? "Autre")
                               .ToDictionary(g => g.Key, g => g.ToList());
            // arbre: un dossier "A/B/C" implique "A" et "A/B" meme s'ils n'ont rien -> on les cree
            // en virtuel pour avoir un parent pliable a chaque niveau
            var all = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in folders)
            {
                var parts = f.Split('/');
                for (int i = 1; i <= parts.Length; i++) all.Add(string.Join("/", parts.Take(i)));
            }

            // construit la hierarchie parent->enfants a partir des chemins plats
            var nodes = all.ToDictionary(p => p, p => new FolderNode(p), StringComparer.OrdinalIgnoreCase);
            var roots = new List<FolderNode>();
            foreach (var n in nodes.Values)
            {
                if (byFolder.TryGetValue(n.FullPath, out var list)) n.Docs = list;
                var slash = n.FullPath.LastIndexOf('/');
                if (slash > 0 && nodes.TryGetValue(n.FullPath.Substring(0, slash), out var parent))
                    parent.Children.Add(n);
                else
                    roots.Add(n);
            }
            foreach (var r in roots) panel.Children.Add(RenderNode(r, 0));
        }

        root.Content = panel;
        return root;
    }

    private UIElement Header()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(new TextBlock
        {
            Text = _m.UnitId, FontSize = 26, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("Text")
        });
        var sub = new List<string> { _m.Model };
        if (!string.IsNullOrEmpty(_m.LegacyId)) sub.Add(_m.LegacyId);
        if (!string.IsNullOrEmpty(_m.Year)) sub.Add(_m.Year!);
        sp.Children.Add(new TextBlock
        {
            Text = string.Join("  •  ", sub.Where(s => !string.IsNullOrEmpty(s))),
            FontSize = 13, Foreground = (Brush)FindResource("TextDim"), Margin = new Thickness(0, 2, 0, 0)
        });
        var specs = new List<string>();
        if (!string.IsNullOrEmpty(_m.Vin)) specs.Add($"VIN {_m.Vin}");
        if (!string.IsNullOrEmpty(_m.Engine)) specs.Add($"Moteur {_m.Engine}");
        if (!string.IsNullOrEmpty(_m.EngineVin)) specs.Add($"S/N moteur {_m.EngineVin}");
        if (specs.Count > 0)
            sp.Children.Add(new TextBlock
            {
                Text = string.Join("   ", specs), FontSize = 12,
                Foreground = (Brush)FindResource("Accent"), Margin = new Thickness(0, 6, 0, 0)
            });
        return sp;
    }

    // barre d'actions d'organisation
    private UIElement Toolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        bar.Children.Add(ActionButton("+ Dossier", () =>
        {
            var name = Prompt("Nouveau dossier", "Nom du dossier  (ex: Électrique/Schémas/Cabine — le / crée des sous-dossiers) :", "");
            if (!string.IsNullOrWhiteSpace(name)) { App.Database.CreateFolder(_m.UnitId, ImportDialog.NormalizeFolder(name)); Refresh(); }
        }));
        bar.Children.Add(ActionButton("+ Importer des fichiers", () =>
        {
            var dlg = new ImportDialog(Window.GetWindow(this), new[] { _m.UnitId });
            dlg.ShowDialog();
            if (dlg.Imported) Refresh();
        }));
        bar.Children.Add(ActionButton("+ Document existant", () => ShowAddDocDialog()));
        return bar;
    }

    private Button ActionButton(string text, Action onClick)
    {
        var b = new Button
        {
            Content = "  " + text + "  ", Height = 30, Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(10, 0, 10, 0), Cursor = Cursors.Hand,
            Background = (Brush)FindResource("Accent"), Foreground = Brushes.White,
            BorderThickness = new Thickness(0), FontSize = 12
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    // noeud d'arbre dossier: chemin complet + docs directs + sous-dossiers
    private sealed class FolderNode
    {
        public string FullPath;
        public List<DocItem> Docs = new();
        public List<FolderNode> Children = new();
        public FolderNode(string p) { FullPath = p; }
    }

    private UIElement RenderNode(FolderNode node, int depth)
    {
        var folder = node.FullPath;
        var leaf = folder.Substring(folder.LastIndexOf('/') + 1);
        var box = new StackPanel { Margin = new Thickness(depth == 0 ? 0 : 16, 0, 0, depth == 0 ? 12 : 6) };

        // conteneur pliable: tout ce qui est sous ce dossier (docs + sous-dossiers), cache par defaut
        var content = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(4, 4, 0, 4) };
        var caret = new TextBlock
        {
            Text = "▶", FontSize = 11, Width = 14, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextDim")
        };

        var headSp = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand };
        headSp.Children.Add(caret);
        var head = new TextBlock
        {
            Text = $"📂 {leaf}  ({node.Docs.Count})", FontSize = depth == 0 ? 14 : 13, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Text"), VerticalAlignment = VerticalAlignment.Center, ToolTip = folder
        };
        headSp.Children.Add(head);
        headSp.MouseLeftButtonUp += (_, _) =>
        {
            var open = content.Visibility != Visibility.Visible;
            content.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            caret.Text = open ? "▼" : "▶";
        };

        // menu contextuel du dossier (sous-dossier / renommer / supprimer)
        var fmenu = new ContextMenu();
        fmenu.Items.Add(MenuItemFor("Nouveau sous-dossier…", () =>
        {
            var nn = Prompt("Nouveau sous-dossier", $"Nom du sous-dossier dans « {folder} » :", "");
            if (!string.IsNullOrWhiteSpace(nn))
            { App.Database.CreateFolder(_m.UnitId, ImportDialog.NormalizeFolder(folder + "/" + nn)); Refresh(); }
        }));
        fmenu.Items.Add(MenuItemFor("Importer des fichiers ici…", () =>
        {
            var dlg = new ImportDialog(Window.GetWindow(this), new[] { _m.UnitId }, folder);
            dlg.ShowDialog();
            if (dlg.Imported) Refresh();
        }));
        fmenu.Items.Add(new Separator());
        fmenu.Items.Add(MenuItemFor("Renommer le dossier", () =>
        {
            var nn = Prompt("Renommer le dossier", "Nouveau nom :", folder);
            if (!string.IsNullOrWhiteSpace(nn) && nn.Trim() != folder)
            { App.Database.RenameFolder(_m.UnitId, folder, nn.Trim()); Refresh(); }
        }));
        fmenu.Items.Add(MenuItemFor("Supprimer le dossier (retire ses docs de la machine)", () =>
        {
            var r = MessageBox.Show(
                $"Supprimer « {folder} » ?\n{node.Docs.Count} document(s) seront retires de cette machine.\nLes fichiers ne sont pas effaces.",
                "Confirmer", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes) { App.Database.DeleteFolder(_m.UnitId, folder); Refresh(); }
        }));
        headSp.ContextMenu = fmenu;
        headSp.MouseRightButtonUp += (_, _) => fmenu.IsOpen = true;
        box.Children.Add(headSp);

        // message d'aide seulement si le dossier est reellement vide (aucun doc ET aucun sous-dossier)
        if (node.Docs.Count == 0 && node.Children.Count == 0)
            content.Children.Add(new TextBlock
            {
                Text = "  (vide — clic droit sur un document pour le déplacer ici)",
                FontSize = 11, Foreground = (Brush)FindResource("TextDim"), Margin = new Thickness(0, 0, 0, 4)
            });

        foreach (var d in node.Docs)
            content.Children.Add(DocRow(d, folder));
        foreach (var child in node.Children.OrderBy(c => c.FullPath, StringComparer.OrdinalIgnoreCase))
            content.Children.Add(RenderNode(child, depth + 1));

        box.Children.Add(content);
        return box;
    }

    private UIElement DocRow(DocItem d, string currentFolder)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = ExtGlyph(d.Ext), Margin = new Thickness(0, 0, 8, 0) });
        sp.Children.Add(new TextBlock
        {
            Text = d.Title, Foreground = (Brush)FindResource("Text"),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (d.IsDuplicate)
            sp.Children.Add(new TextBlock
            {
                Text = "  DUP", FontSize = 10, Foreground = Brushes.OrangeRed,
                VerticalAlignment = VerticalAlignment.Center
            });

        var b = new Border
        {
            Child = sp, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 1, 0, 1),
            Background = (Brush)FindResource("Surface"),
            BorderBrush = (Brush)FindResource("Border"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand
        };
        b.MouseLeftButtonUp += (_, _) => DocOpened?.Invoke(d);

        // menu contextuel du document
        var menu = new ContextMenu();

        var move = new MenuItem { Header = "Déplacer vers" };
        foreach (var f in App.Database.GetFoldersForMachine(_m.UnitId).Where(f => f != currentFolder))
        {
            var target = f;
            move.Items.Add(MenuItemFor(target, () => { App.Database.MoveDocToFolder(d.Id, _m.UnitId, target); Refresh(); }));
        }
        move.Items.Add(new Separator());
        move.Items.Add(MenuItemFor("Nouveau dossier…", () =>
        {
            var name = Prompt("Déplacer vers un nouveau dossier", "Nom (A/B/C pour un sous-dossier) :", "");
            if (!string.IsNullOrWhiteSpace(name))
            {
                var f = ImportDialog.NormalizeFolder(name);
                App.Database.CreateFolder(_m.UnitId, f); App.Database.MoveDocToFolder(d.Id, _m.UnitId, f); Refresh();
            }
        }));
        menu.Items.Add(move);

        menu.Items.Add(MenuItemFor("Renommer", () =>
        {
            var nt = Prompt("Renommer le document", "Nouveau titre :", d.Title);
            if (!string.IsNullOrWhiteSpace(nt) && nt.Trim() != d.Title)
            { App.Database.RenameDocOnMachine(d.Id, _m.UnitId, nt.Trim()); Refresh(); }
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("Retirer de cette machine", () =>
        {
            App.Database.RemoveDocFromMachine(d.Id, _m.UnitId); Refresh();
        }));
        b.ContextMenu = menu;

        return b;
    }

    private static MenuItem MenuItemFor(string header, Action onClick)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => onClick();
        return mi;
    }

    // dialog de recherche/ajout de docs a la machine
    private void ShowAddDocDialog()
    {
        var win = new Window
        {
            Title = $"Ajouter un document à {_m.UnitId}", Width = 620, Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), Background = (Brush)FindResource("Bg")
        };
        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var search = new TextBox { Height = 30, Padding = new Thickness(6, 4, 6, 4), FontSize = 13 };
        Grid.SetRow(search, 0);
        grid.Children.Add(search);

        var results = new ListBox { Margin = new Thickness(0, 8, 0, 8), SelectionMode = SelectionMode.Extended };
        Grid.SetRow(results, 1);
        grid.Children.Add(results);

        // dossier cible pour les docs ajoutes
        var bottom = new StackPanel { Orientation = Orientation.Horizontal };
        bottom.Children.Add(new TextBlock { Text = "Dossier : ", VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Text") });
        var folderBox = new ComboBox { Width = 200, IsEditable = true, Margin = new Thickness(0, 0, 12, 0) };
        foreach (var f in App.Database.GetFoldersForMachine(_m.UnitId)) folderBox.Items.Add(f);
        folderBox.Text = "Autre";
        bottom.Children.Add(folderBox);
        var addBtn = new Button
        {
            Content = "  Ajouter la sélection  ", Height = 32, Padding = new Thickness(12, 0, 12, 0),
            Background = (Brush)FindResource("Accent"), Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand
        };
        bottom.Children.Add(addBtn);
        Grid.SetRow(bottom, 2);
        grid.Children.Add(bottom);

        // map affichage -> doc
        var map = new Dictionary<object, DocItem>();
        void RunSearch()
        {
            results.Items.Clear();
            map.Clear();
            foreach (var doc in App.Database.SearchDocsNotOnMachine(_m.UnitId, search.Text.Trim()))
            {
                var item = new ListBoxItem
                {
                    Content = $"{ExtGlyph(doc.Ext)}  {doc.Title}   —   {doc.RelPath}",
                    Foreground = (Brush)FindResource("Text")
                };
                map[item] = doc;
                results.Items.Add(item);
            }
        }
        search.TextChanged += (_, _) => RunSearch();
        RunSearch();

        addBtn.Click += (_, _) =>
        {
            var folder = string.IsNullOrWhiteSpace(folderBox.Text) ? "Autre" : folderBox.Text.Trim();
            if (folder != "Autre") App.Database.CreateFolder(_m.UnitId, folder);
            int n = 0;
            foreach (var sel in results.SelectedItems)
                if (map.TryGetValue(sel, out var doc))
                { App.Database.AddDocToMachine(doc.Id, _m.UnitId, folder == "Autre" ? null : folder); n++; }
            if (n > 0) { win.Close(); Refresh(); }
        };

        win.Content = grid;
        win.ShowDialog();
    }

    // petite boite de saisie modale (WPF n'a pas d'InputBox natif)
    private string? Prompt(string title, string label, string initial)
    {
        var win = new Window
        {
            Title = title, Width = 420, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("Bg")
        };
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("Text"), Margin = new Thickness(0, 0, 0, 6) });
        var tb = new TextBox { Text = initial, Height = 30, Padding = new Thickness(6, 4, 6, 4), FontSize = 13 };
        sp.Children.Add(tb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        string? result = null;
        var ok = new Button { Content = "  OK  ", Height = 30, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 0, 10, 0), Background = (Brush)FindResource("Accent"), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, IsDefault = true };
        var cancel = new Button { Content = "  Annuler  ", Height = 30, Padding = new Thickness(10, 0, 10, 0), Cursor = Cursors.Hand, IsCancel = true };
        ok.Click += (_, _) => { result = tb.Text; win.Close(); };
        cancel.Click += (_, _) => win.Close();
        row.Children.Add(ok);
        row.Children.Add(cancel);
        sp.Children.Add(row);
        win.Content = sp;
        tb.Focus();
        tb.SelectAll();
        win.ShowDialog();
        return result;
    }

    private static string ExtGlyph(string ext) => ext switch
    {
        "pdf" => "📕",
        "md" or "txt" => "📄",
        "jpg" or "jpeg" or "png" or "bmp" => "🖼",
        "xls" or "xlsx" or "csv" => "📊",
        "bat" or "ps1" => "⌨",
        _ => "📁"
    };
}
