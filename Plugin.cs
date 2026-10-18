using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace GTFOQuicksave;

[BepInPlugin("levi.gtfo-quicksave", "GTFO Quicksave", "1.0.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSource = null!;

    public override void Load()
    {
        LogSource = Log;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<QuicksaveBehaviour>())
            ClassInjector.RegisterTypeInIl2Cpp<QuicksaveBehaviour>();

        AddComponent<QuicksaveBehaviour>();
        new Harmony("levi.gtfo-quicksave").PatchAll();
        Log.LogInfo("Host double-taps F5 to save a checkpoint and F9 to load it.");
    }
}
