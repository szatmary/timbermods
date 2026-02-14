using HarmonyLib;
using Timberborn.ModManagerScene;

namespace AdvancedZipLineStation;

public class ModStarter : IModStarter
{
    public void StartMod(IModEnvironment modEnvironment)
    {
        Anatawa12.AppleSiliconHarmony.Patcher.Patch();
        new Harmony("AdvancedZipLineStation").PatchAll();
    }
}
