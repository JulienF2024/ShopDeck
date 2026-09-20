using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ShopDeck.Views;

// Viewer PDF. Ordre: Sumatra embarque -> pdf.js/WebView2 -> lecteur du PC.
// Sumatra ignore le zoom des destinations /XYZ et /FitR au clic, mais Julien le prefere
// comme viewer principal; pdf.js reste en fallback si runtime\sumatra manque sur la cle.
public class PdfHost : ContentControl
{
    private readonly string _pdfPath;
    private SumatraHost? _sumatra;
    private PdfJsHost? _pdfjs;

    public PdfHost(string pdfPath)
    {
        _pdfPath = pdfPath;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_sumatra != null || _pdfjs != null) return;

        if (File.Exists(App.SumatraPath))
        {
            _sumatra = new SumatraHost(_pdfPath);
            Content = _sumatra;
            return;
        }

        if (File.Exists(Path.Combine(App.PdfJsDir, "web", "viewer.html")) && PdfJsHost.RuntimeAvailable())
        {
            _pdfjs = new PdfJsHost(_pdfPath);
            Content = _pdfjs;
            return;
        }

        Content = new TextBlock
        {
            Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap,
            Text = "Aucun viewer disponible (WebView2 absent du PC, runtime\\sumatra manquant sur la clé).\nOuverture dans le lecteur du PC..."
        };
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_pdfPath) { UseShellExecute = true }); } catch { }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // onglet ferme -> process Sumatra tue, aucun orphelin
        _sumatra?.Kill();
        _sumatra = null;
        _pdfjs = null;
        Content = null;
    }
}
