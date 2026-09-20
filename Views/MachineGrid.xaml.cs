using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShopDeck.Data;

namespace ShopDeck.Views;

public partial class MachineGrid : UserControl
{
    private List<Machine> _all = new();
    public event Action<Machine>? MachineOpened;

    public MachineGrid()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    public void Reload()
    {
        _all = App.Database.GetMachines();
        Render(_all);
    }

    private void ImportBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ImportDialog(Window.GetWindow(this));
        dlg.ShowDialog();
        if (dlg.Imported) Reload();
    }

    private void AddMachineBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new MachineDialog(Window.GetWindow(this));
        dlg.ShowDialog();
        if (dlg.Saved) { Reload(); SearchBox.Text = dlg.SavedUnitId ?? ""; }
    }

    private void EditMachine(Machine m)
    {
        var dlg = new MachineDialog(Window.GetWindow(this), m);
        dlg.ShowDialog();
        if (dlg.Saved) Reload();
    }

    private void DeleteMachine(Machine m)
    {
        var n = App.Database.GetDocsForMachine(m.UnitId).Count;
        var msg = $"Supprimer {m.UnitId} ({m.Model}) ?\n\n{n} document(s) lié(s) seront détachés de cette machine.\nLes fichiers restent sur la clé et sur les autres machines.";
        if (MessageBox.Show(Window.GetWindow(this), msg, "Supprimer la machine", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        App.Database.DeleteMachine(m.UnitId);
        Reload();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        Placeholder.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        if (string.IsNullOrEmpty(q)) { Render(_all); return; }

        var filtered = _all.Where(m =>
            Contains(m.UnitId, q) || Contains(m.LegacyId, q) || Contains(m.Model, q) ||
            Contains(m.Family, q) || Contains(m.Vin, q) || Contains(m.Engine, q)).ToList();
        Render(filtered);
    }

    private static bool Contains(string? s, string q) =>
        !string.IsNullOrEmpty(s) && s.Contains(q, StringComparison.OrdinalIgnoreCase);

    private void Render(List<Machine> machines)
    {
        Cards.Items.Clear();
        foreach (var m in machines)
            Cards.Items.Add(BuildCard(m));
    }

    private Border BuildCard(Machine m)
    {
        var photo = new Border
        {
            Height = 120, CornerRadius = new CornerRadius(6, 6, 0, 0),
            Background = (Brush)FindResource("Rail")
        };
        var img = ResolvePhoto(m);
        if (img != null)
            photo.Child = new Image { Source = img, Stretch = Stretch.UniformToFill };
        else
            photo.Child = new TextBlock
            {
                Text = FamilyGlyph(m.Family), FontSize = 46,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextDim")
            };

        var info = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
        info.Children.Add(new TextBlock
        {
            Text = m.UnitId, FontWeight = FontWeights.SemiBold, FontSize = 15,
            Foreground = (Brush)FindResource("Text")
        });
        info.Children.Add(new TextBlock
        {
            Text = m.Model, FontSize = 12, Foreground = (Brush)FindResource("TextDim"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var legacy = string.IsNullOrEmpty(m.LegacyId) ? "" : m.LegacyId;
        info.Children.Add(new TextBlock
        {
            Text = legacy, FontSize = 11, Foreground = (Brush)FindResource("Accent")
        });

        var stack = new StackPanel();
        stack.Children.Add(photo);
        stack.Children.Add(info);

        var card = new Border
        {
            Width = 190, Margin = new Thickness(0, 0, 14, 14),
            Background = (Brush)FindResource("Surface"),
            BorderBrush = (Brush)FindResource("Border"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Child = stack, Cursor = Cursors.Hand
        };
        card.MouseLeftButtonUp += (_, _) => MachineOpened?.Invoke(m);

        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "✏ Modifier…" }; edit.Click += (_, _) => EditMachine(m);
        var del = new MenuItem { Header = "🗑 Supprimer la machine" }; del.Click += (_, _) => DeleteMachine(m);
        menu.Items.Add(edit); menu.Items.Add(new Separator()); menu.Items.Add(del);
        card.ContextMenu = menu;
        return card;
    }

    private BitmapImage? ResolvePhoto(Machine m)
    {
        string? full = null;
        // 1. PhotoPath explicite si defini
        if (!string.IsNullOrEmpty(m.PhotoPath))
            full = Path.IsPathRooted(m.PhotoPath) ? m.PhotoPath : Path.Combine(App.FilesRoot, m.PhotoPath);
        // 2. sinon resolution auto par modele: files\photos\<Model>.png (nom sanitize)
        if (full == null || !File.Exists(full))
        {
            var dir = Path.Combine(App.FilesRoot, "photos");
            // le modele en DB est "Marque Modele" (ex "Caterpillar 793F") mais les photos
            // sont nommees par modele seul (793F.png) -> essaie plusieurs cles
            foreach (var key in PhotoKeys(m.Model))
            {
                foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
                {
                    var cand = Path.Combine(dir, key + ext);
                    if (File.Exists(cand)) { full = cand; goto found; }
                }
            }
            found: ;
        }
        if (full == null || !File.Exists(full)) return null;
        try
        {
            // StreamSource au lieu de UriSource: plus fiable sur cle USB + libere le fichier
            var bytes = File.ReadAllBytes(full);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(bytes);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    // genere les cles candidates: modele complet, puis sans le 1er mot (la marque)
    private static IEnumerable<string> PhotoKeys(string model)
    {
        var full = SanitizeModel(model);
        if (!string.IsNullOrEmpty(full)) yield return full;
        var sp = (model ?? "").Trim().IndexOf(' ');
        if (sp > 0)
        {
            var noBrand = SanitizeModel(model.Substring(sp + 1));
            if (!string.IsNullOrEmpty(noBrand) && noBrand != full) yield return noBrand;
        }
    }

    // meme regle de nommage que le script de download: modele -> nom de fichier
    private static string SanitizeModel(string model)
    {
        var s = (model ?? "").Trim().ToUpperInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString();
    }

    private static string FamilyGlyph(string family) => family switch
    {
        "793F" => "🚛",
        "PV-235" => "🛠",
        "D10T" => "🚜",
        "Grader" => "🛣",
        _ => "⚙"
    };
}
