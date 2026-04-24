namespace MapGen;

/// Inputs to MapGenerator. Not hardcoded so future UI / CLI can override
/// particular subsystems' parameters without recompiling.
public sealed class GenerationConfig
{
    public int Width { get; init; } = 128;
    public int Height { get; init; } = 128;
    public uint Seed { get; init; } = 1;

    /// Retry budget — seed-increment restarts allowed before surfacing
    /// a hard error. Per spec §9.
    public int PipelineRetryBudget { get; init; } = 5;

    public int MetaCellSize { get; init; } = 8;

    public void Validate()
    {
        if (Width < 16 || Height < 16)
            throw new System.ArgumentOutOfRangeException(
                nameof(Width), "Map dimensions must be at least 16x16.");
        if (Width % MetaCellSize != 0 || Height % MetaCellSize != 0)
            throw new System.ArgumentException(
                $"Width and Height must be multiples of MetaCellSize ({MetaCellSize}).");
    }
}
