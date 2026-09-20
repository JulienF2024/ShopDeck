using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ShopDeck.Core;
using ShopDeck.Data;

namespace ShopDeck.Views;

// Ajout / edition d'une machine. Tape 040-130 -> l'ID 71HTR130 se deduit tout seul (et inversement).
public class MachineDialog : Window
{
    private readonly Machine? _edit;
    private readonly TextBox _unit = Field(), _legacy = Field(), _mfr = Field(), _model = Field(),
                             _year = Field(), _vin = Field(), _engine = Field(), _engineVin = Field(), _photo = Field();
    private readonly TextBox _notes = new() { Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(6, 4, 6, 4) };
    private bool _syncing;
    public bool Saved { get; private set; }
    public string? SavedUnitId { get; private set; }

    public MachineDialog(Window owner, Machine? edit = null)
    {
        _edit = edit;
        Owner = owner;
        Title = edit == null ? "Nouvelle machine" : $"Modifier {edit.UnitId}";
        Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Bg"];

        var g = new Grid { Margin = new Thickness(16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int row = 0;
        Row(g, row++, "ID unité *", _unit, "71HTR130");
        Row(g, row++, "ID legacy", _legacy, "040-130");
        Row(g, row++, "Marque", _mfr, "Caterpillar");
        Row(g, row++, "Modèle *", _model, "793F");
        Row(g, row++, "Année", _year, "2019");
        Row(g, row++, "VIN / S/N", _vin, "");
        Row(g, row++, "Moteur", _engine, "C175-16");
        Row(g, row++, "S/N moteur", _engineVin, "");

        // photo: chemin relatif a files\ ; bouton parcourir copie le fichier choisi dans files\photos
        var photoRow = new DockPanel();
        var browse = new Button { Content = "…", Width = 32, Height = 28, Margin = new Thickness(6, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
        browse.Click += (_, _) => PickPhoto();
        DockPanel.SetDock(browse, Dock.Right);
        photoRow.Children.Add(browse);
        photoRow.Children.Add(_photo);
        _photo.ToolTip = "Vide = photo auto par modèle (files\\photos\\<MODELE>.png)";
        Row(g, row++, "Photo", photoRow, null);
        Row(g, row++, "Notes", _notes, null);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = edit == null ? "  Créer  " : "  Enregistrer  ", Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0), Background = (Brush)Application.Current.Resources["Accent"], Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, IsDefault = true };
        var cancel = new Button { Content = "  Annuler  ", Height = 32, Padding = new Thickness(12, 0, 12, 0), Cursor = System.Windows.Input.Cursors.Hand, IsCancel = true };
        ok.Click += (_, _) => Save();
        btns.Children.Add(ok); btns.Children.Add(cancel);
        Grid.SetRow(btns, row); Grid.SetColumnSpan(btns, 2);
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(btns);

        Content = g;

        if (edit != null)
        {
            _unit.Text = edit.UnitId; _unit.IsReadOnly = true; _unit.Background = (Brush)Application.Current.Resources["Rail"];
            _legacy.Text = edit.LegacyId ?? "";
            var sp = edit.Model.IndexOf(' ');
            _mfr.Text = sp > 0 ? edit.Model[..sp] : "";
            _model.Text = sp > 0 ? edit.Model[(sp + 1)..] : edit.Model;
            _year.Text = edit.Year ?? ""; _vin.Text = edit.Vin ?? ""; _engine.Text = edit.Engine ?? "";
            _engineVin.Text = edit.EngineVin ?? ""; _photo.Text = edit.PhotoPath ?? ""; _notes.Text = edit.Notes ?? "";
        }
        else
        {
            _mfr.Text = "Caterpillar";
            _legacy.TextChanged += (_, _) => Sync(fromLegacy: true);
            _unit.TextChanged += (_, _) => Sync(fromLegacy: false);
        }
        (edit == null ? _legacy : _model).Focus();
    }

    private void Sync(bool fromLegacy)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            if (fromLegacy) { var n = UnitCodec.LegacyToNew(_legacy.Text.Trim()); if (n != null) _unit.Text = n; }
            else { var l = UnitCodec.NewToLegacy(_unit.Text.Trim().ToUpperInvariant()); if (l != null) _legacy.Text = l; }
        }
        finally { _syncing = false; }
    }

    private void PickPhoto()
    {
        var dlg = new OpenFileDialog { Title = "Photo de la machine", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var dir = Path.Combine(App.FilesRoot, "photos");
            Directory.CreateDirectory(dir);
            var name = (string.IsNullOrWhiteSpace(_unit.Text) ? Path.GetFileNameWithoutExtension(dlg.FileName) : _unit.Text.Trim()) + Path.GetExtension(dlg.FileName).ToLowerInvariant();
            var dest = Path.Combine(dir, name);
            if (!string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, dest, true);
            _photo.Text = Path.GetRelativePath(App.FilesRoot, dest);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Photo"); }
    }

    private void Save()
    {
        var unit = _unit.Text.Trim().ToUpperInvariant();
        var model = _model.Text.Trim();
        if (unit.Length == 0 || model.Length == 0) { MessageBox.Show(this, "ID unité et modèle sont obligatoires.", "Machine"); return; }
        if (_edit == null && App.Database.MachineExists(unit))
        {
            MessageBox.Show(this, $"{unit} existe déjà.", "Machine"); return;
        }
        var mfr = _mfr.Text.Trim();
        var m = new Machine
        {
            UnitId = unit,
            LegacyId = Nz(_legacy.Text),
            Family = FleetImporter.NormalizeFamily(model),
            Model = mfr.Length == 0 ? model : $"{mfr} {model}",
            Year = Nz(_year.Text), Vin = Nz(_vin.Text), Engine = Nz(_engine.Text), EngineVin = Nz(_engineVin.Text),
            PhotoPath = Nz(_photo.Text), Notes = Nz(_notes.Text)
        };
        App.Database.UpsertMachine(m);
        App.Database.UndeleteMachine(unit);   // recreer une machine supprimee = elle revit
        Saved = true; SavedUnitId = unit;
        Close();
    }

    private static string? Nz(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static TextBox Field() => new() { Height = 28, Padding = new Thickness(6, 3, 6, 3), FontSize = 13 };

    private static void Row(Grid g, int row, string label, UIElement ctl, string? hint)
    {
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["Text"], Margin = new Thickness(0, 0, 8, 6) };
        Grid.SetRow(l, row); Grid.SetColumn(l, 0);
        if (ctl is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 0, 6);
        if (ctl is TextBox tb && !string.IsNullOrEmpty(hint)) tb.ToolTip = "ex: " + hint;
        Grid.SetRow(ctl, row); Grid.SetColumn(ctl, 1);
        g.Children.Add(l); g.Children.Add(ctl);
    }
}
