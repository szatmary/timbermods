using System.Collections.Generic;
using System.Linq;
using Clockwork.Data;
using Clockwork.Services;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

public sealed class ClockworkPanel : ILoadableSingleton
{
    private readonly UILayout _uiLayout;
    private readonly PartitionSnapshotService _snapshots;
    private readonly ClockworkRegistry _registry;

    private VisualElement? _root;
    private ScrollView? _body;
    private bool _open;

    /// Track which partitions are expanded. Key is the partition's anchor id
    /// when named, else the lowest AutomatorId among its members.
    private readonly HashSet<string> _expanded = new(System.StringComparer.Ordinal);

    public ClockworkPanel(
        UILayout uiLayout,
        PartitionSnapshotService snapshots,
        ClockworkRegistry registry)
    {
        _uiLayout = uiLayout;
        _snapshots = snapshots;
        _registry = registry;
    }

    public void Load()
    {
        _root = Build();
        _root.style.display = DisplayStyle.None;
        _uiLayout.AddTopLeft(_root, 0);
        _registry.Changed += () => { if (_open) RefreshBody(); };
    }

    public void Toggle()
    {
        if (_root == null) return;
        _open = !_open;
        _root.style.display = _open ? DisplayStyle.Flex : DisplayStyle.None;
        if (_open) RefreshBody();
    }

    private void RefreshBody()
    {
        if (_body == null) return;
        _body.Clear();
        var snapshots = _snapshots.GetSnapshots();

        var named = new List<(PartitionSnapshot p, string name)>();
        var unnamed = new List<PartitionSnapshot>();
        foreach (var p in snapshots)
        {
            if (p.AnchorId != null && _registry.TryGet(p.AnchorId, out var name))
                named.Add((p, name));
            else
                unnamed.Add(p);
        }
        named.Sort((x, y) => System.StringComparer.OrdinalIgnoreCase.Compare(x.name, y.name));
        unnamed.Sort((x, y) => y.Automators.Count.CompareTo(x.Automators.Count));

        foreach (var (p, name) in named) AddPartition(p, name, p.AnchorId!);
        foreach (var p in unnamed)
        {
            string key = p.Automators.Count == 0
                ? ""
                : p.Automators.OrderBy(a => a.AutomatorId, System.StringComparer.Ordinal)
                    .First().AutomatorId;
            AddPartition(p, $"(Unnamed flow: {p.Wires.Count} wires)", key);
        }
    }

    private void AddPartition(PartitionSnapshot p, string label, string key)
    {
        bool expanded = _expanded.Contains(key);
        _body!.Add(PartitionRow.Build(p, label,
            onRename: newName =>
            {
                if (string.IsNullOrWhiteSpace(newName)) _registry.Remove(key);
                else _registry.Set(key, newName);
            },
            onToggleExpand: () =>
            {
                if (!_expanded.Add(key)) _expanded.Remove(key);
                RefreshBody();
            },
            expanded));
        if (expanded) _body.Add(PartitionExpandedView.Build(p));
    }

    private VisualElement Build()
    {
        var root = new VisualElement { name = "clockwork-panel" };
        root.style.width = 320;
        root.style.minHeight = 480;
        root.style.backgroundColor = new StyleColor(ClockworkColors.PanelBg);
        root.style.borderTopLeftRadius = 4;
        root.style.borderTopRightRadius = 4;
        root.style.borderBottomLeftRadius = 4;
        root.style.borderBottomRightRadius = 4;
        root.style.paddingTop = 8;
        root.style.paddingLeft = 8;
        root.style.paddingRight = 8;
        root.style.paddingBottom = 8;
        root.style.flexDirection = FlexDirection.Column;
        root.style.marginTop = 8;
        root.style.marginLeft = 8;

        var header = new Label("Clockworks");
        header.style.color = new StyleColor(ClockworkColors.HeaderText);
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.fontSize = 14;
        header.style.marginBottom = 8;
        root.Add(header);

        _body = new ScrollView { name = "clockwork-body" };
        _body.style.flexGrow = 1;
        _body.style.minHeight = 400;
        root.Add(_body);

        return root;
    }
}
