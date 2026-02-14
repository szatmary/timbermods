using System.Runtime.InteropServices;
using HarmonyLib;
using Timberborn.ModManagerScene;

namespace PowerZipline;

public class ModStarter : IModStarter
{
    public void StartMod(IModEnvironment modEnvironment)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            Anatawa12.AppleSiliconHarmony.Patcher.Patch();

        new Harmony("PowerZipline").PatchAll();
    }
}
