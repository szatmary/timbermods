using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.MechanicalSystem;
using UnityEngine;

namespace TreeSpring;

public class TreeSpringBattery : BaseComponent, IAwakableComponent, IFinishedStateListener, IBattery
{
    private MechanicalNode _mechanicalNode = null!;
    private TreeSpringBatterySpec _spec = null!;
    private float _charge;

    public void Awake()
    {
        _mechanicalNode = GetComponent<MechanicalNode>();
        _spec = GetComponent<TreeSpringBatterySpec>();
    }

    public void OnEnterFinishedState()
    {
        _mechanicalNode.SetNominalBatteryCapacity(_spec.Capacity);
        _mechanicalNode.SetNominalBatteryCharge(Mathf.RoundToInt(_charge));
    }

    public void OnExitFinishedState()
    {
    }

    public void ModifyCharge(float chargeDelta)
    {
        _charge = Mathf.Clamp(_charge + chargeDelta, 0f, _spec.Capacity);
        _mechanicalNode.SetNominalBatteryCharge(Mathf.RoundToInt(_charge));
    }
}
