using Timberborn.InputSystem;
using Timberborn.TickSystem;
using UnityEngine;

namespace Graphs.UI;

/// Polls for the Shift+G chord each tick and toggles the graphs window.
/// We debounce on the G key's rising edge so holding the chord doesn't
/// rapid-fire the toggle.
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
        // Shift + G. We use Unity's `Input` directly — matches how the
        // in-game dev console detects its hotkey chord. Task 27 tightens
        // this by checking InputService to suppress hotkeys during text
        // input.
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool g = Input.GetKey(KeyCode.G);
        bool pressed = shift && g;
        if (pressed && !_prevPressed) _window.Toggle();
        _prevPressed = pressed;
    }
}
