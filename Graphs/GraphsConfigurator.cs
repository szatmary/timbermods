using Bindito.Core;
using Graphs.Metrics;
using Graphs.Metrics.Providers;

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
    }
}
