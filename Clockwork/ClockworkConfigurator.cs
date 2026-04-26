using Bindito.Core;
using Clockwork.Data;

namespace Clockwork;

[Context("Game")]
public class ClockworkConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<ClockworkRegistry>().AsSingleton();
    }
}
