using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShopDeck.Data;

namespace ShopDeck.Views;

// Affiche un doc: image/texte in-app, PDF via lecteur systeme (viewer embarque = phase ulterieure).
public class DocViewer : UserControl
{
    public DocViewer(DocItem d)
    {
        Content = Build(d);
    }

    private UIElement Build(DocItem d)
    {
        var full = Path.Combine(App.FilesRoot, d.RelPath);
        if (!File.Exists(full))
            return Message($"Fichier introuvable:\n{full}");

        switch (d.Ext)
        {
            case "jpg": case "jpeg": case "png": case "bmp": case "gif":
                return ImageView(full);
            case "md": case "txt": case "log": case "csv":
                return TextView(full);
            case "pdf":
                // Sumatra embarque; PdfHost gere le fallback lecteur systeme si le runtime manque
                return PdfView(d, full);
            default:
                // xlsx, exe-adjacents: lecteur systeme du PC (Excel...)
                return ExternalPrompt(d, full);
        }
    }

    private UIElement PdfView(DocItem d, string full)
    {
        var grid = new Grid { Background = (Brush)FindResource("Bg") };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // mince barre: secours "ouvrir en externe" (impression, annotation avancee)
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Background = (Brush)FindResource("Surface")
        };
        var ext = new Button
        {
            Content = "⧉ Ouvrir en externe", Margin = new Thickness(8, 4, 8, 4),
            Padding = new Thickness(10, 4, 10, 4), Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = (Brush)FindResource("TextDim")
        };
        ext.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(full) { UseShellExecute = true }); }
            catch (Exception exn) { MessageBox.Show(exn.Message); }
        };
        bar.Children.Add(ext);
        Grid.SetRow(bar, 0);
        grid.Children.Add(bar);

        var host = new PdfHost(full);
        Grid.SetRow(host, 1);
        grid.Children.Add(host);
        return grid;
    }

    private UIElement ImageView(string path)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(path);
        bi.EndInit();
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)FindResource("Bg"),
            Content = new Image { Source = bi, Stretch = Stretch.None, Margin = new Thickness(10) }
        };
    }

    private UIElement TextView(string path)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) { return Message("Lecture impossible: " + ex.Message); }

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBox
            {
                Text = text, IsReadOnly = true, BorderThickness = new Thickness(0),
                TextWrapping = TextWrapping.Wrap, Padding = new Thickness(16),
                FontFamily = new FontFamily("Consolas"), FontSize = 13,
                Background = (Brush)FindResource("Surface"), Foreground = (Brush)FindResource("Text")
            }
        };
    }

    private UIElement ExternalPrompt(DocItem d, string full)
    {
        var sp = new StackPanel { Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock
        {
            Text = d.Title, FontSize = 18, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Text")
        });
        sp.Children.Add(new TextBlock
        {
            Text = $".{d.Ext.ToUpper()}  •  {d.SizeBytes / 1024:N0} Ko",
            Foreground = (Brush)FindResource("TextDim"), Margin = new Thickness(0, 4, 0, 16)
        });
        var open = new Button
        {
            Content = "  Ouvrir dans le lecteur du PC  ", Height = 38, Padding = new Thickness(14, 0, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Left, Cursor = System.Windows.Input.Cursors.Hand,
            Background = (Brush)FindResource("Accent"), Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        open.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(full) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        };
        sp.Children.Add(open);
        return sp;
    }

    private UIElement Message(string msg) => new TextBlock
    {
        Text = msg, Margin = new Thickness(24), Foreground = (Brush)FindResource("TextDim"),
        TextWrapping = TextWrapping.Wrap
    };
}
