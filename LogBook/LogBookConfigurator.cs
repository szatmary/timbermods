using Bindito.Core;
using LogBook.Metrics;
using LogBook.Metrics.Providers;
using LogBook.UI;

namespace LogBook;

[Context("Game")]
public class LogBookConfigurator : Configurator
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
        Bind<GameIcons>().AsSingleton();
        Bind<LogBookRangeSelector>().AsSingleton();
        Bind<LogBookDistrictSelector>().AsSingleton();
        Bind<LogBookLegend>().AsSingleton();
        Bind<LogBookChart>().AsSingleton();
        Bind<LogBookWindow>().AsSingleton();
        Bind<LogBookHotkey>().AsSingleton();
        Bind<LogBookTopBarButton>().AsSingleton();
    }
}
