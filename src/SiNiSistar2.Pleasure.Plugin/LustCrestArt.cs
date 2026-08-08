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
/// The shape follows the reference: a wide tribal banner rather than a ring. A heart at the centre
/// with a smaller heart inside it and a flame rising between its lobes, flanked by symmetric arms
/// that sweep out and end in points, with hooks standing above them. The first version was a circle
/// with curls inside, which was wrong about the silhouette as well as the parts.
///
/// Two-tone, as the reference is: dark plum at the extremities and hot pink towards the middle.
/// That falls out of the reveal order rather than being applied on top of it — each part is painted
/// over the ones outside it, so the mark is darkest where it started and brightest where it ends.
///
/// The parts are revealed from the outside in: the wing tips first, then the arms, the hooks, the
/// shoulders, the heart, and last the core. Outside-in is what makes the last step read as an
/// arrival — the heart is the thing the mark is about, and it is the thing that comes last.
///
/// Everything is a tapered stroke whose width falls to zero at the tip, which is what makes a
/// tribal point rather than a length of pipe. The whole shape was settled offscreen before any of
/// it shipped (DEC-231); <c>scripts/crest_preview.py</c> is the same figure in numpy.
/// </summary>
internal static class LustCrestArt
{
    /// <summary>How many parts the mark comes in. Corruption is divided into this many steps.</summary>
    internal const int PartCount = 6;

    /// <summary>The banner is twice as wide as it is tall.</summary>
    internal const float AspectRatio = 2f;

    // Dark plum at the extremities, hot pink towards the middle, as the reference does.
    private static readonly Color[] Tones =
    {
        new(0.424f, 0.071f, 0.267f, 1f),
        new(0.588f, 0.094f, 0.361f, 1f),
        new(0.769f, 0.133f, 0.478f, 1f),
        new(0.886f, 0.204f, 0.580f, 1f),
        new(0.980f, 0.306f, 0.659f, 1f),
        new(1f, 0.588f, 0.804f, 1f),
    };

    private static Stroke[][]? _parts;

    private readonly struct Stroke
    {
        internal Stroke(Vector2[] points, float from, float to, bool filled)
        {
            Points = points;
            From = from;
            To = to;
            Filled = filled;
        }

        internal Vector2[] Points { get; }

        internal float From { get; }

        internal float To { get; }

        /// <summary>Whether the inside of the curve counts, not only the curve itself.</summary>
        internal bool Filled { get; }
    }

    /// <summary>
    /// The mark with the first <paramref name="revealed"/> parts drawn.
    ///
    /// Nothing here pulses. The pulse is an alpha applied when the texture is drawn, so it costs a
    /// colour rather than a rebuild, and every part breathes together — which is what makes the mark
    /// read as one thing rather than a row of indicators.
    /// </summary>
    internal static Texture2D Build(int height, int revealed)
    {
        height = Math.Max(24, height);
        var width = (int)(height * AspectRatio);
        revealed = Math.Clamp(revealed, 0, PartCount);
        EnsureShape();

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        float feather = 2.4f / height;
        var pixels = new Color[width * height];
        Stroke[][] parts = _parts!;

        for (var y = 0; y < height; y++)
        {
            // Texture rows run bottom-up and the shape is authored with y upwards, so this is a
            // direct mapping. The vertical span is half the horizontal one: the banner is wide.
            float v = (y / (float)(height - 1)) - 0.5f;
            for (var x = 0; x < width; x++)
            {
                float u = ((x / (float)(width - 1)) * 2f) - 1f;

                var colour = new Color(0f, 0f, 0f, 0f);
                for (var k = 0; k < revealed; k++)
                {
                    float d = PartDistance(parts[k], u, v);
                    float a = Math.Clamp(0.5f - (d / (feather * 2f)), 0f, 1f);
                    float halo = Math.Clamp(0.5f - ((d - (feather * 2f)) / (feather * 8f)), 0f, 1f) * 0.35f;
                    float cover = Math.Clamp(a + (halo * 0.5f), 0f, 1f);
                    if (cover <= 0f)
                    {
                        continue;
                    }

                    // Painted over rather than blended by distance: a part nearer the middle belongs
                    // in front of the ones outside it, and that is what makes the two-tone read.
                    Color tone = Tones[k];
                    colour = new Color(
                        (colour.r * (1f - cover)) + (tone.r * cover),
                        (colour.g * (1f - cover)) + (tone.g * cover),
                        (colour.b * (1f - cover)) + (tone.b * cover),
                        Math.Max(colour.a, cover));
                }

                pixels[(y * width) + x] = colour;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false);
        return texture;
    }

    private static float PartDistance(Stroke[] strokes, float x, float y)
    {
        float best = 1f;
        for (var i = 0; i < strokes.Length; i++)
        {
            best = Math.Min(best, StrokeDistance(x, y, strokes[i]));
        }

        return best;
    }

    private static float StrokeDistance(float x, float y, Stroke stroke)
    {
        Vector2[] points = stroke.Points;
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
            best = Math.Min(best, d - (stroke.From + ((stroke.To - stroke.From) * u)));
        }

        if (!stroke.Filled)
        {
            return best;
        }

        // The core is a filled heart, so the inside counts too. The polynomial's magnitude is not a
        // distance, but its sign is sound, which is all that is needed to flip the sign of one.
        const float scale = 0.175f;
        const float centre = -0.03f;
        float hx = x / (scale * 0.98f);
        float hy = (y - centre) / (scale * 0.98f);
        float q = (hx * hx) + (hy * hy) - 1f;
        return (q * q * q) - ((hx * hx) * (hy * hy * hy)) <= 0f ? -best : best;
    }

    /// <summary>
    /// The strokes, built once. Every curve is authored in the same rectangle the texture samples,
    /// so nothing here depends on the size it is finally drawn at.
    /// </summary>
    private static void EnsureShape()
    {
        if (_parts is not null)
        {
            return;
        }

        _parts = new[]
        {
            // The far ends: a long point sweeping out and down, with a barb above it.
            Pair(
                (Bezier(new(0.58f, 0.02f), new(0.78f, 0.05f), new(0.92f, -0.04f), new(1.00f, -0.20f), 36), 0.048f, 0f),
                (Bezier(new(0.70f, 0.04f), new(0.84f, 0.10f), new(0.90f, 0.05f), new(0.95f, 0.15f), 28), 0.024f, 0f)),

            // The main arms, and the downward flick beneath each.
            Pair(
                (Bezier(new(0.33f, 0.03f), new(0.48f, 0.10f), new(0.64f, 0.04f), new(0.82f, -0.05f), 36), 0.070f, 0.032f),
                (Bezier(new(0.40f, -0.04f), new(0.54f, -0.14f), new(0.66f, -0.18f), new(0.78f, -0.28f), 32), 0.034f, 0f)),

            // The spikes standing above the arms, tallest nearest the heart.
            Pair(
                (Bezier(new(0.36f, 0.05f), new(0.44f, 0.22f), new(0.54f, 0.26f), new(0.50f, 0.38f), 32), 0.044f, 0f),
                (Bezier(new(0.55f, 0.03f), new(0.66f, 0.14f), new(0.76f, 0.14f), new(0.75f, 0.25f), 28), 0.032f, 0f)),

            // The pair curling in against the heart, and the barbs under them.
            Pair(
                (Bezier(new(0.26f, 0.08f), new(0.36f, 0.24f), new(0.46f, 0.16f), new(0.36f, 0.04f), 32), 0.034f, 0f),
                (Bezier(new(0.28f, -0.06f), new(0.38f, -0.14f), new(0.46f, -0.14f), new(0.50f, -0.24f), 28), 0.028f, 0f)),

            // The heart, and the flame rising from between its lobes.
            new[]
            {
                new Stroke(Heart(0.34f, -0.05f), 0.032f, 0.032f, false),
                new Stroke(Bezier(new(0f, 0.09f), new(0.05f, 0.24f), new(0.02f, 0.36f), new(0f, 0.50f), 26), 0.050f, 0f, false),
                new Stroke(Mirror(Bezier(new(0f, 0.09f), new(0.05f, 0.24f), new(0.02f, 0.36f), new(0f, 0.50f), 26)), 0.050f, 0f, false),
            },

            // The smaller heart inside, filled: the brightest thing on the mark.
            new[] { new Stroke(Heart(0.175f, -0.03f), 0f, 0f, true) },
        };
    }

    /// <summary>One shape and its mirror image, which is how every flanking part is made.</summary>
    private static Stroke[] Pair(
        (Vector2[] Points, float From, float To) first,
        (Vector2[] Points, float From, float To) second)
    {
        return new[]
        {
            new Stroke(first.Points, first.From, first.To, false),
            new Stroke(Mirror(first.Points), first.From, first.To, false),
            new Stroke(second.Points, second.From, second.To, false),
            new Stroke(Mirror(second.Points), second.From, second.To, false),
        };
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
        const int steps = 110;
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
