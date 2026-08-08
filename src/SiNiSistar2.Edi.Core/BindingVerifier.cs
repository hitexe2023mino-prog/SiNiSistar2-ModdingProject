using System.Text.RegularExpressions;

namespace SiNiSistar2.Edi.Core;

/// <summary>Why one output is not usable, in a form the log and the GUI can both show.</summary>
public sealed record OutputBindingResult(string Output, bool IsBound, IReadOnlyList<string> Failures)
{
    public static OutputBindingResult Bound(string output) =>
        new(output, true, Array.Empty<string>());
}

/// <summary>
/// Checks that each output in the roster is actually backed by the device the roster names, on
/// the channel named after the output, with the variant the roster names, and that no other
/// device shares that channel (SPEC001 7.1).
///
/// The last condition is the one that catches a mis-wired setup before it can move a device: EDI
/// puts a device with no stored channel on the first configured channel, so an unrelated toy can
/// land on an output the MOD drives (付録E).
/// </summary>
public static class BindingVerifier
{
    /// <summary>EDI appends " (1)", " (2)" … when a device is re-added before the old one is gone.</summary>
    private static readonly Regex UniquifiedSuffix = new(
        @"^(?<name>.*) \(\d+\)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<OutputBindingResult> Verify(
        IReadOnlyList<OutputBinding> roster,
        IReadOnlyList<EdiDevice> devices)
    {
        var rosterDeviceNames = roster
            .Select(x => x.EdiDeviceName)
            .ToHashSet(StringComparer.Ordinal);

        var results = new List<OutputBindingResult>(roster.Count);
        foreach (OutputBinding output in roster)
        {
            var failures = new List<string>();
            EdiDevice? device = devices.FirstOrDefault(
                x => string.Equals(x.Name, output.EdiDeviceName, StringComparison.Ordinal));

            if (device is null)
            {
                failures.Add(DescribeMissingDevice(output, devices));
            }
            else
            {
                if (!string.Equals(device.Channel, output.Id, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"device '{device.Name}' is on channel '{device.Channel ?? "(none)"}' but output "
                        + $"'{output.Id}' requires channel '{output.Id}'");
                }

                if (!string.Equals(device.SelectedVariant, output.EdiVariant, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"device '{device.Name}' has variant '{device.SelectedVariant ?? "(none)"}' but "
                        + $"output '{output.Id}' requires '{output.EdiVariant}'");
                }

                if (!device.IsReady)
                {
                    failures.Add($"device '{device.Name}' reports IsReady=false");
                }
            }

            // Anything else sitting on this output's channel would receive this output's waveform.
            string[] intruders = devices
                .Where(x => string.Equals(x.Channel, output.Id, StringComparison.Ordinal)
                            && !rosterDeviceNames.Contains(x.Name))
                .Select(x => x.Name)
                .ToArray();
            if (intruders.Length > 0)
            {
                failures.Add(
                    $"channel '{output.Id}' also holds device(s) {string.Join(", ", intruders.Select(x => $"'{x}'"))} "
                    + "that the roster does not name; they would receive this output's waveform. Assign "
                    + "them to another channel in EDI, or configure UnassignedDeviceChannel (SPEC001 7.4 E4)");
            }

            results.Add(failures.Count == 0
                ? OutputBindingResult.Bound(output.Id)
                : new OutputBindingResult(output.Id, false, failures));
        }

        return results;
    }

    /// <summary>
    /// Names the likely cause when the expected device is absent. A device whose name gained a
    /// uniquifying suffix is the common one, and it reads as "device missing" unless said out loud
    /// (SPEC001 FR-054).
    /// </summary>
    private static string DescribeMissingDevice(OutputBinding output, IReadOnlyList<EdiDevice> devices)
    {
        string[] uniquified = devices
            .Where(device =>
            {
                Match match = UniquifiedSuffix.Match(device.Name);
                return match.Success
                    && string.Equals(match.Groups["name"].Value, output.EdiDeviceName, StringComparison.Ordinal);
            })
            .Select(device => device.Name)
            .ToArray();

        if (uniquified.Length > 0)
        {
            return
                $"EDI reports no device named '{output.EdiDeviceName}', but it does report "
                + $"{string.Join(", ", uniquified.Select(x => $"'{x}'"))}. EDI renames a device when it is "
                + "re-added before the previous one was released. Reconnect the device or restart EDI";
        }

        string found = devices.Count == 0
            ? "no devices at all"
            : string.Join(", ", devices.Select(x => $"'{x.Name}'"));
        return $"EDI reports no device named '{output.EdiDeviceName}'; it reports {found}";
    }
}
