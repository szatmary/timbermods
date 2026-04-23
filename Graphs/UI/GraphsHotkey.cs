using Timberborn.InputSystem;
using Timberborn.TickSystem;
using UnityEngine.InputSystem;

namespace Graphs.UI;

/// Polls for the Shift+G chord each tick and toggles the graphs window.
/// Uses Unity's new Input System — Timberborn disables the legacy Input
/// class in player settings, so `UnityEngine.Input.GetKey` throws at runtime.
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

    private bool _prevEscape;

    public void Tick()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Shift+G toggles the window.
        bool shift = keyboard.shiftKey.isPressed;
        bool g = keyboard.gKey.isPressed;
        bool pressed = shift && g;
        if (pressed && !_prevPressed) _window.Toggle();
        _prevPressed = pressed;

        // Esc while the window is open closes it. When the window is closed,
        // Esc falls through to the game's own handler (open pause menu).
        bool esc = keyboard.escapeKey.isPressed;
        if (esc && !_prevEscape && _window.IsOpen) _window.Close();
        _prevEscape = esc;
    }
}
