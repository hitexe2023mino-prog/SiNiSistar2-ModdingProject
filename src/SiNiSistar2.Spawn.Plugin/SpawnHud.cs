using SiNiSistar2.Spawn.Core;
using UnityEngine;

namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// The read-only overlay (SPEC004 5.8) and the debug panel's key handling (5.9).
///
/// Immediate-mode GUI, for the same reason SPEC003's overlay uses it: it needs no prefab, no
/// asset and no scene object, and it cannot disturb the game's own UI hierarchy or the hold UI
/// that SPEC002 tints (FR-328, DEC-315). Hotkeys are read from <c>Event.current</c> rather than
/// the game's input system, so nothing competes with the game's own bindings.
///
/// <c>GUI.DrawTexture</c> is stripped from this build even though it appears in interop metadata,
/// which is why every fill here goes through <c>GUI.Box</c> and text through <c>GUI.Label</c>.
/// </summary>
internal sealed class SpawnHud
{
    private const float Margin = 8f;
    private const float LineHeight = 18f;
    private const float PanelWidth = 560f;

    private static readonly Color Backdrop = new(0f, 0f, 0f, 0.72f);
    private static readonly Color Foreground = new(0.92f, 0.95f, 1f, 1f);
    private static readonly Color Accent = new(1f, 0.82f, 0.35f, 1f);

    private GUIStyle? _textStyle;
    private GUIStyle? _boxStyle;
    private Texture2D? _solid;
    private bool _faultLogged;

    internal HudMode Mode { get; set; } = HudMode.Off;

    internal bool DebugPanelOpen { get; private set; }

    /// <summary>Set by the observer each frame; the HUD never reads the game itself (FR-327).</summary>
    internal HudSnapshot Snapshot { get; set; } = new();

    /// <summary>The command a keypress selected this frame, consumed by the observer.</summary>
    internal char PendingCommand { get; private set; }

    internal char TakePendingCommand()
    {
        char command = PendingCommand;
        PendingCommand = '\0';
        return command;
    }

    /// <summary>
    /// Draws and handles input. Any drawing failure disables the HUD and reports once, leaving
    /// every spawn mechanism running (FR-329).
    /// </summary>
    internal void OnGUI(KeyCode hudKey, KeyCode debugKey, bool commandsEnabled)
    {
        try
        {
            HandleKeys(hudKey, debugKey, commandsEnabled);

            if (Mode != HudMode.Off)
            {
                DrawStatus();
            }

            if (DebugPanelOpen)
            {
                DrawDebugPanel();
            }

            _faultLogged = false;
        }
        catch (Exception exception)
        {
            Mode = HudMode.Off;
            DebugPanelOpen = false;
            if (!_faultLogged)
            {
                _faultLogged = true;
                SpawnRuntime.Log?.LogError(
                    $"The spawn HUD could not be drawn and was turned off; the MOD keeps running: {exception}");
            }
        }
    }

    private void HandleKeys(KeyCode hudKey, KeyCode debugKey, bool commandsEnabled)
    {
        // Fully qualified: the game has its own SiNiSistar2.Event namespace, which wins here.
        UnityEngine.Event current = UnityEngine.Event.current;
        if (current is null || current.type != EventType.KeyDown)
        {
            return;
        }

        if (current.keyCode == hudKey)
        {
            Mode = HudModel.Next(Mode);
            current.Use();
            return;
        }

        if (current.keyCode == debugKey)
        {
            DebugPanelOpen = !DebugPanelOpen;
            current.Use();
            return;
        }

        if (!DebugPanelOpen || !commandsEnabled)
        {
            // FR-331: with commands off the panel is readable but inert.
            return;
        }

        foreach ((char key, _) in HudModel.Commands)
        {
            if (current.keyCode == DigitKey(key))
            {
                PendingCommand = key;
                current.Use();
                return;
            }
        }
    }

    private void DrawStatus()
    {
        if (Mode == HudMode.Compact)
        {
            DrawLines(Margin, new[] { HudModel.Compact(Snapshot) }, Foreground);
            return;
        }

        DrawLines(Margin, HudModel.Full(Snapshot), Foreground);
    }

    private void DrawDebugPanel()
    {
        IReadOnlyList<string> lines = HudModel.DebugPanel(Snapshot);
        float top = Margin + ((Mode == HudMode.Off ? 0 : LineCount()) * LineHeight) + (Margin * 2f);
        DrawLines(top, lines, Accent);
    }

    private int LineCount() => Mode == HudMode.Compact ? 1 : HudModel.Full(Snapshot).Count;

    private void DrawLines(float top, IReadOnlyList<string> lines, Color colour)
    {
        var area = new Rect(Margin, top, PanelWidth, (lines.Count * LineHeight) + (Margin * 2f));

        Color previous = GUI.color;
        GUI.color = Backdrop;
        GUI.Box(area, GUIContent.none, BoxStyle);
        GUI.color = colour;

        for (var index = 0; index < lines.Count; index++)
        {
            GUI.Label(
                new Rect(area.x + Margin, area.y + Margin + (index * LineHeight), area.width - (Margin * 2f), LineHeight),
                lines[index],
                TextStyle);
        }

        GUI.color = previous;
    }

    private GUIStyle TextStyle => _textStyle ??= new GUIStyle
    {
        fontSize = 13,
        alignment = TextAnchor.MiddleLeft,
        normal = { textColor = Color.white },
        richText = false,
    };

    private GUIStyle BoxStyle
    {
        get
        {
            if (_boxStyle is not null)
            {
                return _boxStyle;
            }

            _solid = new Texture2D(1, 1);
            _solid.SetPixel(0, 0, Color.white);
            _solid.Apply();

            _boxStyle = new GUIStyle { normal = { background = _solid } };
            return _boxStyle;
        }
    }

    private static KeyCode DigitKey(char digit) => KeyCode.Alpha0 + (digit - '0');
}
