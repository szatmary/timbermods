using System.Linq;
using Timberborn.AssetSystem;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Adds a square-toggle-styled button to the top-right strip — sits next
/// to the vanilla "show construction guidelines" / "stockpile overlay" /
/// "natural resources" toggles. Click to open or close the Graphs window.
public sealed class GraphsTopBarButton : ILoadableSingleton
{
    private readonly UILayout _uiLayout;
    private readonly GraphsWindow _window;
    private readonly IAssetLoader _assets;

    public GraphsTopBarButton(UILayout uiLayout, GraphsWindow window, IAssetLoader assets)
    {
        _uiLayout = uiLayout;
        _window = window;
        _assets = assets;
    }

    public void Load()
    {
        var btn = new VisualElement { name = "graphs-top-right-button", pickingMode = PickingMode.Position };
        btn.AddToClassList("square-toggle");
        btn.style.width = 36;
        btn.style.height = 36;
        btn.style.marginLeft = 2;
        btn.style.marginRight = 2;
        btn.tooltip = "Graphs (Shift+G)";

        var baseSprite = LoadSprite("UI/Images/Game/square-toggle-base");
        if (baseSprite != null)
            btn.style.backgroundImage = new StyleBackground(baseSprite);

        // Foreground icon — overlay sprite that identifies the toggle.
        // science-icon reads as "stats / chart" in the existing UI vocabulary.
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.style.position = Position.Absolute;
        icon.style.left = 4;
        icon.style.right = 4;
        icon.style.top = 4;
        icon.style.bottom = 4;
        var iconSprite = LoadSprite("UI/Images/Game/science-icon");
        if (iconSprite != null)
            icon.style.backgroundImage = new StyleBackground(iconSprite);
        btn.Add(icon);

        btn.RegisterCallback<ClickEvent>(_ => _window.Toggle());

        // Order 2 places us alongside vanilla's top-right toggles. Low so we
        // sit next to construction-guidelines / natural-resources etc., not
        // pushed off-screen by a value the layout doesn't expect.
        _uiLayout.AddTopRightButton(btn, 2);
    }

    private Sprite? LoadSprite(string path)
    {
        foreach (var loaded in _assets.LoadAll<Sprite>(path))
            if (loaded.Asset != null) return loaded.Asset;
        return null;
    }
}
