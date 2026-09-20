using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ShopDeck.Views;

// pdf.js dans WebView2. Le PDF et le viewer sont servis via deux hosts virtuels (pas file://):
// meme origine requise par pdf.js pour fetch() le document, et file:// bloque les modules ES.
public class PdfJsHost : UserControl
{
    private const string ViewerHost = "shopdeck-pdfjs";
    private const string FilesHost = "shopdeck-files";

    private readonly string _pdfPath;
    private WebView2? _web;

    public PdfJsHost(string pdfPath)
    {
        _pdfPath = pdfPath;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static bool RuntimeAvailable()
    {
        try { return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
        catch { return false; }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_web != null) return;
        _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1e, 0x1e, 0x1e) };
        Content = _web;
        try
        {
            // profil sur la cle: zero trace sur le PC compagnie, et le PDF est lu directement
            // depuis G:\files sans copie (host virtuel -> dossier reel)
            var userData = Path.Combine(App.AppDir, "data", "webview2");
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping(ViewerHost, App.PdfJsDir, CoreWebView2HostResourceAccessKind.Allow);
            core.SetVirtualHostNameToFolderMapping(FilesHost, App.FilesRoot, CoreWebView2HostResourceAccessKind.Allow);
            // .mjs n'est pas enregistre dans le registre Windows -> WebView2 servirait un mauvais MIME,
            // et un module ES avec un mauvais MIME est refuse. On force le type nous-memes.
            core.AddWebResourceRequestedFilter($"https://{ViewerHost}/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += ForceModuleMime;

            var rel = Path.GetRelativePath(App.FilesRoot, _pdfPath).Replace('\\', '/');
            var fileUrl = $"https://{FilesHost}/{EscapePath(rel)}";
            core.Navigate($"https://{ViewerHost}/web/viewer.html?file={Uri.EscapeDataString(fileUrl)}");
        }
        catch (Exception ex)
        {
            Content = new TextBlock
            {
                Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap,
                Text = "WebView2 indisponible: " + ex.Message
            };
        }
    }

    private static void ForceModuleMime(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        if (!uri.AbsolutePath.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase)) return;
        var local = Path.Combine(App.PdfJsDir, Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Replace('/', '\\'));
        if (!File.Exists(local)) return;
        var env = ((CoreWebView2)sender!).Environment;
        var stream = File.OpenRead(local);
        e.Response = env.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: text/javascript");
    }

    private static string EscapePath(string rel)
    {
        var parts = rel.Split('/');
        for (int i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
        return string.Join('/', parts);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try { _web?.Dispose(); } catch { }
        _web = null;
        Content = null;
    }
}
