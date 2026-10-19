using HarmonyLib;
using Player;
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
        DownedBotRestorer.Commit();
    }
}

[HarmonyPatch(typeof(CheckpointManager), nameof(CheckpointManager.OnLevelCleanup))]
internal static class LevelCleanupPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        QuicksaveState.HasCheckpoint = false;
        DownedBotRestorer.Clear();
    }
}

[HarmonyPatch(typeof(CheckpointManager), nameof(CheckpointManager.ReloadCheckpoint))]
internal static class ReloadCheckpointPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        DownedBotRestorer.ArmRecall();
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
        DownedBotRestorer.Schedule();
    }
}

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", typeof(SNet_Player), typeof(PlayerLocomotion.PLOC_State), typeof(int), typeof(bool))]
internal static class SpawnPlayerPatch
{
    [HarmonyPrefix]
    public static void Prefix(SNet_Player snetPlayer, ref PlayerLocomotion.PLOC_State plocState)
    {
        DownedBotRestorer.ForceSpawnState(snetPlayer, ref plocState);
    }
}

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", typeof(SNet_Player), typeof(int), typeof(PlayerLocomotion.PLOC_State), typeof(int), typeof(bool))]
internal static class SpawnPlayerInternalPatch
{
    [HarmonyPrefix]
    public static void Prefix(SNet_Player player, ref PlayerLocomotion.PLOC_State plocState)
    {
        DownedBotRestorer.ForceSpawnState(player, ref plocState);
    }
}

[HarmonyPatch(typeof(PlayerAIBot), nameof(PlayerAIBot.OnSpawned))]
internal static class BotSpawnedPatch
{
    [HarmonyPostfix]
    public static void Postfix(PlayerAIBot __instance)
    {
        DownedBotRestorer.OnBotSpawned(__instance);
    }
}
