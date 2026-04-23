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

        // UI
        Bind<GraphsRangeSelector>().AsSingleton();
        Bind<GraphsDistrictSelector>().AsSingleton();
        Bind<GraphsWindow>().AsSingleton();
        Bind<GraphsHotkey>().AsSingleton();
    }
}
