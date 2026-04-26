using Bindito.Core;
using Clockwork.Data;
using Clockwork.Services;

namespace Clockwork;

[Context("Game")]
public class ClockworkConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<ClockworkRegistry>().AsSingleton();
        Bind<PartitionSnapshotService>().AsSingleton();
    }
}
