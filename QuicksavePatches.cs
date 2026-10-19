using HarmonyLib;
using SNetwork;

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

[HarmonyPatch(typeof(CheckpointManager), nameof(CheckpointManager.OnRecallComplete))]
internal static class RecallCompletePatch
{
    [HarmonyPostfix]
    public static void Postfix(eBufferType bufferType)
    {
        if (bufferType != eBufferType.Checkpoint)
            return;

        ClearedScanHider.Schedule();
    }
}
