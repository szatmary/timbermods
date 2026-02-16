using Timberborn.BonusSystem;
using Timberborn.Characters;
using Timberborn.SingletonSystem;

namespace GeneticLottery;

public class GeneticLotteryService : ILoadableSingleton
{
    private readonly EventBus _eventBus;
    private readonly System.Random _random = new();

    private const string LifeExpectancyBonusId = "LifeExpectancy";

    public GeneticLotteryService(EventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public void Load()
    {
        _eventBus.Register(this);
    }

    [OnEvent]
    public void OnCharacterCreated(CharacterCreatedEvent e)
    {
        var beaver = e.Character.GetComponent<Timberborn.Beavers.Beaver>();
        if (beaver == null) return;

        var bonusManager = e.Character.GetComponent<BonusManager>();
        if (bonusManager == null) return;

        // Random delta between -0.10 and +0.10
        float delta = (float)(_random.NextDouble() * 0.20 - 0.10);
        bonusManager.AddBonus(LifeExpectancyBonusId, delta);
    }
}
