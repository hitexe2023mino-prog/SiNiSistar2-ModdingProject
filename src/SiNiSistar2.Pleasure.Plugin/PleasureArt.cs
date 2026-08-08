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
    /// <summary>
    /// The milk reservoir: a pair of breasts in outline, filling with white from below.
    ///
    /// Two, drawn as strokes with the nipples marked, rather than the single filled lobe this used
    /// to be. The single lobe read as an abstract blob at gauge size — it needed a legend to say
    /// what it was, which is exactly what a HUD element must not need. A pair in outline is
    /// recognised immediately and leaves its interior free for the liquid, so the reading and the
    /// subject are the same shape.
    /// </summary>
    internal static Texture2D MilkVessel(int size, float fill, float phase)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        float clamped = Math.Clamp(fill, 0f, 1f);

        // Row 0 is the top of the drawn rectangle, so the surface descends as it fills.
        float surface = size * (1f - clamped);

        float radius = size * 0.26f;
        float centreY = size * 0.46f;
        float leftX = size * 0.27f;
        float rightX = size * 0.73f;
        float stroke = Math.Max(1.6f, size * 0.055f);
        float nippleR = Math.Max(1.5f, size * 0.055f);
        float nippleY = centreY + (radius * 0.62f);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                int index = (y * size) + x;

                float left = Distance(x, y, leftX, centreY);
                float right = Distance(x, y, rightX, centreY);
                float nearest = Math.Min(left, right);

                if (nearest > radius + (stroke * 0.5f))
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                // The outline, drawn as a band about the circle rather than a filled edge, so both
                // lobes keep their shape when the liquid is high.
                bool outline = nearest > radius - (stroke * 0.5f);
                if (outline)
                {
                    pixels[index] = new Color32(248, 236, 242, 235);
                    continue;
                }

                float nipple = Math.Min(
                    Distance(x, y, leftX, nippleY),
                    Distance(x, y, rightX, nippleY));
                if (nipple < nippleR)
                {
                    pixels[index] = new Color32(242, 186, 208, 245);
                    continue;
                }

                float wave = (float)Math.Sin((x / (double)size * 6.28318) + phase) * size * 0.012f;
                float localSurface = surface + wave;
                if (y < localSurface)
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float depth = Math.Clamp((y - localSurface) / Math.Max(1f, size * 0.5f), 0f, 1f);
                bool meniscus = y - localSurface < size * 0.03f;
                pixels[index] = meniscus
                    ? new Color32(255, 255, 255, 248)
                    : new Color32(
                        252,
                        (byte)(248 - (depth * 10f)),
                        (byte)(240 - (depth * 20f)),
                        (byte)(205 + (depth * 45f)));
            }
        }

        Upload(texture, pixels, size, size);
        return texture;
    }

    private static float Distance(int x, int y, float cx, float cy)
    {
        float dx = x - cx + 0.5f;
        float dy = y - cy + 0.5f;
        return (float)Math.Sqrt((dx * dx) + (dy * dy));
    }

    internal static Texture2D LiquidDisc(int size, float fill, float phase)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        float radius = size / 2f;
        float clamped = Math.Clamp(fill, 0f, 1f);

        // GUI.Label draws row 0 at the top of the rectangle, so the liquid has to rise toward
        // row 0 rather than away from it. Getting this backwards put the pool on the ceiling.
        float surface = size * (1f - clamped);

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

                float wave = (float)Math.Sin((x / (double)size * 6.28318) + phase) * size * 0.014f;
                float localSurface = surface + wave;

                // Above the liquid only the rim is drawn. Filling the empty part washed out the
                // dial's own art underneath, but leaving nothing at all made an empty gauge
                // indistinguishable from a broken one, so the rim stays as the marker.
                bool rim = distance > radius - Math.Max(1.5f, size * 0.03f);
                if (y < localSurface)
                {
                    pixels[index] = rim
                        ? new Color32(214, 138, 186, 90)
                        : new Color32(0, 0, 0, 0);
                    continue;
                }

                // Deeper liquid is denser, which gives the pool a body instead of a flat wash.
                float depth = Math.Clamp((y - localSurface) / Math.Max(1f, size * 0.55f), 0f, 1f);
                var alpha = (byte)(165 + (depth * 70f));

                bool meniscus = y - localSurface < size * 0.022f;
                if (rim)
                {
                    pixels[index] = new Color32(230, 150, 200, 200);
                    continue;
                }

                pixels[index] = meniscus
                    ? new Color32(255, 214, 238, 240)
                    : new Color32(
                        (byte)(255 - (depth * 40f)),
                        (byte)(78 + (depth * 26f)),
                        (byte)(158 + (depth * 18f)),
                        alpha);
            }
        }

        Upload(texture, pixels, size, size);
        return texture;
    }

    /// <summary>
    /// The cross that measures how many climaxes are left.
    ///
    /// It crumbles from the top down: the head goes first, then the arms, and the shaft last, so
    /// the silhouette is visibly losing ground long before it fails. At the limit it comes apart
    /// into fragments. Flared ends and a bevelled face keep it reading as the character's own
    /// cross rather than as a progress bar in the shape of one.
    /// </summary>
    internal static Texture2D Cross(int width, int height, float progress, bool shattered)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];

        float shaft = width * 0.17f;
        float armHalf = width * 0.42f;
        float barCentre = height * 0.30f;
        float barHalf = height * 0.085f;
        float centreX = width / 2f;
        float eaten = Math.Clamp(progress, 0f, 1f);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                float dx = Math.Abs(x - centreX);
                float dy = y - barCentre;
                var inside = false;

                float verticalEdge = shaft * (1f + (Flare(y / (float)height) * 0.85f));
                if (dx <= verticalEdge)
                {
                    inside = true;
                }

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

                // The erosion front walks down from the top, with a ragged edge so it reads as
                // stone breaking away rather than as a rectangle being cropped.
                float front = eaten * height * 1.02f;
                float ragged = front + (Noise(x * 0.21f) * height * 0.05f);
                if (y < ragged)
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                // Crumbling stone loses grip just behind the front, so the last few rows go patchy.
                if (y < ragged + (height * 0.05f) && Noise((x * 0.7f) + (y * 1.3f)) > 0.45f)
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

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

        if (shattered)
        {
            Shatter(pixels, width, height);
        }

        Upload(texture, pixels, width, height);
        return texture;
    }

    /// <summary>
    /// Breaks whatever is left into fragments: the image is diced into cells and most of them are
    /// thrown away, leaving scattered shards where the cross was.
    /// </summary>
    private static void Shatter(Color32[] pixels, int width, int height)
    {
        var cell = Math.Max(3, width / 12);
        for (var cy = 0; cy < height; cy += cell)
        {
            for (var cx = 0; cx < width; cx += cell)
            {
                // Two thirds of the cells go entirely; the survivors are nudged apart so the
                // remains read as pieces rather than as a cross with holes in it.
                bool keep = Noise((cx * 0.37f) + (cy * 0.11f)) > 0.66f;
                int shift = keep ? (int)((Noise(cx * 0.5f) - 0.5f) * cell * 1.6f) : 0;

                for (int y = cy; y < Math.Min(height, cy + cell); y++)
                {
                    for (int x = cx; x < Math.Min(width, cx + cell); x++)
                    {
                        int source = (y * width) + x;
                        Color32 value = pixels[source];
                        pixels[source] = new Color32(0, 0, 0, 0);

                        if (!keep || value.a == 0)
                        {
                            continue;
                        }

                        int tx = x + shift;
                        int ty = y + (shift / 2);
                        if (tx >= 0 && tx < width && ty >= 0 && ty < height)
                        {
                            pixels[(ty * width) + tx] = value;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Repeatable pseudo-noise in 0..1. Deterministic on purpose: the same damage has to look the
    /// same every frame, or the cross would boil.
    /// </summary>
    private static float Noise(float t)
    {
        double v = Math.Sin(t * 12.9898) * 43758.5453;
        return (float)(v - Math.Floor(v));
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
    /// Uploads the pixels, flipping them on the way.
    ///
    /// Unity stores a texture with row 0 at the bottom, but every generator here is written the way
    /// the shapes are described — top row first. Flipping once at the boundary keeps that single
    /// awkward fact in one place instead of inverting the arithmetic in each generator, which is
    /// how the gauge ended up filling from the ceiling.
    /// </summary>
    private static void Upload(Texture2D texture, Color32[] pixels, int width, int height)
    {
        var flipped = new Color32[pixels.Length];
        for (var y = 0; y < height; y++)
        {
            int source = y * width;
            int target = (height - 1 - y) * width;
            Array.Copy(pixels, source, flipped, target, width);
        }

        texture.SetPixels32(flipped);
        Finish(texture);
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
