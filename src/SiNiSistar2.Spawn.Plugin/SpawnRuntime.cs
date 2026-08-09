using BepInEx.Logging;
using SiNiSistar2.Obj;
using SiNiSistar2.Spawn.Core;
using UnityEngine;

namespace SiNiSistar2.Spawn.Plugin;

/// <summary>Lifecycle of one pseudo box (SPEC004 5.5): unresolved until touched, then hit or miss.</summary>
internal enum MimicBoxState
{
    /// <summary>Placed; the lottery has not run because nothing touched it yet (DEC-313).</summary>
    Unresolved,

    /// <summary>
    /// Resolved as "just a box". Every later hold attempt stays suppressed even when the body
    /// could not be removed (FR-322 縮退経路), so a miss can never turn back into a mimic.
    /// </summary>
    ResolvedMiss,
}

/// <summary>One registered pseudo box: the enemy instance and where its lottery stands.</summary>
internal sealed class MimicBoxEntry
{
    internal MimicBoxEntry(EnemyObject enemy) => Enemy = enemy;

    internal EnemyObject Enemy { get; }

    internal MimicBoxState State { get; set; } = MimicBoxState.Unresolved;
}

/// <summary>
/// Shared state between the plugin, the observer and the Harmony patches. Everything runs on the
/// main thread (SPEC004 10.2), so plain fields are enough.
/// </summary>
internal static class SpawnRuntime
{
    internal static ManualLogSource? Log;

    internal static SpawnProfile Profile = new() { Enabled = false };

    /// <summary>Runtime gate: flipping Enabled off mid-session suspends and rolls back (FR-316).</summary>
    internal static bool Enabled;

    /// <summary>The current visit's random stream (SPEC004 5.6); reseeded on every area entry.</summary>
    internal static IRandomSource Random = new SystemRandomSource();

    /// <summary>
    /// Re-reads the config file. BepInEx does not watch the file, so without this an edit made
    /// while the game runs would have no effect and the panel's own instructions would be a lie.
    /// Invoked when the debug panel opens; applies the debug switch and the master Enabled switch.
    /// </summary>
    internal static Action? ReloadConfig;

    /// <summary>
    /// Turns the debug commands on or off and persists the choice to the config file, so the
    /// switch can be thrown from the panel that reports it (SPEC004 5.9, FR-337).
    /// </summary>
    internal static Action<bool>? SetDebugCommands;

    /// <summary>HUD and debug panel hotkeys (SPEC004 6章 [Debug]).</summary>
    internal static UnityEngine.KeyCode HudHotkey = UnityEngine.KeyCode.F5;

    internal static UnityEngine.KeyCode DebugPanelHotkey = UnityEngine.KeyCode.F3;

    /// <summary>
    /// Pseudo-treasure-box registry keyed by EnemyObject instance id. Only instances registered
    /// here are ever intercepted, so vanilla mimics stay untouched (SPEC004 FR-324).
    /// </summary>
    internal static readonly Dictionary<int, MimicBoxEntry> MimicBoxes = new();

    /// <summary>Miss outcomes waiting for the observer to remove the body and grant the reward.</summary>
    internal static readonly Queue<int> PendingMimicMisses = new();

    /// <summary>
    /// A debug-pinned outcome for the next lottery, or null for the normal roll. Consumed by the
    /// first resolution and reported, so a forced result is never mistaken for a natural one
    /// (SPEC004 5.9 抽選固定, FR-333).
    /// </summary>
    internal static bool? PinnedMimicOutcome;

    /// <summary>
    /// A copy to re-read shortly after it was made. Grounding is settled by the physics step, so
    /// reading it on the spawn frame cannot tell "not evaluated yet" from "never grounded".
    /// </summary>
    internal static (EnemyObject Enemy, double Due, Vector3 SpawnedAt)? PendingCopyCheck;

    internal static int MimicHits;

    internal static int MimicMisses;

    internal static void LogIntervention(string message)
    {
        if (Profile.LogInterventions)
        {
            Log?.LogInfo($"[intervention] {message}");
        }
    }

    internal static void ResetVisitState()
    {
        MimicBoxes.Clear();
        PendingMimicMisses.Clear();
        PinnedMimicOutcome = null;
        MimicHits = 0;
        MimicMisses = 0;
    }
}
