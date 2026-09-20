using System.Windows.Controls;
using ShopDeck.Data;

namespace ShopDeck.Views;

// Section Programmes: TabHost avec home = lanceur. Lance console/gui en onglets paralleles.
public class ProgramsView : UserControl
{
    private readonly TabHost _tabs = new();
    private int _seq;   // suffixe pour permettre plusieurs instances du meme programme

    public ProgramsView()
    {
        Content = _tabs;
        var home = new LauncherHome();
        home.Launch += LaunchEntry;
        _tabs.AddPinned("home", "Lanceur", "🏠", home);
    }

    private void LaunchEntry(LauncherEntry e)
    {
        var key = $"prog:{e.Id}:{_seq++}";   // pas de dedup: on veut pouvoir en lancer plusieurs
        if (e.Kind == "console")
        {
            ConsoleTab? tab = null;
            _tabs.OpenOrFocus(key, e.Name, "🖥", () => { tab = new ConsoleTab(e.Target, e.Args, e.WorkingDir); return tab; },
                onClose: () => tab?.Kill());
        }
        else
        {
            GuiTab? tab = null;
            _tabs.OpenOrFocus(key, e.Name, "🪟", () => { tab = new GuiTab(e.Name, e.Target, e.Args, e.WorkingDir); return tab; },
                onClose: () => tab?.Kill());
        }
    }
}
