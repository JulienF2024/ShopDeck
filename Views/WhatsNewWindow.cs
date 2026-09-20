using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShopDeck.Core;

namespace ShopDeck.Views;

// petite page affichee UNE fois apres qu'une mise a jour a ete appliquee et l'app redemarree.
// alimentee par le flag update_pending.json (Updater.ConsumePendingNews).
public class WhatsNewWindow : Window
{
    public WhatsNewWindow(Updater.PendingNews news)
    {
        Title = "ShopDeck — Quoi de neuf";
        Width = 560; Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["Bg"];

        var root = new DockPanel { Margin = new Thickness(24) };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new TextBlock
        {
            Text = "✅ Mise à jour installée", FontSize = 20, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["Text"], VerticalAlignment = VerticalAlignment.Center
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var ver = new TextBlock
        {
            Text = $"ShopDeck est maintenant en version {news.Version}  ({news.Tag})",
            Foreground = (Brush)Application.Current.Resources["TextDim"],
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(ver, Dock.Top);
        root.Children.Add(ver);

        var close = new Button
        {
            Content = "Continuer", Padding = new Thickness(20, 8, 20, 8),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0),
            Background = (Brush)Application.Current.Resources["Accent"], Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.SemiBold
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var nouv = new TextBlock
        {
            Text = "Nouveautés", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6),
            Foreground = (Brush)Application.Current.Resources["TextDim"]
        };
        DockPanel.SetDock(nouv, Dock.Top);
        root.Children.Add(nouv);

        var notes = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(news.Notes) ? "(aucune note fournie pour cette version)" : news.Notes,
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            BorderThickness = new Thickness(1), Padding = new Thickness(10),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)Application.Current.Resources["Surface"],
            Foreground = (Brush)Application.Current.Resources["Text"],
            BorderBrush = (Brush)Application.Current.Resources["Border"]
        };
        // le TextBox prend l'espace restant entre le titre "Nouveautes" (haut) et le bouton (bas)
        var scrollHost = new Border { Child = notes };
        root.Children.Add(scrollHost);

        Content = root;
    }
}
