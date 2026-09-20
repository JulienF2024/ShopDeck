using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ShopDeck.Core;

namespace ShopDeck;

public partial class MainWindow : Window
{
    private readonly Updater _updater = new();
    private Updater.ReleaseInfo? _pendingRelease;

    public MainWindow()
    {
        InitializeComponent();
        // le check tourne apres l'affichage de la fenetre: jamais bloquant, offline = ignore en silence
        Loaded += async (_, _) =>
        {
            ShowWhatsNewIfUpdated();          // 100% local, avant tout appel reseau
            await CheckForUpdatesAsync();
        };
    }

    // si une MAJ vient d'etre appliquee (flag ecrit par l'updater + version confirmee), montre la page une fois
    private void ShowWhatsNewIfUpdated()
    {
        try
        {
            var news = _updater.ConsumePendingNews();
            if (news == null) return;
            var win = new Views.WhatsNewWindow(news) { Owner = this };
            win.ShowDialog();
        }
        catch { /* jamais bloquant */ }
    }

    private async System.Threading.Tasks.Task CheckForUpdatesAsync()
    {
        try
        {
            var rel = await _updater.CheckLatestAsync();
            // rel null = pas de reseau / GitHub injoignable / pas de release -> on ne montre rien
            if (rel == null || !_updater.IsNewer(rel)) return;

            _pendingRelease = rel;
            UpdateText.Text = $"Nouvelle version {rel.Tag} disponible (actuelle : v{_updater.LocalVersion.ToString(3)}).";
            UpdateBar.Visibility = Visibility.Visible;
        }
        catch { /* offline-safe: aucune erreur ne doit empecher d'utiliser l'app */ }
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingRelease == null) return;
        BtnUpdateNow.IsEnabled = false;
        BtnUpdateLater.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(p =>
                UpdateText.Text = $"Telechargement... {p * 100:0}%");
            var newExe = await _updater.DownloadAndStageAsync(_pendingRelease, progress);
            UpdateText.Text = "Redemarrage pour appliquer la mise a jour...";
            _updater.ApplyAndRestart(newExe);   // ferme l'app, le .bat relais remplace l'exe et relance
        }
        catch (Exception ex)
        {
            UpdateText.Text = "Echec de la mise a jour : " + ex.Message;
            BtnUpdateNow.IsEnabled = true;
            BtnUpdateLater.IsEnabled = true;
        }
    }

    private void UpdateLater_Click(object sender, RoutedEventArgs e)
        => UpdateBar.Visibility = Visibility.Collapsed;

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
