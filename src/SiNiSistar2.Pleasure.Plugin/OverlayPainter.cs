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

    /// <summary>
    /// Fills a rectangle with a flat colour.
    ///
    /// Not through <see cref="Draw"/>, which is where this used to go and where it was wrong.
    /// <c>GUI.Label(Rect, Texture)</c> draws the texture at its own size inside the rectangle; it
    /// does not stretch it. A gauge drawn that way looks right because its texture is built at the
    /// size it is drawn at — but the flat fills all come from a single white pixel, so every one of
    /// them was drawing one pixel. That is why the climax haze was reported as not happening: it
    /// was happening, one pixel at a time.
    ///
    /// A style's background image is stretched to the control, which is the behaviour this needs,
    /// so the fill goes through a box with the solid pixel as its background.
    /// </summary>
    internal static void Fill(Rect area, Color tint)
    {
        Color previous = GUI.color;
        GUI.color = tint;

        try
        {
            GUI.Box(area, GUIContent.none, FillStyle);
        }
        catch (Exception)
        {
            GUI.Box(area, GUIContent.none);
        }

        GUI.color = previous;
    }

    private static GUIStyle? _fillStyle;

    private static GUIStyle FillStyle
    {
        get
        {
            if (_fillStyle is not null)
            {
                return _fillStyle;
            }

            var style = new GUIStyle();
            style.normal.background = Solid;
            style.border = new RectOffset(0, 0, 0, 0);
            style.margin = new RectOffset(0, 0, 0, 0);
            style.padding = new RectOffset(0, 0, 0, 0);
            style.overflow = new RectOffset(0, 0, 0, 0);
            _fillStyle = style;
            return style;
        }
    }

    internal static void Text(Rect area, string text, Color colour)
    {
        Color previous = GUI.contentColor;
        GUI.contentColor = colour;
        GUI.Label(area, text);
        GUI.contentColor = previous;
    }
}
