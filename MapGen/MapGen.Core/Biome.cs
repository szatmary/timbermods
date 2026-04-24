namespace MapGen;

/// Biomes used in WFC. "Start" is a post-WFC label, not a WFC vocabulary
/// entry, but we include it in the enum so downstream stages can switch on
/// the same type. WFC ignores any cell tagged as Start.
public enum Biome : byte
{
    Meadow = 0,
    Forest = 1,
    Badland = 2,
    Rocky = 3,
    Sea = 4,
    Crater = 5,
    Start = 6,
}

public static class Biomes
{
    /// The biome vocabulary WFC operates on (excludes Start).
    public static readonly Biome[] WfcSet =
    {
        Biome.Meadow, Biome.Forest, Biome.Badland,
        Biome.Rocky, Biome.Sea, Biome.Crater,
    };

    /// Target frequency per biome. Index matches WfcSet. Must sum to 1.0.
    /// Values drawn from spec section 1.
    public static readonly float[] Weights =
    {
        0.37f,  // Meadow
        0.25f,  // Forest
        0.20f,  // Badland
        0.15f,  // Rocky
        0.02f,  // Sea
        0.01f,  // Crater
    };

    /// Symmetric adjacency table. Allowed[a,b] = is biome a allowed to
    /// share an edge with biome b? Indexed by (byte)Biome.
    /// Sourced directly from spec section 1.
    public static readonly bool[,] Allowed = BuildAllowed();

    private static bool[,] BuildAllowed()
    {
        var a = new bool[7, 7];
        void Set(Biome x, Biome y, bool v) { a[(byte)x, (byte)y] = v; a[(byte)y, (byte)x] = v; }

        // Meadow row
        Set(Biome.Meadow, Biome.Meadow, true);
        Set(Biome.Meadow, Biome.Forest, true);
        Set(Biome.Meadow, Biome.Badland, true);
        Set(Biome.Meadow, Biome.Rocky, false);
        Set(Biome.Meadow, Biome.Sea, true);
        Set(Biome.Meadow, Biome.Crater, true);
        // Forest row
        Set(Biome.Forest, Biome.Forest, true);
        Set(Biome.Forest, Biome.Badland, false);
        Set(Biome.Forest, Biome.Rocky, true);
        Set(Biome.Forest, Biome.Sea, true);
        Set(Biome.Forest, Biome.Crater, true);
        // Badland row
        Set(Biome.Badland, Biome.Badland, true);
        Set(Biome.Badland, Biome.Rocky, true);
        Set(Biome.Badland, Biome.Sea, true);
        Set(Biome.Badland, Biome.Crater, true);
        // Rocky row
        Set(Biome.Rocky, Biome.Rocky, true);
        Set(Biome.Rocky, Biome.Sea, true);
        Set(Biome.Rocky, Biome.Crater, true);
        // Sea row
        Set(Biome.Sea, Biome.Sea, true);
        Set(Biome.Sea, Biome.Crater, true);
        // Crater row
        Set(Biome.Crater, Biome.Crater, false);   // Craters stay isolated
        return a;
    }

    public static bool IsAllowedAdjacent(Biome a, Biome b) => Allowed[(byte)a, (byte)b];
}
