using Timberborn.CoreUI;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Adds a small wooden-frame button to the top-right corner of the UI that
/// toggles the Graphs window. Less disruptive than a full-screen-blocking
/// hotkey-only entry point — players see the button and can find it.
public sealed class GraphsTopBarButton : ILoadableSingleton
{
    private readonly UILayout _uiLayout;
    private readonly GraphsWindow _window;

    public GraphsTopBarButton(UILayout uiLayout, GraphsWindow window)
    {
        _uiLayout = uiLayout;
        _window = window;
    }

    public void Load()
    {
        // NineSliceButton is the wooden-frame styled button vanilla uses
        // throughout. Inherits Unity's Button so `clicked` works directly.
        var btn = new NineSliceButton { text = "📈" };
        btn.tooltip = "Graphs (Shift+G)";
        btn.style.minWidth = 36;
        btn.style.height = 32;
        btn.style.fontSize = 16;
        btn.clicked += _window.Toggle;
        // Order picks position among other top-right buttons. Higher = further
        // right; choose a high number so we don't displace vanilla buttons.
        _uiLayout.AddTopRightButton(btn, 100);
    }
}
