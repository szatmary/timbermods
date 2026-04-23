namespace Graphs.UI;

/// Stub filled in by Task 18. Keeps the hotkey compilable.
public sealed class GraphsWindow
{
    public bool IsOpen { get; private set; }
    public void Toggle() { IsOpen = !IsOpen; }
    public void Open()  { IsOpen = true;  }
    public void Close() { IsOpen = false; }
}
