using SiNiSistar2.Pleasure.Core;
using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// The two drawing calls this build actually supports, in one place.
///
/// <c>GUI.DrawTexture</c> is stripped from this game's IL2CPP build even though it appears in the
/// interop metadata, so everything goes through <c>GUI.Label(Rect, Texture)</c> with a tint. The
/// fallback to <c>GUI.Box</c> exists because the same could turn out to be true of the label
/// overload on another build, and a gauge that degrades to plain blocks is better than one that
/// vanishes.
/// </summary>
internal static class OverlayPainter
{
    private static bool _labelUnavailable;
    private static Texture2D? _solid;

    /// <summary>A single white pixel, stretched. The basis of every flat fill.</summary>
    internal static Texture2D Solid => _solid ??= PleasureArt.Solid(Color.white);

    internal static void Draw(Rect area, Texture2D texture, Color tint)
    {
        Color previous = GUI.color;
        GUI.color = tint;

        if (!_labelUnavailable)
        {
            try
            {
                GUI.Label(area, texture);
                GUI.color = previous;
                return;
            }
            catch (Exception exception)
            {
                _labelUnavailable = true;
                PleasureRuntime.Log?.LogWarning(
                    "Textures cannot be drawn on this build; the overlay falls back to plain blocks "
                    + $"({exception.Message}).");
            }
        }

        GUI.Box(area, GUIContent.none);
        GUI.color = previous;
    }

    internal static void Fill(Rect area, Color tint) => Draw(area, Solid, tint);

    internal static void Text(Rect area, string text, Color colour)
    {
        Color previous = GUI.contentColor;
        GUI.contentColor = colour;
        GUI.Label(area, text);
        GUI.contentColor = previous;
    }
}
