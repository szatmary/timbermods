using Timberborn.BlockSystem;
using Timberborn.BuilderPrioritySystem;
using Timberborn.Explosions;
using TBPriority = Timberborn.PrioritySystem.Priority;
using Timberborn.SingletonSystem;

namespace DynamitePriority;

public class DynamitePriorityService : ILoadableSingleton
{
    private readonly EventBus _eventBus;

    public DynamitePriorityService(EventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public void Load()
    {
        _eventBus.Register(this);
    }

    [OnEvent]
    public void OnEnteredUnfinishedState(EnteredUnfinishedStateEvent e)
    {
        var blockObject = e.BlockObject;
        if (blockObject.GetComponent<Dynamite>() == null) return;

        var prioritizable = blockObject.GetComponent<BuilderPrioritizable>();
        prioritizable?.SetPriority(TBPriority.Low);
    }
}
