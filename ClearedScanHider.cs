using ChainedPuzzles;
using UnityEngine;

namespace GTFOQuicksave;

internal static class ClearedScanHider
{
    private const float HideWindow = 4f;

    private static float _remaining;
    private static bool _loggedError;

    public static void Schedule()
    {
        _remaining = HideWindow;
        _loggedError = false;
        var hidden = Hide();
        if (hidden > 0)
            Plugin.LogSource.LogInfo($"Hid {hidden} cleared security scans");
    }

    public static void Tick()
    {
        if (_remaining <= 0f)
            return;

        _remaining -= Time.unscaledDeltaTime;
        Hide();
    }

    private static int Hide()
    {
        var manager = ChainedPuzzleManager.Current;
        var instances = manager?.m_instances;
        if (instances == null)
            return 0;

        var hidden = 0;
        for (var i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];
            if (instance == null)
                continue;

            try
            {
                var count = instance.NRofPuzzles();
                for (var puzzle = 0; puzzle < count; puzzle++)
                    hidden += HideCore(instance.GetPuzzle(puzzle), instance.IsSolved);
            }
            catch (Exception ex)
            {
                if (_loggedError)
                    continue;

                _loggedError = true;
                Plugin.LogSource.LogError($"Cleared scan hide failed: {ex}");
            }
        }

        return hidden;
    }

    private static int HideCore(iChainedPuzzleCore? core, bool solved)
    {
        if (core == null)
            return 0;

        var cluster = core.TryCast<CP_Cluster_Core>();
        if (cluster != null)
        {
            var done = solved || (cluster.m_sync != null && cluster.IsFinished());
            var hidden = 0;
            if (done)
            {
                cluster.m_spline?.SetVisible(false);
                cluster.m_hud?.SetVisible(cluster.m_puzzleIndex, false);
                hidden++;
            }

            var children = cluster.m_childCores;
            if (children == null)
                return hidden;

            for (var i = 0; i < children.Length; i++)
                hidden += HideCore(children[i], done);
            return hidden;
        }

        var bioscan = core.TryCast<CP_Bioscan_Core>();
        if (bioscan == null || (!solved && (bioscan.m_sync == null || !bioscan.IsFinished())))
            return 0;

        bioscan.m_graphics?.SetVisible(false, false);
        bioscan.m_spline?.SetVisible(false);
        bioscan.m_playerScanner?.StopScan();
        bioscan.m_hud?.SetVisible(bioscan.m_puzzleIndex, false);
        return 1;
    }
}
