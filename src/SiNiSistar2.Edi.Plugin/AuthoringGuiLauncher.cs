using System.Diagnostics;
using BepInEx.Logging;

namespace SiNiSistar2.Edi.Plugin;

/// <summary>
/// Opens the funscript authoring GUI in the machine's default browser (SPEC001 FR-061).
///
/// The GUI is a loopback web page, and until now the only way in was to read its URL out of the
/// startup log and type it into a browser by hand. That is a poor gate for the one screen the whole
/// authoring workflow runs through: the moment a user wants it is the moment they have just met a
/// new trigger, which is while the game is in the foreground.
///
/// Launching is best effort. A browser that will not start is worth one line in the log and nothing
/// more — the URL still works, and the game must not be disturbed by a failure to open a web page.
/// </summary>
internal static class AuthoringGuiLauncher
{
    internal static void Open(string? url, ManualLogSource? log, string reason)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            log?.LogWarning(
                $"The authoring GUI was asked for ({reason}) but it is not running, so there is "
                + "nothing to open.");
            return;
        }

        try
        {
            // UseShellExecute is what hands the URL to whatever the user has set as their browser.
            // Without it, .NET tries to execute the string as a program and throws.
            using var process = Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
            log?.LogInfo($"Opened the authoring GUI at {url} ({reason}).");
        }
        catch (Exception exception)
        {
            log?.LogWarning(
                $"Could not open a browser for the authoring GUI ({exception.Message}). "
                + $"It is still served at {url}.");
        }
    }
}
