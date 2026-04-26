using Bindito.Core;
using Clockwork.Data;
using Clockwork.Services;
using Clockwork.UI;
using Timberborn.BatchControl;

namespace Clockwork;

[Context("Game")]
public class ClockworkConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<ClockworkRegistry>().AsSingleton();
        Bind<PartitionSnapshotService>().AsSingleton();
        // Register the tab into the Manage Settlement (BatchControl) drawer.
        // Vanilla collects all BatchControlTab instances and shows them as tabs.
        MultiBind<BatchControlTab>().To<ClockworkBatchControlTab>().AsSingleton();
    }
}
