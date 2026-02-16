using Bindito.Core;

namespace DynamiteRubble;

[Context("Game")]
public class DynamiteRubbleConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<DynamiteRubbleService>().AsSingleton();
    }
}
