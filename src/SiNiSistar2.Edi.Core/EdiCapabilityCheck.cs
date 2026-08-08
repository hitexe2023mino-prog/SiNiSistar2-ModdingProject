namespace SiNiSistar2.Edi.Core;

/// <summary>Outcome of the startup capability negotiation (SPEC001 7.4.3, FR-052).</summary>
public sealed record CapabilityCheck(
    bool AllowsPlayback,
    bool AllowsReload,
    IReadOnlyList<string> Blocking,
    IReadOnlyList<string> Warnings)
{
    public bool IsFullySupported => Blocking.Count == 0 && Warnings.Count == 0;
}

/// <summary>
/// Decides whether the EDI that answered is the one this MOD depends on.
///
/// The behaviour the MOD relies on is switchable in EDI so other games keep their old behaviour
/// (FR-051). That makes "the flags were never turned on" a reachable state whose only symptoms
/// are silent misrouting and a stop that does not stop, so it is checked at startup instead
/// (DEC-028).
/// </summary>
public static class EdiCapabilityCheck
{
    public static CapabilityCheck Evaluate(EdiCapabilities? capabilities)
    {
        if (capabilities is null)
        {
            return new CapabilityCheck(
                AllowsPlayback: false,
                AllowsReload: false,
                Blocking: new[]
                {
                    "EDI has no GET /Edi/Info, so it does not carry the changes SPEC001 7.4 requires. "
                    + "Playback stays disabled: variant fallback would misroute waveforms silently "
                    + "(FR-050) and Stop would not stop the device (FR-019, FR-045). Build EDI from "
                    + "the source of truth named in SPEC001 and deploy it to Edi/Edi.exe.",
                },
                Warnings: Array.Empty<string>());
        }

        var blocking = new List<string>();
        var warnings = new List<string>();

        if (!capabilities.StrictVariantResolution)
        {
            blocking.Add(
                "EDI has StrictVariantResolution disabled, so a device asked for a gallery without "
                + "its variant plays another device's waveform without warning. FR-050 cannot hold; "
                + "enable it in Edi/EdiConfig.json.");
        }

        if (!capabilities.StopClearsFiller)
        {
            blocking.Add(
                "EDI has StopClearsFiller disabled, so Stop replays the retained filler instead of "
                + "stopping the device. FR-019 and FR-045 cannot hold; enable it in Edi/EdiConfig.json.");
        }

        if (string.IsNullOrWhiteSpace(capabilities.UnassignedDeviceChannel))
        {
            // Binding verification still catches the damage, so this is not fail-closed: it only
            // means an unrelated device can suppress an output that is otherwise fine (SPEC001 7.4.3).
            warnings.Add(
                "EDI has no UnassignedDeviceChannel, so a device that is not in the roster is placed "
                + "on the first configured channel. Connecting an unrelated toy can therefore suppress "
                + "an output. Set it in Edi/EdiConfig.json and add the value to Channels.");
        }

        return new CapabilityCheck(
            AllowsPlayback: blocking.Count == 0,
            AllowsReload: blocking.Count == 0,
            Blocking: blocking,
            Warnings: warnings);
    }
}
