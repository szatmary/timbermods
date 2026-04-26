using Bindito.Core;
using Clockwork.Data;
using Clockwork.Services;
using Clockwork.UI;

namespace Clockwork;

[Context("Game")]
public class ClockworkConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<ClockworkRegistry>().AsSingleton();
        Bind<PartitionSnapshotService>().AsSingleton();
        Bind<ClockworkPanel>().AsSingleton();
        Bind<ClockworkHotkey>().AsSingleton();
    }
}
