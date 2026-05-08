using Timberborn.InputSystem;
using Timberborn.TickSystem;
using UnityEngine.InputSystem;

namespace Graphs.UI;

/// Polls for Shift+G each tick and toggles the graphs window.
/// Uses Unity's new Input System — Timberborn disables the legacy Input
/// class in player settings, so `UnityEngine.Input.GetKey` throws at runtime.
/// Esc handling lives on the panel: once pushed, PanelStack routes Esc to
/// our IPanelController.OnUICancelled().
public sealed class GraphsHotkey : ITickableSingleton
{
    private readonly InputService _input;
    private readonly GraphsWindow _window;
    private bool _prevPressed;

    public GraphsHotkey(InputService input, GraphsWindow window)
    {
        _input = input;
        _window = window;
    }

    public void Tick()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        bool shift = keyboard.shiftKey.isPressed;
        bool g = keyboard.gKey.isPressed;
        bool pressed = shift && g;
        if (pressed && !_prevPressed) _window.Toggle();
        _prevPressed = pressed;
    }
}
