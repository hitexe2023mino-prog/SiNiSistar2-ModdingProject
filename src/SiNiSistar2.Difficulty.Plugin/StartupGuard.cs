using System.Security.Cryptography;

namespace SiNiSistar2.Difficulty.Plugin;

/// <summary>
/// Startup work that every MOD in this repository needs but that must not be done more than once
/// per game process.
///
/// The MODs are deliberately independent: none references another and none declares a BepInEx
/// dependency, so any one of them can be installed alone. That independence used to mean each one
/// separately SHA-256'd the 51 MB <c>GameAssembly.dll</c>, adding most of a second to startup for
/// no new information. The results are shared through a process-wide slot instead, which keeps the
/// assemblies uncoupled while doing the work once.
/// </summary>
internal static class StartupGuard
{
    private const string FingerprintSlot = "community.sinisistar2.buildFingerprint";
    private const string InstanceSlot = "community.sinisistar2.instanceCheck";
    private const string InstanceMutexName = @"Global\community.sinisistar2.gameInstance";

    /// <summary>
    /// SHA-256 of a file, computed once per process however many MODs ask for it.
    /// </summary>
    internal static string Sha256(string path)
    {
        string key = $"{FingerprintSlot}:{path}";
        if (AppDomain.CurrentDomain.GetData(key) is string cached)
        {
            return cached;
        }

        using FileStream stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        string hash = Convert.ToHexString(algorithm.ComputeHash(stream));
        AppDomain.CurrentDomain.SetData(key, hash);
        return hash;
    }

    /// <summary>
    /// Whether another copy of the game is already running.
    ///
    /// Two instances contend for the save files, the BepInEx log, and the loopback port the EDI
    /// MOD's authoring GUI listens on. The symptoms are confusing and look random — one launch
    /// works, the next appears to fail — so the condition is worth naming out loud. Evaluated once
    /// per process: a second MOD asking must not see the mutex this process already holds and
    /// conclude it is a duplicate.
    /// </summary>
    internal static bool IsAnotherInstanceRunning()
    {
        if (AppDomain.CurrentDomain.GetData(InstanceSlot) is bool cached)
        {
            return cached;
        }

        var duplicate = false;
        try
        {
            // Held for the life of the process on purpose; it is never disposed.
            var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool createdNew);
            duplicate = !createdNew;
            AppDomain.CurrentDomain.SetData($"{InstanceSlot}.handle", mutex);
        }
        catch (Exception)
        {
            // An unavailable mutex is not evidence of a duplicate; say nothing rather than warn
            // about a condition that may not exist.
            duplicate = false;
        }

        AppDomain.CurrentDomain.SetData(InstanceSlot, duplicate);
        return duplicate;
    }

    /// <summary>The message shown when a duplicate is detected. Shared so both MODs say the same thing.</summary>
    internal const string DuplicateInstanceMessage =
        "Another copy of SiNiSistar2 is already running. Two instances fight over the save files, "
        + "the BepInEx log and the loopback ports, which makes launches appear to fail at random. "
        + "Close every running copy before starting a new one.";
}
