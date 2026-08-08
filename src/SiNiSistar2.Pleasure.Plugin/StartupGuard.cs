using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SiNiSistar2.Pleasure.Plugin;

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


    /// <summary>
    /// Who this process is and who started it (SPEC003 付録A A-29).
    ///
    /// The duplicate warning said a second copy was running but not where it came from, so it could
    /// be read and still leave the question open. Three copies were once alive at the same time, two
    /// of them 463 milliseconds apart, and nothing in the log said whether a person or a program had
    /// asked for them. The parent process is the one fact that settles it: a shell, a launcher and a
    /// build script are three different answers and they are not guessable from timing.
    ///
    /// Every sibling is listed with its start time as well, because the interesting question is not
    /// "is there another one" but "how many, and how far apart were they started".
    /// </summary>
    internal static string DescribeLaunch()
    {
        var parts = new List<string>(3);
        try
        {
            using Process self = Process.GetCurrentProcess();
            parts.Add($"pid {self.Id} started {self.StartTime:HH:mm:ss.fff}");
            parts.Add($"parent {DescribeParent()}");
            parts.Add($"siblings [{DescribeSiblings(self.Id)}]");
        }
        catch (Exception exception)
        {
            parts.Add($"the launch could not be described: {exception.Message}");
        }

        return string.Join("; ", parts);
    }

    private static string DescribeParent()
    {
        try
        {
            var information = default(ProcessBasicInformation);
            using Process self = Process.GetCurrentProcess();
            int status = NtQueryInformationProcess(
                self.Handle, 0, ref information, Marshal.SizeOf<ProcessBasicInformation>(), out _);
            if (status != 0)
            {
                return $"(unavailable, status 0x{status:X8})";
            }

            var parentId = (int)information.InheritedFromUniqueProcessId;
            try
            {
                using Process parent = Process.GetProcessById(parentId);
                return $"{parent.ProcessName} (pid {parentId}), started {parent.StartTime:HH:mm:ss.fff}";
            }
            catch (Exception)
            {
                // A parent that has already exited is itself worth knowing: a launcher that starts
                // the game and quits leaves exactly this trace.
                return $"pid {parentId}, which has already exited";
            }
        }
        catch (Exception exception)
        {
            return $"(unreadable: {exception.Message})";
        }
    }

    private static string DescribeSiblings(int selfId)
    {
        try
        {
            Process[] all = Process.GetProcessesByName("SiNiSistar2");
            var rows = new List<string>(all.Length);
            foreach (Process process in all)
            {
                using (process)
                {
                    string mark = process.Id == selfId ? " (this one)" : string.Empty;
                    try
                    {
                        rows.Add($"pid {process.Id} at {process.StartTime:HH:mm:ss.fff}{mark}");
                    }
                    catch (Exception)
                    {
                        rows.Add($"pid {process.Id}{mark}");
                    }
                }
            }

            rows.Sort(StringComparer.Ordinal);
            return string.Join(", ", rows);
        }
        catch (Exception exception)
        {
            return $"unreadable: {exception.Message}";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int informationClass,
        ref ProcessBasicInformation information,
        int informationLength,
        out int returnLength);
    /// <summary>The message shown when a duplicate is detected. Shared so both MODs say the same thing.</summary>
    internal const string DuplicateInstanceMessage =
        "Another copy of SiNiSistar2 is already running. Two instances fight over the save files, "
        + "the BepInEx log and the loopback ports, which makes launches appear to fail at random. "
        + "Close every running copy before starting a new one.";
}
