using System;
using Player;
using SNetwork;
using UnityEngine;

namespace GTFOQuicksave;

public class QuicksaveBehaviour : MonoBehaviour
{
    public const float DoublePressWindow = 1f;
    private const float CaptureTimeout = 5f;
    private const float WipeDamage = 1000f;
    private const float WipeWindow = 1.5f;
    private const int LoadPressesRequired = 3;

    private float _saveArmedAt;
    private float _loadArmedAt;
    private int _loadPresses;
    private bool _capturePending;
    private float _captureStartedAt;
    private PlayerAgent[]? _wipeTargets;
    private float _wipeUntil;
    private bool _wipeErrorLogged;
    private Vector3 _pendingPosition;
    private bool _hadBuffer;
    private int _bufferIdBefore;
    private bool _sawCapturing;

    public QuicksaveBehaviour(IntPtr ptr) : base(ptr) { }

    public void Update()
    {
        if (SNet.Current == null || !SNet.IsMaster || GameStateManager.Current == null)
            return;

        if (_capturePending && GameStateManager.CurrentStateName == eGameStateName.InLevel)
            PumpCapture();

        DownedBotRestorer.Tick();

        if (GameStateManager.CurrentStateName != eGameStateName.InLevel || CheckpointManager.IsReloadingCheckpoint)
        {
            _saveArmedAt = 0f;
            _loadArmedAt = 0f;
            _loadPresses = 0;
            if (_capturePending)
                DownedBotRestorer.CancelPending();
            _capturePending = false;
            _wipeTargets = null;
            return;
        }

        if (_wipeTargets != null)
            ApplyWipeDamage();

        ClearedScanHider.Tick();

        if (!PlayerManager.HasLocalPlayerAgent())
            return;

        var now = Time.realtimeSinceStartup;
        if (Input.GetKeyDown(KeyCode.F5))
            OnDoublePress(ref _saveArmedAt, now, "Press F5 again to save", Save);
        else if (Input.GetKeyDown(KeyCode.F9))
            OnLoadPress(now);
    }

    private void OnDoublePress(ref float armedAt, float now, string armMessage, Action commit)
    {
        if (armedAt > 0f && now - armedAt <= DoublePressWindow)
        {
            armedAt = 0f;
            commit();
            return;
        }

        armedAt = now;
        Notify(armMessage);
    }

    private void OnLoadPress(float now)
    {
        if (_loadArmedAt <= 0f || now - _loadArmedAt > DoublePressWindow)
            _loadPresses = 0;

        _loadPresses++;
        _loadArmedAt = now;
        if (_loadPresses < LoadPressesRequired)
        {
            var remaining = LoadPressesRequired - _loadPresses;
            Notify(remaining == 1 ? "Press F9 once more to load" : "Press F9 twice more to load");
            return;
        }

        _loadPresses = 0;
        _loadArmedAt = 0f;
        Load();
    }

    private void Save()
    {
        if (_capturePending)
        {
            Notify("Checkpoint is busy");
            return;
        }

        var capture = SNet.Capture;
        var agent = PlayerManager.GetLocalPlayerAgent();
        if (capture == null || agent == null || capture.IsCapturing || capture.IsRecalling)
        {
            Notify("Checkpoint is busy");
            return;
        }

        _pendingPosition = agent.Position;
        try
        {
            DownedBotRestorer.CapturePending();
            MapDataManager.CheckpointStoreSyncedGUIObjects();
            RememberBuffer();
            capture.CaptureGameState(eBufferType.Checkpoint);
        }
        catch (Exception ex)
        {
            DownedBotRestorer.CancelPending();
            Plugin.LogSource.LogError($"Checkpoint capture failed: {ex}");
            Notify("Checkpoint capture failed");
            return;
        }

        _capturePending = true;
        _sawCapturing = capture.IsCapturing;
        _captureStartedAt = Time.realtimeSinceStartup;
        PumpCapture();
    }

    private void Load()
    {
        if (_capturePending || !QuicksaveState.HasCheckpoint || !HasValidCheckpointBuffer())
        {
            Notify(_capturePending ? "Checkpoint is busy" : "No checkpoint to load");
            return;
        }

        var players = PlayerManager.PlayerAgentsInLevel;
        if (players == null || players.Count == 0)
        {
            Notify("Checkpoint load failed");
            return;
        }

        // Copy first. Dying removes an agent from the live list and the next index would be skipped.
        var targets = new PlayerAgent[players.Count];
        var count = 0;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (!CanWipe(player))
                continue;

            targets[count++] = player;
        }

        if (count == 0)
        {
            Notify("Checkpoint load failed");
            return;
        }

        if (count != targets.Length)
            Array.Resize(ref targets, count);

        _wipeTargets = targets;
        _wipeUntil = Time.realtimeSinceStartup + WipeWindow;
        _wipeErrorLogged = false;
        ApplyWipeDamage();
        Notify("Loading checkpoint");
    }

    private void ApplyWipeDamage()
    {
        var targets = _wipeTargets;
        if (targets == null)
            return;

        var stillAlive = false;
        for (var i = 0; i < targets.Length; i++)
        {
            var player = targets[i];
            if (!CanWipe(player))
                continue;

            stillAlive = true;
            try
            {
                // The first hit downs a standing player. Another hit in the same damage segment is
                // folded into that down, so keep striking until the player is actually dead.
                player.Damage.FallDamage(WipeDamage);
                if (CanWipe(player))
                    player.Damage.FallDamage(WipeDamage);
            }
            catch (Exception ex)
            {
                if (_wipeErrorLogged)
                    continue;

                _wipeErrorLogged = true;
                Plugin.LogSource.LogError($"Checkpoint load failed: {ex}");
            }
        }

        if (!stillAlive || Time.realtimeSinceStartup >= _wipeUntil)
            _wipeTargets = null;
    }

    private static bool CanWipe(PlayerAgent? player)
    {
        try
        {
            return player != null && player.Alive && player.Damage != null;
        }
        catch
        {
            return false;
        }
    }

    private void PumpCapture()
    {
        var capture = SNet.Capture;
        if (capture == null)
        {
            FailCapture();
            return;
        }

        if (!IsCaptureReady(capture))
        {
            if (Time.realtimeSinceStartup - _captureStartedAt > CaptureTimeout)
                FailCapture();
            return;
        }

        _capturePending = false;
        try
        {
            CheckpointManager.StoreCheckpoint(_pendingPosition);
        }
        finally
        {
            DownedBotRestorer.FinishPending();
        }
        if (TryGetCheckpointBuffer(out var buffer))
            Plugin.LogSource.LogInfo($"Checkpoint buffer id={buffer.data.bufferID} valid={buffer.isValid} got={capture.GotBuffer(eBufferType.Checkpoint)}");
        Notify("Checkpoint saved");
    }

    private void FailCapture()
    {
        _capturePending = false;
        DownedBotRestorer.CancelPending();
        LogBuffers();
        Notify("Checkpoint capture failed");
    }

    private void RememberBuffer()
    {
        _sawCapturing = false;
        var capture = SNet.Capture;
        _hadBuffer = capture != null && capture.GotBuffer(eBufferType.Checkpoint);
        _bufferIdBefore = 0;
        if (TryGetCheckpointBuffer(out var buffer) && buffer.isValid)
            _bufferIdBefore = buffer.data.bufferID;
    }

    private bool IsCaptureReady(SNet_Capture capture)
    {
        if (capture.IsCapturing)
        {
            _sawCapturing = true;
            return false;
        }

        var got = capture.GotBuffer(eBufferType.Checkpoint);
        var id = 0;
        if (TryGetCheckpointBuffer(out var buffer) && buffer.isValid)
            id = buffer.data.bufferID;

        if (!got && id == 0)
            return false;

        if (!_hadBuffer)
            return true;

        if (id != 0 && id != _bufferIdBefore)
            return true;

        return _sawCapturing;
    }

    private static bool HasValidCheckpointBuffer()
    {
        var capture = SNet.Capture;
        if (capture == null)
            return false;

        if (TryGetCheckpointBuffer(out var buffer) && buffer.isValid && buffer.data.bufferID != 0)
            return true;

        return capture.GotBuffer(eBufferType.Checkpoint);
    }

    private static bool TryGetCheckpointBuffer(out SNet_CaptureBuffer buffer)
    {
        buffer = null!;
        var capture = SNet.Capture;
        if (capture == null)
            return false;

        var buffers = capture.m_buffers;
        if (buffers == null)
            return false;

        for (var i = 0; i < buffers.Length; i++)
        {
            var candidate = buffers[i];
            if (candidate == null || candidate.type != eBufferType.Checkpoint)
                continue;

            buffer = candidate;
            return true;
        }

        return false;
    }

    private static void LogBuffers()
    {
        var capture = SNet.Capture;
        if (capture == null)
        {
            Plugin.LogSource.LogError("Checkpoint capture failed: SNet.Capture is missing.");
            return;
        }

        Plugin.LogSource.LogError($"Checkpoint capture failed. GotBuffer={capture.GotBuffer(eBufferType.Checkpoint)} IsCapturing={capture.IsCapturing}");
        var buffers = capture.m_buffers;
        if (buffers == null)
            return;

        for (var i = 0; i < buffers.Length; i++)
        {
            var candidate = buffers[i];
            if (candidate == null)
            {
                Plugin.LogSource.LogError($"buffer[{i}] null");
                continue;
            }

            Plugin.LogSource.LogError($"buffer[{i}] type={candidate.type} valid={candidate.isValid} id={candidate.data.bufferID}");
        }
    }

    internal static void Notify(string message)
    {
        Plugin.LogSource.LogInfo(message);
        if (GuiManager.Current == null)
            return;

        var layer = GuiManager.PlayerLayer;
        if (layer == null || layer.m_gameEventLog == null)
            return;

        layer.m_gameEventLog.AddLogItem(message, eGameEventChatLogType.GameEvent);
    }
}
