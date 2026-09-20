using System;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShopDeck.Views;

// 4 pings independants en grille 2x2. Chaque cellule = champ adresse + Start/Stop + console defilante.
// Comportement type "ping xxx -t": boucle jusqu'a Stop, une ligne par reponse.
public class PingView : UserControl
{
    public PingView()
    {
        var grid = new Grid { Background = (Brush)FindResource("Bg"), Margin = new Thickness(8) };
        for (int i = 0; i < 2; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
            {
                var cell = new PingCell(FindResource);
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        Content = grid;
    }

    private sealed class PingCell : Border
    {
        private readonly TextBox _name;
        private readonly TextBox _addr;
        private readonly Button _btn;
        private readonly TextBox _out;   // TextBox readonly = selection/copie possible, contrairement a TextBlock
        private CancellationTokenSource? _cts;
        private readonly Func<object, object> _res;

        public PingCell(Func<object, object> findResource)
        {
            _res = findResource;
            Margin = new Thickness(6);
            CornerRadius = new CornerRadius(8);
            Background = (Brush)_res("Surface");
            BorderBrush = (Brush)_res("Border");
            BorderThickness = new Thickness(1);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var bar = new Grid { Margin = new Thickness(8, 8, 8, 6) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });          // Nom: court, largeur fixe
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // IP: prend le reste
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _name = new TextBox
            {
                Height = 30, Padding = new Thickness(8, 4, 8, 4), FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = (Brush)_res("Bg"), Foreground = (Brush)_res("Text"),
                BorderBrush = (Brush)_res("Border")
            };
            _name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Toggle(); };
            Placeholder(_name, "Nom");
            Grid.SetColumn(_name, 0);

            _addr = new TextBox
            {
                Height = 30, Padding = new Thickness(8, 4, 8, 4), FontSize = 13, Margin = new Thickness(6, 0, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = (Brush)_res("Bg"), Foreground = (Brush)_res("Text"),
                BorderBrush = (Brush)_res("Border")
            };
            _addr.KeyDown += (_, e) => { if (e.Key == Key.Enter) Toggle(); };
            Placeholder(_addr, "Adresse ou IP  (ex: 8.8.8.8)");
            Grid.SetColumn(_addr, 1);

            _btn = new Button
            {
                Content = "Ping", Width = 76, Height = 30, Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand, FontSize = 13, BorderThickness = new Thickness(0),
                Background = (Brush)_res("Accent"), Foreground = Brushes.White
            };
            _btn.Click += (_, _) => Toggle();
            Grid.SetColumn(_btn, 2);

            bar.Children.Add(_name);
            bar.Children.Add(_addr);
            bar.Children.Add(_btn);
            Grid.SetRow(bar, 0);
            root.Children.Add(bar);

            _out = new TextBox
            {
                IsReadOnly = true, IsReadOnlyCaretVisible = false, TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"), FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
                BorderThickness = new Thickness(0), Padding = new Thickness(8, 4, 8, 8),
                Margin = new Thickness(8, 0, 8, 8)
            };
            Grid.SetRow(_out, 1);
            root.Children.Add(_out);

            Child = root;
        }

        private void Toggle()
        {
            if (_cts != null) { Stop(); return; }
            var host = HintActive(_addr) ? "" : _addr.Text.Trim();
            if (string.IsNullOrWhiteSpace(host)) return;
            var label = HintActive(_name) ? "" : _name.Text.Trim();

            _cts = new CancellationTokenSource();
            _name.IsEnabled = false;
            _addr.IsEnabled = false;
            _btn.Content = "Stop";
            _btn.Background = new SolidColorBrush(Color.FromRgb(0xD1, 0x3A, 0x3A));
            _out.Clear();
            var head = string.IsNullOrEmpty(label) ? host : $"{label}  [{host}]";
            Append($"Envoi d'une requête « ping » sur {head} :\n\n");
            _ = Loop(host, _cts.Token);
        }

        private void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _name.IsEnabled = true;
            _addr.IsEnabled = true;
            _btn.Content = "Ping";
            _btn.Background = (Brush)_res("Accent");
            Append("\nPing arrêté.\n");
        }

        // le hint est actif quand Tag==true (le texte affiche est le placeholder, pas une vraie saisie)
        private static bool HintActive(TextBox tb) => tb.Tag is true;

        // -t: boucle infinie ~1s d'intervalle. Stats cumulees affichees en continu.
        private async Task Loop(string host, CancellationToken ct)
        {
            int sent = 0, recv = 0;
            using var ping = new Ping();
            while (!ct.IsCancellationRequested)
            {
                sent++;
                try
                {
                    var r = await ping.SendPingAsync(host, 2000);
                    if (r.Status == IPStatus.Success)
                    {
                        recv++;
                        var ttl = r.Options?.Ttl.ToString() ?? "?";
                        Append($"Réponse de {r.Address} : octets=32 temps={r.RoundtripTime} ms TTL={ttl}\n");
                    }
                    else
                    {
                        Append($"Échec ({Translate(r.Status)})\n");
                    }
                }
                catch (Exception ex)
                {
                    // diag: on n'arrete jamais tout seul. Un host mort/DNS foireux fait partie de l'info,
                    // et le lien peut revenir a tout moment -> on continue de marteler.
                    Append($"Erreur : {ex.InnerException?.Message ?? ex.Message}\n");
                }
                Dispatcher.Invoke(() => _btn.ToolTip = $"Envoyés={sent}  Reçus={recv}  Perdus={sent - recv} ({(sent == 0 ? 0 : (sent - recv) * 100 / sent)}%)");

                try { await Task.Delay(1000, ct); } catch { }
            }
        }

        private static string Translate(IPStatus s) => s switch
        {
            IPStatus.TimedOut => "délai d'attente dépassé",
            IPStatus.DestinationHostUnreachable => "hôte inaccessible",
            IPStatus.DestinationNetworkUnreachable => "réseau inaccessible",
            _ => s.ToString()
        };

        private void Append(string text) => Dispatcher.Invoke(() =>
        {
            _out.AppendText(text);
            _out.ScrollToEnd();
        });

        // placeholder maison, etat par-TextBox dans Tag (true = hint affiche, pas une vraie saisie)
        private void Placeholder(TextBox tb, string hint)
        {
            void ShowHint() { tb.Text = hint; tb.Foreground = (Brush)_res("TextDim"); tb.Tag = true; }
            ShowHint();
            tb.GotFocus += (_, _) => { if (tb.Tag is true) { tb.Text = ""; tb.Foreground = (Brush)_res("Text"); tb.Tag = false; } };
            tb.LostFocus += (_, _) => { if (string.IsNullOrWhiteSpace(tb.Text)) ShowHint(); };
        }
    }
}
