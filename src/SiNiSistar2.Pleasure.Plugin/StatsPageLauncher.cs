using System.Diagnostics;
using BepInEx.Logging;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Opens the play-statistics page in the machine's default browser (SPEC006 FR-614).
///
/// The page is a loopback web page, and without this the only way in is to read its URL out of the
/// startup log and type it into a browser by hand. The moment someone wants the diary is the moment
/// they have just been made to climax by something new — which is while the game is in the
/// foreground and typing a URL is the last thing they are going to do.
///
/// Nothing is drawn in game. What the key opens is the browser; the page stays exactly where
/// SPEC006 2.2 puts it, outside the game (DEC-608).
///
/// Written out again rather than shared with SPEC001's identical launcher: the two MODs must not
/// reference one another, and either has to keep working when the other is not installed
/// (SPEC006 2.3).
/// </summary>
internal static class StatsPageLauncher
{
    internal static void Open(string? url, ManualLogSource? log, string reason)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            log?.LogWarning(
                $"The statistics page was asked for ({reason}) but it is not running, so there is "
                + "nothing to open.");
            return;
        }

        try
        {
            // UseShellExecute is what hands the URL to whatever the user has set as their browser.
            // Without it, .NET tries to execute the string as a program and throws.
            using var process = Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
            log?.LogInfo($"Opened the statistics page at {url} ({reason}).");
        }
        catch (Exception exception)
        {
            // Best effort, always. A browser that will not start is worth one line in the log and
            // nothing more: the URL still works, and failing to open a web page must never disturb
            // the run in progress (FR-611).
            log?.LogWarning(
                $"Could not open a browser for the statistics page ({exception.Message}). "
                + $"It is still served at {url}.");
        }
    }
}
