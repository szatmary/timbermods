using Bindito.Core;

namespace DynamitePriority;

[Context("Game")]
public class DynamitePriorityConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<DynamitePriorityService>().AsSingleton();
    }
}
