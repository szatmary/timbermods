using Bindito.Core;
using Graphs.Metrics;
using Graphs.Metrics.Providers;
using Graphs.UI;

namespace Graphs;

[Context("Game")]
public class GraphsConfigurator : Configurator
{
    protected override void Configure()
    {
        // Shared state
        Bind<DistrictFilter>().AsSingleton();

        // Weather + registry + sampler
        Bind<WeatherStateSampler>().AsSingleton();
        Bind<MetricRegistry>().AsSingleton();
        Bind<MetricSampler>().AsSingleton();

        // Providers (multibind as IMetricProvider)
        MultiBind<IMetricProvider>().To<ScienceMetricProvider>().AsSingleton();
        MultiBind<IMetricProvider>().To<PopulationMetricProvider>().AsSingleton();
        MultiBind<IMetricProvider>().To<GoodsMetricProvider>().AsSingleton();
        MultiBind<IMetricProvider>().To<WellbeingMetricProvider>().AsSingleton();
        // StatisticsMetricProvider kept in the codebase but not registered for
        // now — bring back when we have persistence and an event-based counter
        // that survives save/load.
        // MultiBind<IMetricProvider>().To<StatisticsMetricProvider>().AsSingleton();

        // UI
        Bind<GameIcons>().AsSingleton();
        Bind<GraphsRangeSelector>().AsSingleton();
        Bind<GraphsDistrictSelector>().AsSingleton();
        Bind<GraphsLegend>().AsSingleton();
        Bind<GraphsChart>().AsSingleton();
        Bind<GraphsWindow>().AsSingleton();
        Bind<GraphsHotkey>().AsSingleton();
    }
}
