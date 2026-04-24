using System.Collections.Generic;

namespace MapGen;

public enum GenerationStatus { Success, Failed }

public sealed class GenerationResult
{
    public GenerationStatus Status { get; }
    public MapData? Map { get; }
    public uint ActualSeedUsed { get; }
    public int RetryCount { get; }
    public IReadOnlyList<string> Log { get; }
    public string? FailureReason { get; }

    public GenerationResult(GenerationStatus status, MapData? map, uint actualSeed,
        int retries, IReadOnlyList<string> log, string? failureReason = null)
    {
        Status = status;
        Map = map;
        ActualSeedUsed = actualSeed;
        RetryCount = retries;
        Log = log;
        FailureReason = failureReason;
    }
}
