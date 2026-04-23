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

    public static Toggle CreateLocalizableToggle(bool initialValue)
    {
        if (_localizableToggleType != null &&
            Activator.CreateInstance(_localizableToggleType) is Toggle t)
        {
            // VisualElementLocalizer rejects null/empty _textLocKey — walk
            // the type hierarchy for any "_textLocKey" field and stuff a
            // dummy key into each. Its translation is never displayed
            // because the label comes from a sibling Label.
            SetTextLocKey(t, "Graphs.Placeholder");
            t.value = initialValue;
            return t;
        }
        return new Toggle { value = initialValue };
    }

    private static void SetTextLocKey(object target, string value)
    {
        var type = target.GetType();
        while (type != null)
        {
            var f = type.GetField("_textLocKey",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) { f.SetValue(target, value); return; }
            type = type.BaseType;
        }
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
