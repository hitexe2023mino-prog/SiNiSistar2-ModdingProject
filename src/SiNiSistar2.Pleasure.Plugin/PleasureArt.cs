using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Builds the overlay's images pixel by pixel.
///
/// Everything is generated rather than shipped as files: the MOD stays a single DLL pair, and the
/// art can respond to state — the cross is redrawn with the damage carved into it rather than
/// being a fixed sprite with decorations laid on top.
///
/// <c>GUI.DrawTexture</c> is stripped from this game build, but <c>GUI.Label(Rect, Texture)</c>
/// survives, so a generated texture can still reach the screen.
/// </summary>
internal static class PleasureArt
{
    internal static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, color);
        Finish(texture);
        return texture;
    }

    /// <summary>
    /// The pleasure gauge: a disc that fills with liquid from the bottom.
    ///
    /// A rising level reads as something accumulating in a vessel, which an arc never did, and it
    /// sits inside the dial the game already draws instead of ringing it.
    /// </summary>
    internal static Texture2D LiquidDisc(int size, float fill, float phase)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        float radius = size / 2f;
        float clamped = Math.Clamp(fill, 0f, 1f);

        // The surface sits where the liquid has reached, with a shallow wave so it reads as fluid
        // rather than as a bar that happens to be round.
        float surface = clamped * size;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                float dx = x - radius + 0.5f;
                float dy = y - radius + 0.5f;
                float distance = (float)Math.Sqrt((dx * dx) + (dy * dy));
                int index = (y * size) + x;

                if (distance > radius - 0.5f)
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float wave = (float)Math.Sin((x / (double)size * 6.28318) + phase) * size * 0.012f;
                float localSurface = surface + wave;

                if (y > localSurface)
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                // Deeper liquid is denser; the top centimetre is brighter, which gives the surface
                // a meniscus without needing a second pass.
                float depth = Math.Clamp((localSurface - y) / Math.Max(1f, size * 0.35f), 0f, 1f);
                var near = (byte)(255 - (depth * 40f));
                var alpha = (byte)(150 + (depth * 70f));

                bool meniscus = localSurface - y < size * 0.02f;
                pixels[index] = meniscus
                    ? new Color32(255, 210, 235, 235)
                    : new Color32(near, (byte)(70 + (depth * 30f)), (byte)(150 + (depth * 20f)), alpha);
            }
        }

        texture.SetPixels32(pixels);
        Finish(texture);
        return texture;
    }

    /// <summary>
    /// The cross that measures how many climaxes are left.
    ///
    /// Flared ends and a bevelled face rather than three plain bars: it has to look like the
    /// character's own cross for its breaking to mean anything. Damage is carved out of the
    /// silhouette so the shape itself is what degrades.
    /// </summary>
    internal static Texture2D Cross(int width, int height, int notches, bool broken)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];

        float shaft = width * 0.17f;
        float armHalf = width * 0.42f;
        float barCentre = height * 0.70f;
        float barHalf = height * 0.085f;
        float centreX = width / 2f;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                float dx = Math.Abs(x - centreX);
                float dy = y - barCentre;
                var inside = false;

                // Shaft, widening slightly toward each tip so the ends read as flared.
                float verticalEdge = shaft * (1f + (Flare(y / (float)height) * 0.85f));
                if (dx <= verticalEdge)
                {
                    inside = true;
                }

                // Arms, with the same flare toward their tips.
                if (Math.Abs(dy) <= barHalf * (1f + (Flare(dx / armHalf) * 0.9f)) && dx <= armHalf)
                {
                    inside = true;
                }

                int index = (y * width) + x;
                if (!inside)
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                // Bevel: lit from the upper left, so the face has depth instead of being flat.
                float bevel = Math.Clamp(1f - (dx / Math.Max(1f, verticalEdge)), 0f, 1f);
                var lift = (byte)(28f * bevel);
                pixels[index] = new Color32(
                    (byte)Math.Min(255, 214 + lift),
                    (byte)Math.Min(255, 198 + lift),
                    (byte)Math.Min(255, 168 + lift),
                    255);
            }
        }

        CarveRosette(pixels, width, height, centreX, barCentre, width * 0.13f);
        CarveDamage(pixels, width, height, centreX, notches, broken);

        texture.SetPixels32(pixels);
        Finish(texture);
        return texture;
    }

    /// <summary>Widens near 0 and 1 and vanishes in the middle, which is what makes the tips flare.</summary>
    private static float Flare(float t)
    {
        float edge = Math.Min(Math.Clamp(t, 0f, 1f), 1f - Math.Clamp(t, 0f, 1f));
        return edge > 0.09f ? 0f : 1f - (edge / 0.09f);
    }

    /// <summary>The boss at the crossing, cut in rather than drawn on so the bevel stays consistent.</summary>
    private static void CarveRosette(Color32[] pixels, int width, int height, float cx, float cy, float radius)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float distance = (float)Math.Sqrt((dx * dx) + (dy * dy));
                if (distance > radius)
                {
                    continue;
                }

                int index = (y * width) + x;
                if (pixels[index].a == 0)
                {
                    continue;
                }

                // A ring and a dot: enough to read as an ornament at the size this is drawn.
                bool ring = distance > radius * 0.62f && distance < radius * 0.82f;
                bool dot = distance < radius * 0.24f;
                if (ring || dot)
                {
                    pixels[index] = new Color32(176, 156, 120, 255);
                }
            }
        }
    }

    /// <summary>
    /// Bites one notch out of the silhouette per climax, at fixed places so the same damage always
    /// looks the same. When it breaks, the shaft is severed outright.
    /// </summary>
    private static void CarveDamage(Color32[] pixels, int width, int height, float cx, int notches, bool broken)
    {
        (float X, float Y, float R)[] sites =
        {
            (0.62f, 0.30f, 0.10f),
            (0.38f, 0.52f, 0.09f),
            (0.72f, 0.72f, 0.08f),
            (0.30f, 0.86f, 0.09f),
            (0.60f, 0.12f, 0.10f),
            (0.42f, 0.66f, 0.07f),
            (0.68f, 0.44f, 0.08f),
            (0.34f, 0.22f, 0.09f),
        };

        int shown = Math.Min(notches, sites.Length);
        for (var index = 0; index < shown; index++)
        {
            (float sx, float sy, float sr) = sites[index];
            Bite(pixels, width, height, sx * width, sy * height, sr * width);
        }

        if (!broken)
        {
            return;
        }

        // Severed: a clean gap across the shaft below the arms.
        var breakY = (int)(height * 0.52f);
        var gap = (int)Math.Max(2f, height * 0.035f);
        for (int y = breakY; y < Math.Min(height, breakY + gap); y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = new Color32(0, 0, 0, 0);
            }
        }
    }

    private static void Bite(Color32[] pixels, int width, int height, float cx, float cy, float radius)
    {
        var minX = (int)Math.Max(0, cx - radius);
        var maxX = (int)Math.Min(width - 1, cx + radius);
        var minY = (int)Math.Max(0, cy - radius);
        var maxY = (int)Math.Min(height - 1, cy + radius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                if ((dx * dx) + (dy * dy) <= radius * radius)
                {
                    pixels[(y * width) + x] = new Color32(0, 0, 0, 0);
                }
            }
        }
    }

    private static void Finish(Texture2D texture)
    {
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.Apply();

        // Unity drops textures on scene unload unless they are marked, and a dropped texture would
        // throw on every frame the overlay draws.
        texture.hideFlags = HideFlags.HideAndDontSave;
    }
}
