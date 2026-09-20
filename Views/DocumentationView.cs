using System.Windows.Controls;
using ShopDeck.Data;

namespace ShopDeck.Views;

// Section Documentation: TabHost avec home = grille machines. Ouvre fiches et docs en onglets.
public class DocumentationView : UserControl
{
    private readonly TabHost _tabs = new();

    public DocumentationView()
    {
        Content = _tabs;

        var grid = new MachineGrid();
        grid.MachineOpened += OpenMachine;
        _tabs.AddPinned("home", "Machines", "🏠", grid);
    }

    private void OpenMachine(Machine m)
    {
        _tabs.OpenOrFocus($"machine:{m.UnitId}", m.UnitId, "🚛", () =>
        {
            var page = new MachinePage(m);
            page.DocOpened += OpenDoc;
            return page;
        });
    }

    private void OpenDoc(DocItem d)
    {
        _tabs.OpenOrFocus($"doc:{d.Id}", d.Title, "📄", () => new DocViewer(d));
    }
}
