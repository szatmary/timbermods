using System;
using System.Collections.Generic;
using System.Reflection;
using Timberborn.Beavers;
using Timberborn.BotUpkeep;
using Timberborn.Explosions;
using Timberborn.Forestry;
using Timberborn.Healthcare;
using Timberborn.InventoryNeedSystem;
using Timberborn.SingletonSystem;
using Timberborn.TailDecalSystem;
using Timberborn.TimeSystem;

namespace Graphs.Metrics.Providers;

/// Settlement-wide cumulative counters driven by game events.
/// The game's own `*StatisticCollector` classes are internal, so we can't
/// inject them. Instead we subscribe to the same events via EventBus and
/// maintain our own counters. These don't persist across save/load — they
/// reset to 0 on reload and only reflect what happens during the current
/// session.
public sealed class StatisticsMetricProvider : IMetricProvider, ILoadableSingleton
{
    private readonly EventBus _eventBus;

    private long _beaversBorn;
    private long _beaversExploded;
    private long _botsManufactured;
    private long _chippedTeeth;
    private long _daysPassed;
    private long _dynamiteDetonated;
    private long _tailsPainted;
    private long _treesCut;
    private long _waterConsumed;

    public StatisticsMetricProvider(EventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public void Load() => _eventBus.Register(this);

    public IEnumerable<MetricDefinition> GetMetrics()
    {
        yield return Stat("stat.daysPassed",          "Days passed",          () => _daysPassed);
        yield return Stat("stat.beaversBorn",         "Beavers born",         () => _beaversBorn);
        yield return Stat("stat.beaversExploded",     "Beavers exploded",     () => _beaversExploded);
        yield return Stat("stat.botsManufactured",    "Bots manufactured",    () => _botsManufactured);
        yield return Stat("stat.chippedTeeth",        "Chipped teeth",        () => _chippedTeeth);
        yield return Stat("stat.dynamiteDetonated",   "Dynamite detonated",   () => _dynamiteDetonated);
        yield return Stat("stat.tailsPainted",        "Tails painted",        () => _tailsPainted);
        yield return Stat("stat.treesCut",            "Trees cut",            () => _treesCut);
        yield return Stat("stat.waterConsumed",       "Water consumed",       () => _waterConsumed);
    }

    private static MetricDefinition Stat(string id, string displayName, Func<long> getter) =>
        new(id, displayName, MetricCategory.Statistics, MetricScope.Settlement,
            _ => getter());

    [OnEvent] public void OnBeaverBorn(BeaverBornEvent _)                          => _beaversBorn++;
    [OnEvent] public void OnMortalDiedFromExplosion(MortalDiedFromExplosionEvent _) => _beaversExploded++;
    [OnEvent] public void OnBotManufactured(BotManufacturedEvent _)                => _botsManufactured++;
    [OnEvent] public void OnTeethChipped(TeethChippedEvent _)                      => _chippedTeeth++;
    [OnEvent] public void OnDaytimeStart(DaytimeStartEvent _)                      => _daysPassed++;
    [OnEvent] public void OnDynamiteDetonated(DynamiteDetonatedEvent _)            => _dynamiteDetonated++;
    [OnEvent] public void OnTailDecalApplied(TailDecalAppliedEvent _)              => _tailsPainted++;
    [OnEvent] public void OnTreeCut(TreeCutEvent _)                                => _treesCut++;

    [OnEvent]
    public void OnGoodConsumed(GoodConsumedEvent evt)
    {
        // Only count water consumption; event also fires for other goods.
        var goodId = TryReadString(evt, "GoodId");
        if (goodId == "Water") _waterConsumed++;
    }

    private static string? TryReadString(object src, string name)
    {
        var t = src.GetType();
        var p = t.GetProperty(name) ?? t.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (p != null) return p.GetValue(src) as string;
        var f = t.GetField(name) ?? t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return f?.GetValue(src) as string;
    }
}
