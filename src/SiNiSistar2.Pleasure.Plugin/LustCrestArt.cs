using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Draws the lust crest, part by part, as corruption rises (SPEC003 5.7, FR-266).
///
/// The crest is the game's own mark, redrawn rather than borrowed: the sprite the game uses lives
/// on the body texture and there is no way to lift it out for a HUD without unpacking a bundle. So
/// it is rebuilt from curves, which also lets it come apart — which is the point. Corruption is not
/// a bar; it is a mark that finishes itself.
///
/// The parts are revealed from the outside in: the ring first, then the outermost flourish, then
/// the horns, the inner curls, the heart, and last the core. Outside-in is what makes the last step
/// read as an arrival: the heart is the thing the mark is about, and it is the thing that comes
/// last.
///
/// Every part is a signed distance field, the same approach the milk gauge uses (DEC-231). Distance
/// fields cost more to build than a stamped sprite, but they stay sharp at any HUD size and the
/// glow falls out of the same number rather than needing a second pass. The whole shape was settled
/// offscreen before any of it shipped.
/// </summary>
internal static class LustCrestArt
{
    /// <summary>How many parts the mark comes in. Corruption is divided into this many steps.</summary>
    internal const int PartCount = 6;

    private static readonly Color Ink = new(1f, 0.376f, 0.698f, 1f);
    private static readonly Color Halo = new(1f, 0.745f, 0.878f, 1f);

    private static Vector2[][]? _strokes;
    private static float[][]? _widths;
    private static int[]? _partOf;

    /// <summary>
    /// The mark with the first <paramref name="revealed"/> parts drawn.
    ///
    /// Nothing here pulses. The pulse is an alpha applied when the texture is drawn, so it costs a
    /// colour rather than a rebuild, and every part breathes together — which is what makes the mark
    /// read as one thing rather than a row of indicators.
    /// </summary>
    internal static Texture2D Build(int size, int revealed)
    {
        size = Math.Max(24, size);
        revealed = Math.Clamp(revealed, 0, PartCount);
        EnsureShape();

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        float feather = 2.4f / size;
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y++)
        {
            // Texture rows run bottom-up; the shape is authored with y upwards, so this is a
            // direct mapping rather than a flip.
            float v = ((y / (float)(size - 1)) * 2f) - 1f;
            for (var x = 0; x < size; x++)
            {
                float u = ((x / (float)(size - 1)) * 2f) - 1f;
                float d = Distance(u, v, revealed);

                float a = Math.Clamp(0.5f - (d / (feather * 2f)), 0f, 1f);
                float halo = Math.Clamp(0.5f - ((d - (feather * 2f)) / (feather * 10f)), 0f, 1f) * 0.45f;
                if (a <= 0f && halo <= 0f)
                {
                    pixels[(y * size) + x] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                var colour = new Color(
                    (Ink.r * a) + (Halo.r * halo),
                    (Ink.g * a) + (Halo.g * halo),
                    (Ink.b * a) + (Halo.b * halo),
                    Math.Clamp(a + (halo * 0.5f), 0f, 1f));
                pixels[(y * size) + x] = colour;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false);
        return texture;
    }

    /// <summary>Distance to the nearest revealed part, negative inside it.</summary>
    private static float Distance(float x, float y, int revealed)
    {
        if (revealed <= 0)
        {
            return 1f;
        }

        float best = 1f;

        // The ring is analytic rather than a stroke: a circle costs two operations, and as a
        // polyline it would be the most expensive part of the whole mark.
        float radius = (float)Math.Sqrt((x * x) + (y * y));
        float band = Math.Abs(radius - 0.93f) - 0.011f;
        float angle = Math.Abs((float)Math.Atan2(y, x));
        float gap = Math.Min(Math.Abs(angle - ((float)Math.PI / 2f)) - 0.30f, 0f);
        best = Math.Max(band, (-gap * 4f) - 0.001f);

        Vector2[][] strokes = _strokes!;
        float[][] widths = _widths!;
        int[] partOf = _partOf!;
        for (var s = 0; s < strokes.Length; s++)
        {
            if (partOf[s] >= revealed)
            {
                continue;
            }

            best = Math.Min(best, StrokeDistance(x, y, strokes[s], widths[s]));
        }

        // The core is a filled heart rather than an outline, so it needs the inside of the curve as
        // well as the curve. The polynomial's magnitude is not a distance, but its sign is sound.
        if (revealed >= 6)
        {
            best = Math.Min(best, CoreDistance(x, y));
        }

        return best;
    }

    private static float StrokeDistance(float x, float y, Vector2[] points, float[] width)
    {
        float best = 1f;
        int last = points.Length - 1;
        for (var i = 0; i < last; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[i + 1];
            float vx = b.x - a.x;
            float vy = b.y - a.y;
            float wx = x - a.x;
            float wy = y - a.y;
            float dd = (vx * vx) + (vy * vy);
            float t = dd > 1e-9f ? Math.Clamp(((wx * vx) + (wy * vy)) / dd, 0f, 1f) : 0f;
            float dx = wx - (vx * t);
            float dy = wy - (vy * t);
            float d = (float)Math.Sqrt((dx * dx) + (dy * dy));

            // The width tapers along the run, which is what turns a stroked curve into a flourish
            // rather than a length of pipe.
            float u = (i + t) / last;
            float w = width[0] + ((width[1] - width[0]) * u);
            best = Math.Min(best, d - w);
        }

        return best;
    }

    private static float CoreDistance(float x, float y)
    {
        const float scale = 0.21f;
        const float centre = -0.03f;
        float best = StrokeDistance(x, y, Heart(scale, centre), new[] { 0f, 0f });

        float hx = x / (scale * 0.98f);
        float hy = (y - centre) / (scale * 0.98f);
        float q = (hx * hx) + (hy * hy) - 1f;
        float implicitValue = (q * q * q) - ((hx * hx) * (hy * hy * hy));
        return implicitValue <= 0f ? -best : best;
    }

    /// <summary>
    /// The strokes, built once. Every curve is authored in the same square the texture samples, so
    /// nothing here depends on the size it is finally drawn at.
    /// </summary>
    private static void EnsureShape()
    {
        if (_strokes is not null)
        {
            return;
        }

        var strokes = new List<Vector2[]>();
        var widths = new List<float[]>();
        var parts = new List<int>();

        void Add(int part, Vector2[] stroke, float from, float to)
        {
            strokes.Add(stroke);
            widths.Add(new[] { from, to });
            parts.Add(part);

            strokes.Add(Mirror(stroke));
            widths.Add(new[] { from, to });
            parts.Add(part);
        }

        // Part 1 is the ring, drawn analytically. Parts are numbered from zero here.

        // Part 2: the outermost flourish, a long sweep out to the ring that hooks back.
        Add(1, Bezier(new(0.30f, 0.34f), new(0.62f, 0.74f), new(0.92f, 0.44f), new(0.70f, 0.18f), 40), 0.030f, 0.014f);
        Add(1, Bezier(new(0.70f, 0.18f), new(0.58f, 0.02f), new(0.44f, 0.14f), new(0.53f, 0.25f), 28), 0.014f, 0.003f);

        // Part 3: the horns, rising from the heart's shoulder and curling inward at the top.
        Add(2, Bezier(new(0.14f, 0.30f), new(0.30f, 0.66f), new(0.62f, 0.66f), new(0.58f, 0.40f), 40), 0.034f, 0.016f);
        Add(2, Bezier(new(0.58f, 0.40f), new(0.55f, 0.24f), new(0.38f, 0.28f), new(0.44f, 0.40f), 28), 0.016f, 0.003f);

        // Part 4: the tight pair hugging the heart's shoulders.
        Add(3, Bezier(new(0.08f, 0.14f), new(0.26f, 0.32f), new(0.40f, 0.14f), new(0.26f, 0.06f), 32), 0.022f, 0.004f);

        // Part 5: the heart, and the tail hanging from its point. The tail belongs to the heart —
        // on its own it reads as a stray comma floating in the ring.
        strokes.Add(Heart(0.40f, -0.04f));
        widths.Add(new[] { 0.028f, 0.028f });
        parts.Add(4);
        strokes.Add(Bezier(new(0f, -0.46f), new(0.05f, -0.56f), new(0f, -0.66f), new(0f, -0.70f), 20));
        widths.Add(new[] { 0.026f, 0.004f });
        parts.Add(4);

        // Part 6 is the filled core, handled separately: it needs an inside, not a stroke.

        _strokes = strokes.ToArray();
        _widths = widths.ToArray();
        _partOf = parts.ToArray();
    }

    private static Vector2[] Mirror(Vector2[] points)
    {
        var flipped = new Vector2[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            flipped[i] = new Vector2(-points[i].x, points[i].y);
        }

        return flipped;
    }

    private static Vector2[] Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int steps)
    {
        var points = new Vector2[steps];
        for (var i = 0; i < steps; i++)
        {
            float t = i / (float)(steps - 1);
            float s = 1f - t;
            points[i] = new Vector2(
                (s * s * s * p0.x) + (3f * s * s * t * p1.x) + (3f * s * t * t * p2.x) + (t * t * t * p3.x),
                (s * s * s * p0.y) + (3f * s * s * t * p1.y) + (3f * s * t * t * p2.y) + (t * t * t * p3.y));
        }

        return points;
    }

    private static Vector2[] Heart(float scale, float centre)
    {
        const int steps = 120;
        var points = new Vector2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            double t = (i / (double)steps) * 2d * Math.PI;
            double sin = Math.Sin(t);
            double x = 16d * sin * sin * sin;
            double y = (13d * Math.Cos(t))
                - (5d * Math.Cos(2d * t))
                - (2d * Math.Cos(3d * t))
                - Math.Cos(4d * t);
            points[i] = new Vector2(
                (float)(x / 17d) * scale,
                ((float)(y / 17d) * scale) + centre);
        }

        return points;
    }
}
