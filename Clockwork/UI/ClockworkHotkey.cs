using Timberborn.TickSystem;
using UnityEngine.InputSystem;

namespace Clockwork.UI;

/// Polls for Shift+C each tick and toggles the Clockwork drawer.
public sealed class ClockworkHotkey : ITickableSingleton
{
    private readonly ClockworkPanel _panel;
    private bool _prevPressed;

    public ClockworkHotkey(ClockworkPanel panel)
    {
        _panel = panel;
    }

    public void Tick()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        bool shift = keyboard.shiftKey.isPressed;
        bool c = keyboard.cKey.isPressed;
        bool pressed = shift && c;
        if (pressed && !_prevPressed) _panel.Toggle();
        _prevPressed = pressed;
    }
}
