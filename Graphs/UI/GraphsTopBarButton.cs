using Timberborn.CoreUI;
using Timberborn.SingletonSystem;
using Timberborn.TooltipSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Square-toggle in the top-right strip alongside vanilla's
/// construction-guidelines / stockpile-overlay / natural-resources buttons.
/// Reuses the vanilla `Common/SquareToggle` template for sizing, frame
/// sprites, and hover behavior; the chart icon is painted on the toggle's
/// checkmark element. Tooltip routes through the game's tooltip service.
public sealed class GraphsTopBarButton : ILoadableSingleton
{
    private readonly UILayout _uiLayout;
    private readonly GraphsWindow _window;
    private readonly VisualElementLoader _loader;
    private readonly ITooltipRegistrar _tooltipRegistrar;

    // Warm tan that vanilla square-toggle icons paint in.
    private static readonly Color IconTint = new(0.729f, 0.627f, 0.420f);  // #baa06b

    // Held so Unity doesn't reap the runtime-generated texture/sprite once
    // they're attached to the checkmark.
    private Sprite? _iconSprite;

    public GraphsTopBarButton(
        UILayout uiLayout,
        GraphsWindow window,
        VisualElementLoader loader,
        ITooltipRegistrar tooltipRegistrar)
    {
        _uiLayout = uiLayout;
        _window = window;
        _loader = loader;
        _tooltipRegistrar = tooltipRegistrar;
    }

    public void Load()
    {
        var root = _loader.LoadVisualElement("Common/SquareToggle");
        var toggle = root.Q<Toggle>("Toggle");

        // Vanilla `.square-toggle--<name>` USS sets background-image on the
        // checkmark descendant; we override inline with the procedural icon.
        var checkmark = toggle.Q(className: "unity-toggle__checkmark");
        if (checkmark != null)
        {
            _iconSprite = GraphIcon.Create();
            checkmark.style.backgroundImage = new StyleBackground(_iconSprite);
            checkmark.style.unityBackgroundImageTintColor = new StyleColor(IconTint);
        }

        _tooltipRegistrar.Register(root, "Graphs (Shift+G)");

        // The toggle's checked state is incidental — the window owns its
        // own open/closed flag.
        toggle.RegisterValueChangedCallback(_ => _window.Toggle());

        _uiLayout.AddTopRightButton(root, 2);
    }
}
