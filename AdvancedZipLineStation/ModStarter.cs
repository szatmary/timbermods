using HarmonyLib;
using Timberborn.ModManagerScene;

namespace AdvancedZipLineStation;

public class ModStarter : IModStarter
{
    public void StartMod(IModEnvironment modEnvironment)
    {
        new Harmony("AdvancedZipLineStation").PatchAll();
    }
}
