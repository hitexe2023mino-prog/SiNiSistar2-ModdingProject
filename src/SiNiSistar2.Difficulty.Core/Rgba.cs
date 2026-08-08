using System.Globalization;

namespace SiNiSistar2.Difficulty.Core;

/// <summary>
/// A colour in 0..1 components. Kept as plain floats so the layer that decides when the gauge is
/// tinted stays free of UnityEngine and testable without the game (SPEC002 FR-134).
/// </summary>
public readonly record struct Rgba(float R, float G, float B, float A);

/// <summary>Parses the <c>RRGGBB</c> / <c>RRGGBBAA</c> colour written in configuration.</summary>
public static class HexColor
{
    /// <summary>
    /// Accepts <c>RRGGBB</c> or <c>RRGGBBAA</c>, with or without a leading <c>#</c>. Returns false
    /// for anything else rather than falling back to a colour the user did not ask for: a silently
    /// substituted colour would look like the feature working while ignoring the setting.
    /// </summary>
    public static bool TryParse(string? text, out Rgba color)
    {
        color = default;
        string value = (text ?? string.Empty).Trim().TrimStart('#');
        if (value.Length != 6 && value.Length != 8)
        {
            return false;
        }

        if (!byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            || !byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            || !byte.TryParse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        byte a = 255;
        if (value.Length == 8
            && !byte.TryParse(value[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
        {
            return false;
        }

        color = new Rgba(r / 255f, g / 255f, b / 255f, a / 255f);
        return true;
    }

    /// <summary>The shipped tint for a nullification window.</summary>
    public const string DefaultNullificationHex = "FF3E9D";
}
