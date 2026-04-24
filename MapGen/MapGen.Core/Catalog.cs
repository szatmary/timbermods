using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MapGen;

public class CatalogEntry
{
    public string Key { get; set; } = "";
    public string BlueprintKey { get; set; } = "";
    public float Weight { get; set; } = 1f;
}

public sealed class RuinCatalogEntry : CatalogEntry
{
    public int FootprintW { get; set; } = 1;
    public int FootprintH { get; set; } = 1;
}

public sealed class Catalog
{
    public IReadOnlyList<CatalogEntry> Trees { get; set; } = new List<CatalogEntry>();
    public IReadOnlyList<CatalogEntry> Resources { get; set; } = new List<CatalogEntry>();
    public IReadOnlyList<CatalogEntry> Thorns { get; set; } = new List<CatalogEntry>();
    public IReadOnlyList<RuinCatalogEntry> Ruins { get; set; } = new List<RuinCatalogEntry>();
    public Dictionary<string, CatalogEntry> BlockObjects { get; set; } = new();

    public static Catalog LoadFromDirectory(string dir)
    {
        var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return new Catalog
        {
            Trees = Read<List<CatalogEntry>>(Path.Combine(dir, "Trees.json"), opt),
            Resources = Read<List<CatalogEntry>>(Path.Combine(dir, "Resources.json"), opt),
            Thorns = Read<List<CatalogEntry>>(Path.Combine(dir, "Thorns.json"), opt),
            Ruins = Read<List<RuinCatalogEntry>>(Path.Combine(dir, "Ruins.json"), opt),
            BlockObjects = Read<Dictionary<string, CatalogEntry>>(Path.Combine(dir, "BlockObjects.json"), opt),
        };
    }

    private static T Read<T>(string path, JsonSerializerOptions opt) where T : new()
    {
        if (!File.Exists(path)) return new T();
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), opt) ?? new T();
    }
}
