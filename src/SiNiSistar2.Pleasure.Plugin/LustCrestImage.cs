using BepInEx;
using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Projects the real crest image, when one has been provided (SPEC003 FR-270, FR-271, DEC-246).
///
/// The mark has to match the game's own crest exactly — overlaid, identical. Hand-authored curves
/// cannot converge on that: every iteration was judged against the reference by eye, and "closer"
/// is not "the same". Projecting the actual image is the only construction that is identical by
/// definition, and it was explicitly sanctioned.
///
/// Three sources, best first, all from the plugin's config folder:
///
/// 1. <c>lust-crest-Lv1.png</c> … <c>lust-crest-Lv6.png</c> — the mark separated into the parts it
///    is revealed in, authored by hand. This is the best source because the boundaries are the
///    author's rather than the MOD's: a part appears exactly where the artwork says one begins.
///    <c>scripts/pdn_layers.py</c> writes these straight out of a Paint.NET file.
/// 2. <c>lust-crest.png</c> — one flat image. The reveal then has to be invented, and it is done
///    with a radial frontier, which is a guess about anatomy that the layered source does not need
///    to make.
/// 3. Neither — <see cref="LustCrestArt"/> draws an approximation from curves, so a missing file
///    costs fidelity rather than the mechanism.
///
/// A white background is keyed out automatically for a flat image, because the reference is
/// distributed on white. Layers carry their own alpha and are trusted as authored.
/// </summary>
internal static class LustCrestImage
{
    private const string FlatName = "lust-crest.png";
    private const string LayerFormat = "lust-crest-Lv{0}.png";

    private static bool _attempted;
    private static byte[][]? _layers;
    private static int _width;
    private static int _height;
    private static int _minX;
    private static int _minY;
    private static int _maxX;
    private static int _maxY;

    /// <summary>Whether a real image is loaded and ready to project.</summary>
    internal static bool Available => Load();

    /// <summary>Whether the mark came in as separate parts rather than one flat picture.</summary>
    internal static bool IsLayered => Load() && _layers!.Length > 1;

    /// <summary>Width over height of the mark's actual content, margins trimmed.</summary>
    internal static float Aspect
    {
        get
        {
            if (!Load())
            {
                return LustCrestArt.AspectRatio;
            }

            float w = Math.Max(1, _maxX - _minX + 1);
            float h = Math.Max(1, _maxY - _minY + 1);
            return w / h;
        }
    }

    /// <summary>
    /// The mark with its first <paramref name="revealed"/> parts drawn, at the size it will occupy.
    ///
    /// Built at the drawn size rather than scaled afterwards, because <c>GUI.Label</c> draws a
    /// texture at its own size (付録A A-43).
    ///
    /// The content box is the union of every layer, not of the revealed ones, so the mark keeps its
    /// place on the HUD as it fills in. Cropping to what is currently visible would make the first
    /// part fill the box and then appear to shrink as the rest arrived.
    /// </summary>
    internal static Texture2D Build(int height, int revealed)
    {
        Load();
        height = Math.Max(24, height);
        var width = (int)(height * Aspect);
        revealed = Math.Clamp(revealed, 0, LustCrestArt.PartCount);

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        byte[][] layers = _layers!;
        int count = layers.Length;
        float contentW = _maxX - _minX + 1;
        float contentH = _maxY - _minY + 1;

        // One flat image has no parts of its own, so the reveal is a radial frontier over it.
        // Layers make this unnecessary, which is the reason to prefer them.
        bool radial = count == 1;
        float threshold = radial
            ? (LustCrestArt.PartCount - revealed) / (float)LustCrestArt.PartCount
            : 0f;
        int take = radial ? 1 : Math.Min(revealed, count);

        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1);
            float sy = _minY + (v * (contentH - 1));
            for (var x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);
                float sx = _minX + (u * (contentW - 1));

                // Composited in the order the parts were authored in, each over the ones before.
                var colour = new Color(0f, 0f, 0f, 0f);
                for (var k = 0; k < take; k++)
                {
                    Color sample = Sample(layers[k], sx, sy);
                    if (sample.a <= 0f)
                    {
                        continue;
                    }

                    float inverse = 1f - sample.a;
                    colour = new Color(
                        (sample.r * sample.a) + (colour.r * inverse),
                        (sample.g * sample.a) + (colour.g * inverse),
                        (sample.b * sample.a) + (colour.b * inverse),
                        sample.a + (colour.a * inverse));
                }

                if (radial && threshold > 0f)
                {
                    float nx = (u * 2f) - 1f;
                    float ny = (v * 2f) - 1f;
                    float r = (float)Math.Sqrt((nx * nx) + (ny * ny));
                    colour.a *= Math.Clamp((r - threshold + 0.03f) / 0.06f, 0f, 1f);
                }

                pixels[(y * width) + x] = colour;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false);
        return texture;
    }

    private static Color Sample(byte[] source, float sx, float sy)
    {
        var x0 = (int)Math.Floor(sx);
        var y0 = (int)Math.Floor(sy);
        int x1 = Math.Min(x0 + 1, _width - 1);
        int y1 = Math.Min(y0 + 1, _height - 1);
        x0 = Math.Clamp(x0, 0, _width - 1);
        y0 = Math.Clamp(y0, 0, _height - 1);
        float fx = sx - x0;
        float fy = sy - y0;

        Color a = At(source, x0, y0);
        Color b = At(source, x1, y0);
        Color c = At(source, x0, y1);
        Color d = At(source, x1, y1);
        return Color.Lerp(Color.Lerp(a, b, fx), Color.Lerp(c, d, fx), fy);
    }

    private static Color At(byte[] source, int x, int y)
    {
        int i = ((y * _width) + x) * 4;
        const float scale = 1f / 255f;
        return new Color(source[i] * scale, source[i + 1] * scale, source[i + 2] * scale, source[i + 3] * scale);
    }

    private static bool Load()
    {
        if (_attempted)
        {
            return _layers is not null;
        }

        _attempted = true;
        string folder = Path.Combine(Paths.ConfigPath, PleasurePlugin.PluginGuid);

        try
        {
            var layers = new List<byte[]>();
            for (var level = 1; level <= LustCrestArt.PartCount; level++)
            {
                string path = Path.Combine(folder, string.Format(LayerFormat, level));
                if (!File.Exists(path))
                {
                    break;
                }

                byte[]? pixels = Decode(path, keyWhite: false);
                if (pixels is null)
                {
                    return false;
                }

                layers.Add(pixels);
            }

            if (layers.Count == LustCrestArt.PartCount)
            {
                _layers = layers.ToArray();
                MeasureContent();
                PleasureRuntime.Log?.LogInfo(
                    $"The crest was loaded as {layers.Count} authored parts from '{folder}' "
                    + $"({_width}x{_height}, content {_maxX - _minX + 1}x{_maxY - _minY + 1}). They "
                    + "are drawn in the order they were authored in.");
                return true;
            }

            if (layers.Count > 0)
            {
                PleasureRuntime.Log?.LogWarning(
                    $"Only {layers.Count} of {LustCrestArt.PartCount} crest parts were found in "
                    + $"'{folder}'. All of lust-crest-Lv1.png .. Lv{LustCrestArt.PartCount}.png are "
                    + "needed, so the flat image is used instead.");
            }

            string flat = Path.Combine(folder, FlatName);
            if (!File.Exists(flat))
            {
                PleasureRuntime.Log?.LogInfo(
                    $"No crest image in '{folder}'; the lust crest is drawn from curves instead. "
                    + $"Put the parts there as {string.Format(LayerFormat, "1")} .. "
                    + $"{string.Format(LayerFormat, LustCrestArt.PartCount)} to have the exact mark "
                    + "projected.");
                return false;
            }

            byte[]? single = Decode(flat, keyWhite: true);
            if (single is null)
            {
                return false;
            }

            _layers = new[] { single };
            MeasureContent();
            PleasureRuntime.Log?.LogInfo(
                $"The crest image was loaded from '{flat}' ({_width}x{_height}, content "
                + $"{_maxX - _minX + 1}x{_maxY - _minY + 1}). It has no authored parts, so the "
                + "reveal uses a radial frontier.");
            return true;
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning(
                $"The crest image could not be read from '{folder}': {exception.Message}. The drawn "
                + "crest is used.");
            return false;
        }
    }

    /// <summary>
    /// One PNG as straight RGBA bytes, at the resolution the file carries.
    ///
    /// Bytes rather than <c>Color</c>: the interop array hands back four floats per pixel, which for
    /// six layers of this size is eighty megabytes to hold for the session against twenty-one.
    /// </summary>
    private static byte[]? Decode(string path, bool keyWhite)
    {
        byte[] data = File.ReadAllBytes(path);
        var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(loaded, data, false))
        {
            PleasureRuntime.Log?.LogWarning($"The crest image at '{path}' could not be decoded.");
            return null;
        }

        if (_width == 0)
        {
            _width = loaded.width;
            _height = loaded.height;
        }
        else if (loaded.width != _width || loaded.height != _height)
        {
            PleasureRuntime.Log?.LogWarning(
                $"'{path}' is {loaded.width}x{loaded.height} but the first part was {_width}x{_height}. "
                + "Every part has to share one canvas or they cannot be composited.");
            UnityEngine.Object.Destroy(loaded);
            return null;
        }

        var raw = loaded.GetPixels();
        UnityEngine.Object.Destroy(loaded);

        var pixels = new byte[_width * _height * 4];
        var authoredAlpha = false;
        for (var i = 0; i < raw.Length; i++)
        {
            Color p = raw[i];
            if (p.a < 0.97f)
            {
                authoredAlpha = true;
            }

            int o = i * 4;
            pixels[o] = (byte)(Math.Clamp(p.r, 0f, 1f) * 255f);
            pixels[o + 1] = (byte)(Math.Clamp(p.g, 0f, 1f) * 255f);
            pixels[o + 2] = (byte)(Math.Clamp(p.b, 0f, 1f) * 255f);
            pixels[o + 3] = (byte)(Math.Clamp(p.a, 0f, 1f) * 255f);
        }

        if (keyWhite && !authoredAlpha)
        {
            // White is background, everything else is mark. The colours themselves are left
            // untouched — they are the thing that has to match.
            for (var i = 0; i < pixels.Length; i += 4)
            {
                int lowest = Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2]));
                float distance = 1f - (lowest / 255f);
                pixels[i + 3] = (byte)(Math.Clamp((distance - 0.06f) / 0.22f, 0f, 1f) * 255f);
            }
        }

        return pixels;
    }

    /// <summary>
    /// The box every part shares, so the mark does not move on the HUD as it fills in.
    /// </summary>
    private static void MeasureContent()
    {
        _minX = _width;
        _minY = _height;
        _maxX = 0;
        _maxY = 0;

        foreach (byte[] layer in _layers!)
        {
            for (var y = 0; y < _height; y++)
            {
                for (var x = 0; x < _width; x++)
                {
                    if (layer[(((y * _width) + x) * 4) + 3] <= 12)
                    {
                        continue;
                    }

                    _minX = Math.Min(_minX, x);
                    _minY = Math.Min(_minY, y);
                    _maxX = Math.Max(_maxX, x);
                    _maxY = Math.Max(_maxY, y);
                }
            }
        }

        if (_maxX < _minX)
        {
            _minX = 0;
            _minY = 0;
            _maxX = _width - 1;
            _maxY = _height - 1;
        }
    }
}
