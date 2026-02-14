using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.MechanicalSystem;

namespace TreeSpring;

public class TreeSpringAnimator : BaseComponent, IAwakableComponent, IFinishedStateListener
{
    private MechanicalNode _mechanicalNode = null!;

    public void Awake()
    {
        _mechanicalNode = GetComponent<MechanicalNode>();
    }

    public void OnEnterFinishedState()
    {
    }

    public void OnExitFinishedState()
    {
    }
}
