using BepInEx;
using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Projects the real crest image, when one has been provided (SPEC003 FR-270, DEC-246).
///
/// The mark has to match the game's own crest exactly — overlaid, identical. Hand-authored curves
/// cannot converge on that: every iteration was judged against the reference by eye, and "closer"
/// is not "the same". Projecting the actual image is the only construction that is identical by
/// definition, and it was explicitly sanctioned.
///
/// The file is read from the plugin's config folder, next to the enemy catalogue. If it is absent,
/// the drawn approximation in <see cref="LustCrestArt"/> still works, so the HUD never loses the
/// mechanism to a missing file — it only loses fidelity, and says so once in the log.
///
/// A white background is keyed out automatically, because the reference is distributed on white.
/// An image that already carries an alpha channel is trusted as authored.
///
/// The outside-in reveal is elliptical distance from the mark's centre rather than authored part
/// boundaries: the crest's own anatomy is concentric — wing tips, wings, hooks, shoulders, heart,
/// core — so a radial frontier crosses its parts in exactly the order the requirement asks for,
/// without inventing segmentation the image does not carry.
/// </summary>
internal static class LustCrestImage
{
    private const string FileName = "lust-crest.png";

    private static bool _attempted;
    private static Color[]? _pixels;
    private static int _width;
    private static int _height;
    private static int _minX;
    private static int _minY;
    private static int _maxX;
    private static int _maxY;

    /// <summary>Whether a real image is loaded and ready to project.</summary>
    internal static bool Available => Load();

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
    /// The image resampled to the drawn size, with only the outer <paramref name="revealed"/>
    /// sixths of its radius visible.
    ///
    /// Resampled rather than drawn scaled, because <c>GUI.Label</c> draws a texture at its own
    /// size (付録A A-43) — the texture has to be built at the size it will occupy on screen.
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

        Color[] source = _pixels!;
        float contentW = _maxX - _minX + 1;
        float contentH = _maxY - _minY + 1;
        float threshold = (LustCrestArt.PartCount - revealed) / (float)LustCrestArt.PartCount;

        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1);
            float sy = _minY + (v * (contentH - 1));
            for (var x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);
                float sx = _minX + (u * (contentW - 1));

                Color colour = Sample(source, sx, sy);

                // The reveal frontier. Fully-revealed skips the gate entirely so the exact centre
                // is never half-hidden by the soft edge.
                if (threshold > 0f)
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

    private static Color Sample(Color[] source, float sx, float sy)
    {
        var x0 = (int)Math.Floor(sx);
        var y0 = (int)Math.Floor(sy);
        int x1 = Math.Min(x0 + 1, _width - 1);
        int y1 = Math.Min(y0 + 1, _height - 1);
        x0 = Math.Clamp(x0, 0, _width - 1);
        y0 = Math.Clamp(y0, 0, _height - 1);
        float fx = sx - x0;
        float fy = sy - y0;

        Color a = source[(y0 * _width) + x0];
        Color b = source[(y0 * _width) + x1];
        Color c = source[(y1 * _width) + x0];
        Color d = source[(y1 * _width) + x1];
        Color top = Color.Lerp(a, b, fx);
        Color bottom = Color.Lerp(c, d, fx);
        return Color.Lerp(top, bottom, fy);
    }

    private static bool Load()
    {
        if (_attempted)
        {
            return _pixels is not null;
        }

        _attempted = true;
        string path = Path.Combine(Paths.ConfigPath, PleasurePlugin.PluginGuid, FileName);
        try
        {
            if (!File.Exists(path))
            {
                PleasureRuntime.Log?.LogInfo(
                    $"No crest image at '{path}'; the lust crest is drawn from curves instead. "
                    + "Put the game's crest there as a PNG to have the exact mark projected.");
                return false;
            }

            byte[] data = File.ReadAllBytes(path);
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(loaded, data, false))
            {
                PleasureRuntime.Log?.LogWarning(
                    $"The crest image at '{path}' could not be decoded; the drawn crest is used.");
                return false;
            }

            _width = loaded.width;
            _height = loaded.height;
            var raw = loaded.GetPixels();
            UnityEngine.Object.Destroy(loaded);

            // Copied into a managed array once: the resample reads every pixel four times per
            // rebuild, and interop array indexing is a call, not a load.
            var pixels = new Color[_width * _height];
            var authoredAlpha = false;
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = raw[i];
                if (pixels[i].a < 0.97f)
                {
                    authoredAlpha = true;
                }
            }

            if (!authoredAlpha)
            {
                // White is background, everything else is mark. The colours themselves are left
                // untouched — they are the thing that has to match — so a keyed edge keeps a hint
                // of its white fringe rather than being re-tinted into something the image never
                // held.
                for (var i = 0; i < pixels.Length; i++)
                {
                    Color p = pixels[i];
                    float distance = 1f - Math.Min(p.r, Math.Min(p.g, p.b));
                    pixels[i].a = Math.Clamp((distance - 0.06f) / 0.22f, 0f, 1f);
                }
            }

            // The image is flipped here rather than per-sample: GetPixels rows run bottom-up while
            // the draw maps v = 0 to the top of the mark.
            var flipped = new Color[pixels.Length];
            for (var y = 0; y < _height; y++)
            {
                Array.Copy(pixels, y * _width, flipped, (_height - 1 - y) * _width, _width);
            }

            // The content's bounding box, so margins in the file cost no screen space and the
            // aspect is the mark's own.
            _minX = _width;
            _minY = _height;
            _maxX = 0;
            _maxY = 0;
            for (var y = 0; y < _height; y++)
            {
                for (var x = 0; x < _width; x++)
                {
                    if (flipped[(y * _width) + x].a <= 0.05f)
                    {
                        continue;
                    }

                    _minX = Math.Min(_minX, x);
                    _minY = Math.Min(_minY, y);
                    _maxX = Math.Max(_maxX, x);
                    _maxY = Math.Max(_maxY, y);
                }
            }

            if (_maxX < _minX)
            {
                PleasureRuntime.Log?.LogWarning(
                    $"The crest image at '{path}' is entirely background; the drawn crest is used.");
                return false;
            }

            _pixels = flipped;
            PleasureRuntime.Log?.LogInfo(
                $"The crest image was loaded from '{path}' ({_width}x{_height}, content "
                + $"{_maxX - _minX + 1}x{_maxY - _minY + 1}, "
                + $"{(authoredAlpha ? "authored alpha" : "white background keyed out")}). The HUD "
                + "projects it exactly.");
            return true;
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning(
                $"The crest image could not be read from '{path}': {exception.Message}. The drawn "
                + "crest is used.");
            return false;
        }
    }
}
