using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShopDeck.Core;

namespace ShopDeck.Views;

// Onglet Mise a jour: compare version locale vs derniere GitHub Release, telecharge + installe.
public class UpdateView : UserControl
{
    private readonly Updater _up = new();
    private readonly TextBlock _localVer;
    private readonly TextBlock _remoteVer;
    private readonly TextBlock _status;
    private readonly TextBox _notes;
    private readonly Button _check;
    private readonly Button _install;
    private readonly ProgressBar _bar;
    private Updater.ReleaseInfo? _pending;

    public UpdateView()
    {
        var root = new StackPanel { Margin = new Thickness(24), Background = (Brush)Application.Current.Resources["Bg"] };

        root.Children.Add(new TextBlock
        {
            Text = "Mise à jour de ShopDeck", FontSize = 20, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["Text"], Margin = new Thickness(0, 0, 0, 16)
        });

        _localVer = Row("Version installée :", _up.LocalVersion.ToString(3), root);
        _remoteVer = Row("Dernière version en ligne :", "— (clique « Vérifier »)", root);

        _bar = new ProgressBar { Height = 6, Margin = new Thickness(0, 12, 0, 8), Visibility = Visibility.Collapsed, Maximum = 1.0 };
        root.Children.Add(_bar);

        _status = new TextBlock
        {
            Text = "", Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextDim"]
        };
        root.Children.Add(_status);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        _check = new Button { Content = "🔄 Vérifier les mises à jour", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand };
        _check.Click += async (_, _) => await CheckAsync();
        _install = new Button { Content = "⬇ Installer et redémarrer", Padding = new Thickness(14, 7, 14, 7), Cursor = System.Windows.Input.Cursors.Hand, IsEnabled = false };
        _install.Click += async (_, _) => await InstallAsync();
        btns.Children.Add(_check); btns.Children.Add(_install);
        root.Children.Add(btns);

        root.Children.Add(new TextBlock
        {
            Text = "Notes de version", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4),
            Foreground = (Brush)Application.Current.Resources["TextDim"]
        });
        _notes = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)Application.Current.Resources["Surface"],
            Foreground = (Brush)Application.Current.Resources["Text"],
            BorderBrush = (Brush)Application.Current.Resources["Border"]
        };
        root.Children.Add(_notes);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private TextBlock Row(string label, string value, Panel parent)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        sp.Children.Add(new TextBlock { Text = label, Width = 200, Foreground = (Brush)Application.Current.Resources["TextDim"] });
        var val = new TextBlock { Text = value, Foreground = (Brush)Application.Current.Resources["Text"], FontWeight = FontWeights.SemiBold };
        sp.Children.Add(val);
        parent.Children.Add(sp);
        return val;
    }

    private async System.Threading.Tasks.Task CheckAsync()
    {
        _check.IsEnabled = false; _install.IsEnabled = false; _pending = null;
        _status.Text = "Connexion à GitHub…";
        try
        {
            var rel = await _up.CheckLatestAsync();
            if (rel == null)
            {
                _status.Text = "Impossible de lire les releases (pas de connexion, ou aucune release publiée, ou token manquant pour le repo privé).";
                _remoteVer.Text = "inconnue";
                return;
            }
            _remoteVer.Text = $"{rel.Version.ToString(3)}  ({rel.Tag})";
            _notes.Text = string.IsNullOrWhiteSpace(rel.Notes) ? "(aucune note)" : rel.Notes;

            if (_up.IsNewer(rel))
            {
                _pending = rel;
                _install.IsEnabled = true;
                var mo = rel.Size > 0 ? $" ({rel.Size / 1024.0 / 1024.0:0.#} Mo)" : "";
                _status.Text = $"✅ Nouvelle version disponible{mo}. Clique « Installer et redémarrer ».";
            }
            else
            {
                _status.Text = "✔ ShopDeck est à jour.";
            }
        }
        catch (Exception ex)
        {
            _status.Text = "Erreur : " + ex.Message;
        }
        finally { _check.IsEnabled = true; }
    }

    private async System.Threading.Tasks.Task InstallAsync()
    {
        if (_pending == null) return;
        var ok = MessageBox.Show(
            "ShopDeck va se fermer, se mettre à jour puis redémarrer.\nContinuer ?",
            "Mise à jour", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        _check.IsEnabled = false; _install.IsEnabled = false;
        _bar.Visibility = Visibility.Visible; _bar.Value = 0;
        _status.Text = "Téléchargement…";
        try
        {
            var prog = new Progress<double>(p => { _bar.Value = p; _status.Text = $"Téléchargement… {p * 100:0}%"; });
            var newExe = await _up.DownloadAndStageAsync(_pending, prog);
            _status.Text = "Installation… l'application va redémarrer.";
            _up.ApplyAndRestart(newExe);
        }
        catch (Exception ex)
        {
            _status.Text = "Échec : " + ex.Message;
            _bar.Visibility = Visibility.Collapsed;
            _check.IsEnabled = true; _install.IsEnabled = true;
        }
    }
}
