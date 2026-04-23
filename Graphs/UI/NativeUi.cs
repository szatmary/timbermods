using System;
using System.Reflection;
using Timberborn.CoreUI;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Factory helpers that instantiate game-native UIToolkit widgets whose types
/// are `internal` in Timberborn.CoreUI. We need these so `VisualElementInitializer`
/// can paint them with the game's sprites (checkbox glyph, nine-sliced button bg).
/// Reflection is a pragmatic workaround for the internal access; the types are
/// stable enough across patch versions for this to survive updates.
public static class NativeUi
{
    private static readonly Type? _localizableToggleType =
        typeof(VisualElementInitializer).Assembly.GetType("Timberborn.CoreUI.LocalizableToggle");

    private static readonly Type? _nineSliceButtonType =
        typeof(VisualElementInitializer).Assembly.GetType("Timberborn.CoreUI.NineSliceButton");

    private static readonly FieldInfo? _localizableToggleTextLocKeyField =
        _localizableToggleType?.GetField(
            "_textLocKey",
            BindingFlags.Instance | BindingFlags.NonPublic);

    public static Toggle CreateLocalizableToggle(bool initialValue)
    {
        if (_localizableToggleType != null)
        {
            if (Activator.CreateInstance(_localizableToggleType) is Toggle t)
            {
                // VisualElementLocalizer throws if _textLocKey is null; we
                // don't need a label on these toggles (the metric name lives
                // in a sibling Label), so seed it with an empty string.
                _localizableToggleTextLocKeyField?.SetValue(t, string.Empty);
                t.value = initialValue;
                return t;
            }
        }
        return new Toggle { value = initialValue };
    }

    public static Button CreateNineSliceButton(string label, Action onClick)
    {
        Button btn;
        if (_nineSliceButtonType != null &&
            Activator.CreateInstance(_nineSliceButtonType) is Button n)
        {
            btn = n;
        }
        else
        {
            btn = new Button();
        }
        btn.text = label;
        btn.clicked += onClick;
        return btn;
    }
}
