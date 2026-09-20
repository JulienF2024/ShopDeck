using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShopDeck.Views;

// Programme GUI (FileZilla, RemoteTools, CAT ET...): ouvre sa propre fenetre.
// L'onglet est une telecommande: statut, PID, uptime, boutons front/kill/restart.
// (Le mode thumbnail DWM + tuilage viendra en phase ulterieure.)
public class GuiTab : UserControl
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    private Process? _proc;
    private readonly string _target, _name;
    private readonly string? _args, _wd;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _timer;
    private DateTime _startedAt;

    public GuiTab(string name, string target, string? args, string? workingDir)
    {
        _name = name; _target = target; _args = args; _wd = workingDir;

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = name, FontSize = 20, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["Text"], Margin = new Thickness(0, 0, 0, 12)
        });

        _status = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"), FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["TextDim"], Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(_status);

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        var front = MakeBtn("⬆ Amener au front"); front.Click += (_, _) => BringToFront();
        var restart = MakeBtn("↻ Redémarrer"); restart.Click += (_, _) => { Kill(); Start(); };
        var kill = MakeBtn("⏹ Tuer"); kill.Click += (_, _) => Kill();
        bar.Children.Add(front); bar.Children.Add(restart); bar.Children.Add(kill);
        root.Children.Add(bar);

        Content = root;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Start();
    }

    private Button MakeBtn(string txt) => new()
    {
        Content = txt, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6),
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
                UseShellExecute = true
            };
            _proc = Process.Start(psi);
            _startedAt = DateTime.Now;
        }
        catch (Exception ex) { _status.Text = $"erreur de lancement: {ex.Message}"; }
        Refresh();
    }

    private void BringToFront()
    {
        try
        {
            _proc?.Refresh();
            var h = _proc?.MainWindowHandle ?? IntPtr.Zero;
            if (h != IntPtr.Zero) { ShowWindow(h, SW_RESTORE); SetForegroundWindow(h); }
        }
        catch { }
    }

    private void Refresh()
    {
        if (_proc == null) { _status.Text = "non lancé"; return; }
        _proc.Refresh();
        if (_proc.HasExited)
        {
            _status.Text = $"● arrêté (code {_proc.ExitCode})";
            return;
        }
        var up = DateTime.Now - _startedAt;
        _status.Text = $"● en cours  |  PID {_proc.Id}  |  uptime {up:hh\\:mm\\:ss}";
    }

    public void Kill()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true); }
        catch { }
        _timer.Stop();
    }
}
