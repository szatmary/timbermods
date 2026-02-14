using Timberborn.BlueprintSystem;

namespace TreeSpring;

public record TreeSpringBatterySpec : ComponentSpec
{
    [Serialize]
    public int Capacity { get; init; }
}
