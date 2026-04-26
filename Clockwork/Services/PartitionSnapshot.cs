using System;
using System.Collections.Generic;
using UnityEngine;

namespace Clockwork.Services;

/// One automation flow. `Automators` are the buildings in the partition;
/// `Wires` are the directed connections between them. The UI iterates
/// these without re-touching vanilla types each frame.
public sealed class PartitionSnapshot
{
    public List<AutomatorView> Automators { get; } = new();
    public List<WireView> Wires { get; } = new();

    /// True if any transmitter in the partition is currently asserting.
    public bool Asserting;

    /// First anchor id (per ClockworkRegistry) contained in this partition,
    /// or null if no member is an anchor.
    public string? AnchorId;
}

public sealed class AutomatorView
{
    public string AutomatorId = "";
    public string DisplayName = "";       // vanilla AutomatorName, or fallback
    public AutomatorRole Role;
    public bool Asserting;
    public Vector3 WorldPosition;
}

[Flags]
public enum AutomatorRole
{
    None = 0,
    Emitter = 1,
    Receiver = 2,
    Gate = Emitter | Receiver,
}

public sealed class WireView
{
    public string FromAutomatorId = "";
    public string ToAutomatorId = "";
    public bool Asserting;
}
