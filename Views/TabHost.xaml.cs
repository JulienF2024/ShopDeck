using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShopDeck.Views;

public class TabItemModel
{
    public string Key { get; set; } = "";        // identifiant unique pour la dedup
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool Pinned { get; set; }             // home non-fermable
    public UIElement Content { get; set; } = null!;
    public Action? OnClose { get; set; }         // ex: tuer un process
}

public partial class TabHost : UserControl
{
    private readonly List<TabItemModel> _tabs = new();
    private TabItemModel? _active;

    public TabHost() => InitializeComponent();

    // ajoute un onglet epingle (home). A appeler a l'init de chaque section.
    public void AddPinned(string key, string title, string icon, UIElement content)
    {
        var t = new TabItemModel { Key = key, Title = title, Icon = icon, Pinned = true, Content = content };
        _tabs.Add(t);
        Rebuild();
        if (_active == null) Activate(t);
    }

    // ouvre (ou focus si deja ouvert) un onglet dynamique
    public void OpenOrFocus(string key, string title, string icon, Func<UIElement> factory, Action? onClose = null)
    {
        var existing = _tabs.Find(t => t.Key == key);
        if (existing != null) { Activate(existing); return; }
        var t = new TabItemModel { Key = key, Title = title, Icon = icon, Content = factory(), OnClose = onClose };
        _tabs.Add(t);
        Rebuild();
        Activate(t);
    }

    private void Activate(TabItemModel t)
    {
        _active = t;
        Host.Child = t.Content;
        Rebuild();
    }

    private void Close(TabItemModel t)
    {
        if (t.Pinned) return;
        t.OnClose?.Invoke();
        var idx = _tabs.IndexOf(t);
        _tabs.Remove(t);
        if (_active == t)
        {
            var next = _tabs.Count > 0 ? _tabs[Math.Max(0, idx - 1)] : null;
            if (next != null) Activate(next); else Host.Child = null;
        }
        Rebuild();
    }

    // reconstruit visuellement la barre (simple et robuste vu le petit nombre d'onglets)
    private void Rebuild()
    {
        TabsBar.Items.Clear();
        foreach (var t in _tabs)
        {
            var active = t == _active;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = t.Icon, Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = t.Title, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Text") });

            if (!t.Pinned)
            {
                var x = new Button
                {
                    Content = "✕", FontSize = 11, Width = 20, Height = 20,
                    Margin = new Thickness(8, 0, 6, 0), Padding = new Thickness(0),
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand, Foreground = (Brush)FindResource("TextDim")
                };
                x.Click += (_, _) => Close(t);
                sp.Children.Add(x);
            }
            else sp.Children.Add(new TextBlock { Width = 10 });

            var b = new Border
            {
                Child = sp, Height = 40, CornerRadius = new CornerRadius(0),
                Background = active ? (Brush)FindResource("Bg") : (Brush)FindResource("Surface"),
                BorderBrush = (Brush)FindResource("Border"),
                BorderThickness = new Thickness(0, 0, 1, active ? 0 : 1),
                Cursor = Cursors.Hand
            };
            b.MouseLeftButtonUp += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) Activate(t);
            };
            // clic molette = fermer (confort browser)
            b.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) Close(t); };
            TabsBar.Items.Add(b);
        }
    }
}
