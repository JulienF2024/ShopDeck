using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ShopDeck;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Rail_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewDocs == null || ViewProgs == null || ViewFiles == null || ViewPing == null) return; // pendant l'init XAML
        var rb = (RadioButton)sender;
        ViewDocs.Visibility = rb == RailDocs ? Visibility.Visible : Visibility.Collapsed;
        ViewProgs.Visibility = rb == RailProgs ? Visibility.Visible : Visibility.Collapsed;
        ViewFiles.Visibility = rb == RailFiles ? Visibility.Visible : Visibility.Collapsed;
        ViewPing.Visibility = rb == RailPing ? Visibility.Visible : Visibility.Collapsed;
    }

    // Capture d'ecran: outil Windows integre (Win10/11), aucune install, marche sans admin.
    // 1) ms-screenclip: = overlay Win+Shift+S (Capture et croquis)  2) fallback SnippingTool.exe classique
    private void Snip_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
            return;
        }
        catch { }
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SnippingTool.exe");
        try
        {
            if (File.Exists(legacy)) { Process.Start(new ProcessStartInfo(legacy) { UseShellExecute = true }); return; }
        }
        catch { }
        MessageBox.Show(this, "Aucun outil de capture trouvé sur ce PC.\nRaccourci Windows : Win + Shift + S", "Capture");
    }
}
