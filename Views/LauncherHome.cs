using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ShopDeck.Data;

namespace ShopDeck.Views;

// Home de la section Programmes: liste configurable par Julien (add/remove), lance en onglet.
public class LauncherHome : UserControl
{
    private readonly StackPanel _list;
    public event Action<LauncherEntry>? Launch;

    public LauncherHome()
    {
        var root = new DockPanel { Background = (Brush)Application.Current.Resources["Bg"] };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        var add = new Button { Content = "➕ Ajouter un programme", Padding = new Thickness(12, 6, 12, 6), Cursor = System.Windows.Input.Cursors.Hand };
        add.Click += (_, _) => AddDialog();
        bar.Children.Add(add);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        _list = new StackPanel { Margin = new Thickness(16, 0, 16, 16) };
        var sv = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(sv);

        Content = root;
        SeedIfEmpty();
        Reload();
    }

    // premier boot: propose les executables portables trouves dans files\programmes
    private void SeedIfEmpty()
    {
        if (App.Database.GetLaunchers().Count > 0) return;
        var progRoot = Path.Combine(App.FilesRoot, "programmes");
        if (!Directory.Exists(progRoot)) return;

        var found = new List<string>();
        try
        {
            foreach (var exe in Directory.EnumerateFiles(progRoot, "*.exe", SearchOption.AllDirectories))
            {
                var n = Path.GetFileName(exe).ToLowerInvariant();
                if (n.Contains("filezilla") || n.Contains("firefox") || n.Contains("remotetool") || n.Contains("putty"))
                    found.Add(exe);
                if (found.Count > 12) break;
            }
        }
        catch { }

        foreach (var exe in found)
            App.Database.AddLauncher(new LauncherEntry
            {
                Name = Path.GetFileNameWithoutExtension(exe),
                Category = "Portables",
                Target = exe,
                Kind = "gui"
            });
    }

    private void Reload()
    {
        _list.Children.Clear();
        var entries = App.Database.GetLaunchers();
        if (entries.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "Aucun programme. Clique « Ajouter un programme ».",
                Foreground = (Brush)Application.Current.Resources["TextDim"], Margin = new Thickness(4, 8, 0, 0)
            });
            return;
        }

        foreach (var grp in entries.GroupBy(e => string.IsNullOrEmpty(e.Category) ? "Autres" : e.Category))
        {
            _list.Children.Add(new TextBlock
            {
                Text = grp.Key, FontWeight = FontWeights.SemiBold, FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextDim"], Margin = new Thickness(0, 12, 0, 6)
            });
            foreach (var e in grp) _list.Children.Add(Card(e));
        }
    }

    private Border Card(LauncherEntry e)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var kindIcon = e.Kind == "console" ? "🖥" : "🪟";
        sp.Children.Add(new TextBlock { Text = kindIcon, FontSize = 18, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = e.Name, FontSize = 14, Foreground = (Brush)Application.Current.Resources["Text"] });
        info.Children.Add(new TextBlock { Text = e.Target, FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextDim"], TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520 });
        sp.Children.Add(info);

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var run = new Button { Content = "▶ Lancer", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand };
        run.Click += (_, _) => Launch?.Invoke(e);
        var del = new Button { Content = "🗑", Padding = new Thickness(8, 4, 8, 4), Cursor = System.Windows.Input.Cursors.Hand };
        del.Click += (_, _) => { App.Database.DeleteLauncher(e.Id); Reload(); };
        right.Children.Add(run); right.Children.Add(del);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(sp, 0); Grid.SetColumn(right, 1);
        grid.Children.Add(sp); grid.Children.Add(right);

        return new Border
        {
            Child = grid, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 6),
            Background = (Brush)Application.Current.Resources["Surface"],
            BorderBrush = (Brush)Application.Current.Resources["Border"], BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
    }

    private void AddDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choisir un programme",
            Filter = "Exécutables (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|Tous (*.*)|*.*"
        };
        var progRoot = Path.Combine(App.FilesRoot, "programmes");
        if (Directory.Exists(progRoot)) dlg.InitialDirectory = progRoot;
        if (dlg.ShowDialog() != true) return;

        var target = dlg.FileName;
        var ext = Path.GetExtension(target).ToLowerInvariant();
        var kind = (ext is ".bat" or ".cmd") ? "console" : "gui";

        App.Database.AddLauncher(new LauncherEntry
        {
            Name = Path.GetFileNameWithoutExtension(target),
            Category = "Mes programmes",
            Target = target,
            WorkingDir = Path.GetDirectoryName(target),
            Kind = kind
        });
        Reload();
    }
}
