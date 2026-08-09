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
    private const float ButtonHeight = 24f;
    private const float PanelWidth = 560f;

    private static readonly Color Backdrop = new(0f, 0f, 0f, 0.72f);
    private static readonly Color Foreground = new(0.92f, 0.95f, 1f, 1f);
    private static readonly Color Accent = new(1f, 0.82f, 0.35f, 1f);

    private GUIStyle? _textStyle;
    private GUIStyle? _boxStyle;
    private Texture2D? _solid;
    private bool _faultLogged;
    private bool _cursorSaved;
    private bool _previousCursorVisible;
    private CursorLockMode _previousCursorLock;

    internal HudMode Mode { get; set; } = HudMode.Off;

    internal bool DebugPanelOpen { get; private set; }

    /// <summary>
    /// Set when a command key was pressed while commands are off, so the panel can say why
    /// nothing happened. Cleared when the panel is closed.
    /// </summary>
    internal bool PressedWhileDisabled { get; private set; }

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
            ApplyCursor(DebugPanelOpen);

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

    /// <summary>
    /// Buttons need a pointer. This is the one piece of game state the HUD writes, it is confined
    /// to the debug panel being open, and the previous values are put back when it closes
    /// (SPEC004 5.9, the stated exception to FR-327).
    /// </summary>
    private void ApplyCursor(bool panelOpen)
    {
        if (panelOpen)
        {
            if (!_cursorSaved)
            {
                _previousCursorVisible = Cursor.visible;
                _previousCursorLock = Cursor.lockState;
                _cursorSaved = true;
            }

            if (!Cursor.visible)
            {
                Cursor.visible = true;
            }

            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
            }

            return;
        }

        if (!_cursorSaved)
        {
            return;
        }

        Cursor.visible = _previousCursorVisible;
        Cursor.lockState = _previousCursorLock;
        _cursorSaved = false;
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
            PressedWhileDisabled = false;
            current.Use();
            return;
        }

        if (!DebugPanelOpen)
        {
            return;
        }

        // Enable only. It never disables, so pressing it again when a command looks unresponsive
        // cannot turn the tool off (HudModel.ToggleKey).
        if (IsDigitKey(current.keyCode, HudModel.ToggleKey))
        {
            PendingCommand = HudModel.ToggleKey;
            PressedWhileDisabled = false;
            current.Use();
            return;
        }

        foreach ((char key, _) in HudModel.Commands)
        {
            if (!IsDigitKey(current.keyCode, key))
            {
                continue;
            }

            if (commandsEnabled)
            {
                PendingCommand = key;
            }
            else
            {
                NoteDisabledPress(key);
            }

            current.Use();
            return;
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

    /// <summary>
    /// The panel, drawn as real buttons rather than a printed key list. Reading a list and
    /// remembering which number to press is work the panel can do for the user, and it removes
    /// the whole class of "I pressed a key and cannot tell whether it registered".
    /// </summary>
    private void DrawDebugPanel()
    {
        IReadOnlyList<string> header = HudModel.DebugHeader(Snapshot, PressedWhileDisabled);
        bool enabled = Snapshot.DebugCommandsEnabled;

        float top = Margin + ((Mode == HudMode.Off ? 0 : LineCount()) * LineHeight) + (Margin * 2f);
        float left = Math.Max(Margin, Screen.width - PanelWidth - Margin);
        float height = (header.Count * LineHeight) + ((HudModel.Commands.Count + 1) * ButtonHeight)
            + (Margin * 3f);
        var panel = new Rect(left, top, PanelWidth, height);

        Color previous = GUI.color;
        GUI.color = Backdrop;
        GUI.Box(panel, GUIContent.none, BoxStyle);

        GUI.color = Accent;
        float y = panel.y + Margin;
        foreach (string line in header)
        {
            GUI.Label(new Rect(panel.x + Margin, y, panel.width - (Margin * 2f), LineHeight), line, TextStyle);
            y += LineHeight;
        }

        // Buttons use the built-in skin so they look and behave like buttons; the tint is reset
        // first because a coloured GUI.color would wash them out.
        GUI.color = Color.white;
        y += Margin;

        var switchArea = new Rect(panel.x + Margin, y, panel.width - (Margin * 2f), ButtonHeight - 2f);
        if (GUI.Button(switchArea, HudModel.ToggleLabel(enabled)))
        {
            PendingCommand = enabled ? HudModel.DisableCommand : HudModel.ToggleKey;
            PressedWhileDisabled = false;
        }

        y += ButtonHeight;

        foreach ((char key, string label) in HudModel.Commands)
        {
            var area = new Rect(panel.x + Margin, y, panel.width - (Margin * 2f), ButtonHeight - 2f);
            bool clicked = GUI.Button(area, HudModel.CommandLabel(key, label));
            y += ButtonHeight;

            if (!clicked)
            {
                continue;
            }

            if (enabled)
            {
                PendingCommand = key;
            }
            else
            {
                // Same acknowledgement a keypress gets, so a click is never silently dropped.
                NoteDisabledPress(key);
            }
        }

        GUI.color = previous;
    }

    private int LineCount() => Mode == HudMode.Compact ? 1 : HudModel.Full(Snapshot).Count;

    private void DrawLines(float top, IReadOnlyList<string> lines, Color colour)
    {
        // Top-right: the player portrait and status sit in the top-left corner, and an overlay on
        // top of them hides the very thing being played.
        float left = Math.Max(Margin, Screen.width - PanelWidth - Margin);
        var area = new Rect(left, top, PanelWidth, (lines.Count * LineHeight) + (Margin * 2f));

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

    /// <summary>
    /// FR-331 keeps the panel inert while commands are off, but silence is what made this look
    /// broken. The attempt is acknowledged once per panel session and points at the switch.
    /// </summary>
    private void NoteDisabledPress(char key)
    {
        if (PressedWhileDisabled)
        {
            return;
        }

        PressedWhileDisabled = true;
        SpawnRuntime.Log?.LogWarning(
            $"[debug] '{key}' did nothing: debug commands are off. Click the COMMANDS switch at "
            + $"the top of the panel, or press {HudModel.ToggleKey}, to turn them on.");
    }

    /// <summary>
    /// Accepts the number row and the numpad. Only the number row was handled at first, which
    /// makes the panel look dead to anyone reaching for the keypad.
    /// </summary>
    private static bool IsDigitKey(KeyCode pressed, char digit)
    {
        var offset = digit - '0';
        return pressed == KeyCode.Alpha0 + offset || pressed == KeyCode.Keypad0 + offset;
    }
}
