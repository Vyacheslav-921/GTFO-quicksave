using HarmonyLib;

namespace GTFOQuicksave;

internal static class QuicksaveState
{
    public static bool HasCheckpoint;
}

[HarmonyPatch(typeof(CheckpointManager), nameof(CheckpointManager.StoreCheckpoint))]
internal static class StoreCheckpointPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        QuicksaveState.HasCheckpoint = true;
    }
}

[HarmonyPatch(typeof(CheckpointManager), nameof(CheckpointManager.OnLevelCleanup))]
internal static class LevelCleanupPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        QuicksaveState.HasCheckpoint = false;
    }
}
