using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShopDeck.Views;

// Programme console (ping, .bat, scripts): output live redirige dans un TextBox scrollable.
// Le process est tue quand l'onglet ferme (via OnClose du TabHost).
public class ConsoleTab : UserControl
{
    private readonly TextBox _out;
    private Process? _proc;
    private readonly string _target;
    private readonly string? _args;
    private readonly string? _wd;

    public ConsoleTab(string target, string? args, string? workingDir)
    {
        _target = target; _args = args; _wd = workingDir;

        var root = new DockPanel { Background = (Brush)Application.Current.Resources["Bg"] };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        var stop = MakeBtn("⏹ Stop"); stop.Click += (_, _) => Kill();
        var restart = MakeBtn("↻ Redémarrer"); restart.Click += (_, _) => { Kill(); Start(); };
        var clear = MakeBtn("🧹 Effacer"); clear.Click += (_, _) => _out.Clear();
        bar.Children.Add(stop); bar.Children.Add(restart); bar.Children.Add(clear);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        _out = new TextBox
        {
            IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd4)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap, BorderThickness = new Thickness(0),
            Margin = new Thickness(8, 0, 8, 8)
        };
        root.Children.Add(_out);

        Content = root;
        Start();
    }

    private Button MakeBtn(string txt) => new()
    {
        Content = txt, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 4, 10, 4),
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private void Start()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _target, Arguments = _args ?? "",
                WorkingDirectory = string.IsNullOrEmpty(_wd) ? "" : _wd,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (_, e) => Append(e.Data);
            _proc.ErrorDataReceived += (_, e) => Append(e.Data);
            _proc.Exited += (_, _) => Append($"\n[process terminé, code {_proc?.ExitCode}]");
            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
            Append($"[lancé: {_target} {_args}]\n");
        }
        catch (Exception ex) { Append($"[erreur de lancement] {ex.Message}"); }
    }

    private void Append(string? line)
    {
        if (line == null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _out.AppendText(line + "\n");
            _out.ScrollToEnd();
        });
    }

    public void Kill()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true); }
        catch { /* deja mort */ }
    }
}
