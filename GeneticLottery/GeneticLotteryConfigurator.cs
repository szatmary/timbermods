using Bindito.Core;

namespace GeneticLottery;

[Context("Game")]
public class GeneticLotteryConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<GeneticLotteryService>().AsSingleton();
    }
}
