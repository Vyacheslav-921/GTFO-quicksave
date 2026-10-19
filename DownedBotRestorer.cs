using Player;
using SNetwork;
using UnityEngine;

namespace GTFOQuicksave;

// Checkpoint capture stores a downed bot's locomotion, then bot spawn starts the AI,
// which stands them back up. Keep the bots that were downed at save time downed.
internal static class DownedBotRestorer
{
    private const float RestoreWindow = 4f;
    private const float DownDamage = 1000f;

    private static int _savedMask;
    private static int _pendingMask;
    private static bool _hasPending;
    private static int _damagedMask;
    private static int _aiNotifiedMask;
    private static bool _forceSpawn;
    private static float _remaining;
    private static bool _loggedError;

    public static void CapturePending()
    {
        _pendingMask = Collect();
        _hasPending = true;
    }

    public static void CancelPending()
    {
        _hasPending = false;
        _pendingMask = 0;
    }

    public static void Commit()
    {
        // Leave a pending snapshot in place so a second store during the same capture
        // does not replace it with whatever the bots are doing after the snapshot.
        _savedMask = _hasPending ? _pendingMask : Collect();
    }

    public static void FinishPending()
    {
        _hasPending = false;
        _pendingMask = 0;
    }

    public static void Clear()
    {
        _savedMask = 0;
        _damagedMask = 0;
        _aiNotifiedMask = 0;
        _remaining = 0f;
        _forceSpawn = false;
        CancelPending();
    }

    public static void ArmRecall()
    {
        if (_savedMask == 0)
            return;

        _forceSpawn = true;
        _damagedMask = 0;
        _aiNotifiedMask = 0;
        _loggedError = false;
    }

    public static void Schedule()
    {
        _loggedError = false;
        if (_savedMask == 0)
        {
            _remaining = 0f;
            _forceSpawn = false;
            return;
        }

        _forceSpawn = true;
        _remaining = RestoreWindow;
        var count = CountBits(_savedMask);
        if (count > 0)
            Plugin.LogSource.LogInfo($"Restoring {count} downed {(count == 1 ? "bot" : "bots")}");
        Restore();
    }

    public static void Tick()
    {
        if (_remaining <= 0f)
            return;

        _remaining -= Time.unscaledDeltaTime;
        Restore();
        if (_remaining <= 0f)
            _forceSpawn = false;
    }

    public static void ForceSpawnState(SNet_Player? player, ref PlayerLocomotion.PLOC_State state)
    {
        if (!_forceSpawn || player == null || !player.IsBot)
            return;

        try
        {
            if (HasSlot(_savedMask, player.PlayerSlotIndex()))
                state = PlayerLocomotion.PLOC_State.Downed;
        }
        catch (Exception ex)
        {
            LogOnce(ex);
        }
    }

    public static void OnBotSpawned(PlayerAIBot ai)
    {
        if (!_forceSpawn || ai == null)
            return;

        try
        {
            var agent = ai.Agent;
            if (!ShouldKeep(agent))
                return;

            KeepDown(agent!, ai, SlotBit(agent!.PlayerSlotIndex));
        }
        catch (Exception ex)
        {
            LogOnce(ex);
        }
    }

    private static void Restore()
    {
        if (_savedMask == 0)
            return;

        var players = PlayerManager.PlayerAgentsInLevel;
        if (players == null)
            return;

        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (!ShouldKeep(player))
                continue;

            try
            {
                KeepDown(player!, FindAi(player!), SlotBit(player!.PlayerSlotIndex));
            }
            catch (Exception ex)
            {
                LogOnce(ex);
            }
        }
    }

    private static bool ShouldKeep(PlayerAgent? player)
    {
        if (player == null || !player.Alive)
            return false;

        var owner = player.Owner;
        if (owner == null || !owner.IsBot)
            return false;

        return HasSlot(_savedMask, player.PlayerSlotIndex);
    }

    private static void KeepDown(PlayerAgent player, PlayerAIBot? ai, int bit)
    {
        // A teammate started reviving this bot. Leave that revive alone.
        if (ai != null && ai.IsInteractedWith())
        {
            ai.SetEnabled(true);
            _savedMask &= ~bit;
            return;
        }

        var locomotion = player.Locomotion;
        if (!IsDowned(locomotion))
            PutDown(player, locomotion, bit);

        if (ai == null)
            return;

        ai.SetEnabled(false);
        if (!IsDowned(player.Locomotion) || (_aiNotifiedMask & bit) != 0)
            return;

        ai.OnDowned();
        _aiNotifiedMask |= bit;
    }

    // One hit downs a standing bot. A second hit in a later damage segment kills them.
    private static void PutDown(PlayerAgent player, PlayerLocomotion? locomotion, int bit)
    {
        if ((_damagedMask & bit) == 0 && player.Damage != null && player.Damage.Health > 0f)
        {
            player.Damage.FallDamage(DownDamage);
            _damagedMask |= bit;
            return;
        }

        locomotion?.ChangeState(PlayerLocomotion.PLOC_State.Downed, false);
    }

    private static PlayerAIBot? FindAi(PlayerAgent player)
    {
        var ai = player.GetComponent<PlayerAIBot>();
        if (ai != null)
            return ai;

        var found = UnityEngine.Object.FindObjectsOfType<PlayerAIBot>();
        if (found == null)
            return null;

        for (var i = 0; i < found.Length; i++)
        {
            var candidate = found[i];
            if (candidate != null && candidate.Agent == player)
                return candidate;
        }

        return null;
    }

    private static bool IsDowned(PlayerLocomotion? locomotion)
    {
        return locomotion != null && locomotion.m_currentStateEnum == PlayerLocomotion.PLOC_State.Downed;
    }

    private static int Collect()
    {
        var players = PlayerManager.PlayerAgentsInLevel;
        if (players == null)
            return 0;

        var mask = 0;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            try
            {
                if (player == null || !player.Alive || !IsDowned(player.Locomotion))
                    continue;

                var owner = player.Owner;
                if (owner == null || !owner.IsBot)
                    continue;

                var bit = SlotBit(player.PlayerSlotIndex);
                if (bit != 0)
                    mask |= bit;
            }
            catch (Exception ex)
            {
                LogOnce(ex);
            }
        }

        return mask;
    }

    private static int SlotBit(int slot)
    {
        if ((uint)slot >= 31)
            return 0;

        return 1 << slot;
    }

    private static bool HasSlot(int mask, int slot)
    {
        var bit = SlotBit(slot);
        return bit != 0 && (mask & bit) != 0;
    }

    private static int CountBits(int mask)
    {
        var count = 0;
        while (mask != 0)
        {
            count += mask & 1;
            mask >>= 1;
        }

        return count;
    }

    private static void LogOnce(Exception ex)
    {
        if (_loggedError)
            return;

        _loggedError = true;
        Plugin.LogSource.LogError($"Downed bot restore failed: {ex}");
    }
}
