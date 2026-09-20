using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ShopDeck.Views;

// Sumatra COMPLET (toolbar, signets F12, menu, recherche) embarque par SetParent.
// Le mode -plugin est volontairement nu dans Sumatra (pas de toolbar ni TOC), donc on lance une
// fenetre normale, on lui retire son cadre et on la reparente dans notre HWND.
public class SumatraHost : HwndHost
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr val);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lp);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr h);
    private delegate bool EnumProc(IntPtr h, IntPtr lp);

    private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000;
    private const long WS_POPUP = 0x80000000L, WS_CAPTION = 0x00C00000L, WS_THICKFRAME = 0x00040000L,
                       WS_MINIMIZEBOX = 0x00020000L, WS_MAXIMIZEBOX = 0x00010000L, WS_SYSMENU = 0x00080000L;
    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const long WS_EX_APPWINDOW = 0x00040000L, WS_EX_WINDOWEDGE = 0x00000100L, WS_EX_DLGMODALFRAME = 0x00000001L;
    private const uint SWP_NOZORDER = 0x0004, SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040;
    private const int WM_SIZE = 0x0005, SW_HIDE = 0, SW_SHOW = 5;

    private readonly string _pdfPath;
    private IntPtr _hwnd, _child;
    private Process? _proc;

    public SumatraHost(string pdfPath) => _pdfPath = pdfPath;

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var (w, h) = PixelSize();
        _hwnd = CreateWindowEx(0, "static", "", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            0, 0, w, h, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Launch();
        return new HandleRef(this, _hwnd);
    }

    private void Launch()
    {
        if (!File.Exists(App.SumatraPath)) return;
        var appdata = string.IsNullOrEmpty(App.SumatraDataDir) ? "" : $"-appdata \"{App.SumatraDataDir}\" ";
        try
        {
            _proc = Process.Start(new ProcessStartInfo
            {
                FileName = App.SumatraPath,
                // -new-window: jamais recycle une instance existante -> 1 process par onglet, kill propre
                Arguments = $"{appdata}-new-window \"{_pdfPath}\"",
                UseShellExecute = false
            });
            var pid = (uint)_proc.Id;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                for (int i = 0; i < 200; i++)          // 200 x 25ms = 5s max
                {
                    await System.Threading.Tasks.Task.Delay(25);
                    var frame = FindFrame(pid);
                    if (frame == IntPtr.Zero) continue;
                    Dispatcher.Invoke(() => Adopt(frame));
                    break;
                }
            });
        }
        catch { _proc = null; }
    }

    // fenetre principale de Sumatra = classe SUMATRA_PDF_FRAME appartenant a notre process
    private static IntPtr FindFrame(uint pid)
    {
        var found = IntPtr.Zero;
        var sb = new StringBuilder(64);
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var p);
            if (p != pid) return true;
            sb.Clear();
            GetClassName(h, sb, sb.Capacity);
            if (sb.ToString() != "SUMATRA_PDF_FRAME") return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    // retire cadre/titre, passe en WS_CHILD et colle la fenetre dans notre zone
    private void Adopt(IntPtr frame)
    {
        if (_hwnd == IntPtr.Zero) return;
        _child = frame;
        ShowWindow(frame, SW_HIDE);
        var style = GetWindowLongPtr(frame, GWL_STYLE).ToInt64();
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        style |= WS_CHILD;
        SetWindowLongPtr(frame, GWL_STYLE, new IntPtr(style));
        var ex = GetWindowLongPtr(frame, GWL_EXSTYLE).ToInt64();
        ex &= ~(WS_EX_APPWINDOW | WS_EX_WINDOWEDGE | WS_EX_DLGMODALFRAME);
        SetWindowLongPtr(frame, GWL_EXSTYLE, new IntPtr(ex));
        SetParent(frame, _hwnd);
        var (w, h) = PixelSize();
        SetWindowPos(frame, IntPtr.Zero, 0, 0, w, h, SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        ShowWindow(frame, SW_SHOW);
    }

    // ActualWidth est en DIP: sur un ecran 125%/150% il faut convertir en pixels physiques
    private (int w, int h) PixelSize()
    {
        double sx = 1, sy = 1;
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            var m = src.CompositionTarget.TransformToDevice;
            sx = m.M11; sy = m.M22;
        }
        return (Math.Max(1, (int)(ActualWidth * sx)), Math.Max(1, (int)(ActualHeight * sy)));
    }

    private void ResizeChild()
    {
        if (_child == IntPtr.Zero) return;
        var (w, h) = PixelSize();
        MoveWindow(_child, 0, 0, w, h, true);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SIZE) ResizeChild();
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override void OnMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_child != IntPtr.Zero) SetFocus(_child);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Kill();
        DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    public void Kill()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true); } catch { }
        _proc = null; _child = IntPtr.Zero;
    }

    public bool Available => File.Exists(App.SumatraPath);
}
