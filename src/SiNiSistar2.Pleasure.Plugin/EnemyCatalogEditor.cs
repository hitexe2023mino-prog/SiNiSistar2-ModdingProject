using SiNiSistar2.Pleasure.Core;
using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// The in-game screen for saying which enemies make sexual attacks (SPEC003 FR-236〜238).
///
/// It exists because the decision cannot be made anywhere else honestly. Whether an enemy's hold is
/// sexual is a judgement about what is on screen, and the only place that is visible is in front of
/// the enemy — not in a text file being edited between sessions from memory. The list therefore
/// opens over the running game, and a change applies to the very next hit.
///
/// Everything is drawn with the two calls this build supports and hit-tested by hand.
/// <c>GUI.Button</c>, <c>GUI.BeginScrollView</c> and <c>GUI.TextField</c> are all in the interop
/// metadata, but so was <c>GUI.DrawTexture</c>, which throws at runtime here. Nothing in this file
/// depends on a call that has not already been proven on this build.
/// </summary>
internal sealed class EnemyCatalogEditor
{
    private const float RowHeight = 22f;
    private const float HeaderHeight = 74f;
    private const float FooterHeight = 46f;

    private static readonly Color Panel = new(0.05f, 0.03f, 0.06f, 0.92f);
    private static readonly Color Edge = new(1f, 0.82f, 0.42f, 0.75f);
    private static readonly Color SelectedRow = new(1f, 0.45f, 0.70f, 0.22f);
    private static readonly Color SeenRow = new(1f, 1f, 1f, 0.05f);
    private static readonly Color Faint = new(0.72f, 0.70f, 0.74f, 1f);
    private static readonly Color Bright = new(1f, 0.96f, 0.92f, 1f);

    private EnemyAttackDocument? _snapshot;
    private IReadOnlyList<EnemyAttackRow> _rows = Array.Empty<EnemyAttackRow>();
    private bool _rowsStale = true;
    private bool _metOnly = true;
    private int _selected;
    private int _scroll;
    private int _visibleRows = 1;

    internal bool IsOpen { get; private set; }

    internal void Toggle()
    {
        if (IsOpen)
        {
            Commit();
            return;
        }

        // The snapshot is what Escape restores. Taken as a document rather than a reference because
        // the catalogue is edited in place: the classifier is holding it.
        _snapshot = PleasureRuntime.Enemies.ToDocument();
        _rowsStale = true;
        IsOpen = true;
        SelectCurrentCaptor();
        PleasureRuntime.Log?.LogInfo(
            "Enemy classification opened. Up/Down or the wheel moves, Space cycles "
            + "Auto/Sexual/NonSexual, Tab shows all enemies or only those that have held you, Enter "
            + "saves, Escape cancels.");
    }

    /// <summary>
    /// Consumes the events the editor understands. Returns true when the event was the editor's, so
    /// the caller knows the game's own UI should not also act on it.
    /// </summary>
    internal bool HandleEvent(UnityEngine.Event current)
    {
        if (!IsOpen)
        {
            return false;
        }

        switch (current.type)
        {
            case EventType.KeyDown when current.keyCode is KeyCode.Return or KeyCode.KeypadEnter:
                Commit();
                return true;

            case EventType.KeyDown when current.keyCode == KeyCode.Escape:
                Cancel();
                return true;

            case EventType.KeyDown when current.keyCode == KeyCode.Tab:
                _metOnly = !_metOnly;
                _rowsStale = true;
                _selected = 0;
                _scroll = 0;
                return true;

            case EventType.KeyDown when current.keyCode == KeyCode.DownArrow:
                Move(1);
                return true;

            case EventType.KeyDown when current.keyCode == KeyCode.UpArrow:
                Move(-1);
                return true;

            case EventType.KeyDown when current.keyCode == KeyCode.PageDown:
                Move(_visibleRows);
                return true;

            case EventType.KeyDown when current.keyCode == KeyCode.PageUp:
                Move(-_visibleRows);
                return true;

            case EventType.KeyDown when current.keyCode is KeyCode.Space or KeyCode.RightArrow:
                Cycle(forward: true);
                return true;

            case EventType.KeyDown when current.keyCode == KeyCode.LeftArrow:
                Cycle(forward: false);
                return true;

            case EventType.ScrollWheel:
                Scroll((int)Math.Round(current.delta.y));
                return true;

            case EventType.MouseDown:
                return ClickAt(current.mousePosition);

            // Swallowed so a drag inside the list does not also move the layout editor or leave a
            // half-finished gesture behind.
            case EventType.MouseDrag:
            case EventType.MouseUp:
                return true;
        }

        return false;
    }

    internal void Draw()
    {
        if (!IsOpen)
        {
            return;
        }

        Rect panel = PanelArea();
        OverlayPainter.Fill(panel, Panel);
        Outline(panel);

        IReadOnlyList<EnemyAttackRow> rows = Rows();
        OverlayPainter.Text(
            new Rect(panel.x + 16f, panel.y + 10f, panel.width - 32f, 22f),
            $"Enemy attacks — {(_metOnly ? "enemies that have held you" : "all enemies")} ({rows.Count})",
            Bright);
        OverlayPainter.Text(
            new Rect(panel.x + 16f, panel.y + 32f, panel.width - 32f, 22f),
            "Auto decides from what the attack inflicts.  Sexual always raises pleasure.  "
            + "NonSexual never does.",
            Faint);

        string captor = PleasureRuntime.BinderEnemyId is { } id
            ? PleasureRuntime.BinderDisplayName is { } named ? $"{named}  ({id})" : id
            : "(not held)";
        OverlayPainter.Text(
            new Rect(panel.x + 16f, panel.y + 50f, panel.width - 32f, 22f),
            $"Holding you now: {captor}",
            Faint);

        for (var index = 0; index < _visibleRows && index + _scroll < rows.Count; index++)
        {
            DrawRow(panel, rows[index + _scroll], index, index + _scroll == _selected);
        }

        OverlayPainter.Text(
            new Rect(panel.x + 16f, panel.yMax - 38f, panel.width - 32f, 22f),
            "Up/Down or wheel: move    Space or click: cycle    Tab: held-you only / all",
            Faint);
        OverlayPainter.Text(
            new Rect(panel.x + 16f, panel.yMax - 20f, panel.width - 32f, 22f),
            "Enter: save    Escape: cancel    F10: close",
            Faint);
    }

    private void DrawRow(Rect panel, EnemyAttackRow row, int slot, bool selected)
    {
        var area = new Rect(
            panel.x + 8f,
            panel.y + HeaderHeight + (slot * RowHeight),
            panel.width - 16f,
            RowHeight - 2f);

        if (selected)
        {
            OverlayPainter.Fill(area, SelectedRow);
        }
        else if (row.Seen)
        {
            OverlayPainter.Fill(area, SeenRow);
        }

        // A colour chip as well as the word, so the shape of the list can be read at a glance
        // instead of one row at a time.
        OverlayPainter.Fill(new Rect(area.x + 6f, area.y + 5f, 10f, 10f), ChipColour(row.Setting));

        bool isCaptor = string.Equals(row.Id, PleasureRuntime.BinderEnemyId, StringComparison.Ordinal);
        OverlayPainter.Text(
            new Rect(area.x + 24f, area.y + 1f, area.width - 340f, RowHeight),
            isCaptor ? $"► {row.Id}" : row.Id,
            selected || isCaptor ? Bright : Faint);

        // The name the game itself uses, for the rows that have earned one by holding the player.
        // The identifier stays: it is what the file is keyed on, and a list showing only names would
        // hide the fact that two rows can describe the same creature (SPEC003 DEC-261).
        if (row.DisplayName is { } displayName)
        {
            OverlayPainter.Text(
                new Rect(area.xMax - 320f, area.y + 1f, 160f, RowHeight),
                displayName,
                selected || isCaptor ? Bright : Faint);
        }

        OverlayPainter.Text(
            new Rect(area.xMax - 160f, area.y + 1f, 150f, RowHeight),
            SettingLabel(row.Setting),
            selected ? Bright : Faint);
    }

    private static Color ChipColour(EnemyAttackSetting setting) => setting switch
    {
        EnemyAttackSetting.Sexual => new Color(1f, 0.42f, 0.70f, 0.95f),
        EnemyAttackSetting.NonSexual => new Color(0.45f, 0.62f, 0.95f, 0.95f),
        _ => new Color(0.55f, 0.55f, 0.58f, 0.75f),
    };

    private static string SettingLabel(EnemyAttackSetting setting) => setting switch
    {
        EnemyAttackSetting.Sexual => "Sexual",
        EnemyAttackSetting.NonSexual => "NonSexual",
        _ => "Auto",
    };

    private void Outline(Rect area)
    {
        const float edge = 2f;
        OverlayPainter.Fill(new Rect(area.x, area.y, area.width, edge), Edge);
        OverlayPainter.Fill(new Rect(area.x, area.yMax - edge, area.width, edge), Edge);
        OverlayPainter.Fill(new Rect(area.x, area.y, edge, area.height), Edge);
        OverlayPainter.Fill(new Rect(area.xMax - edge, area.y, edge, area.height), Edge);
    }

    private Rect PanelArea()
    {
        // Wider than it was: a row now carries an identifier, a name and a setting side by side.
        float width = Math.Min(760f, Screen.width * 0.86f);
        float height = Math.Min(Screen.height * 0.82f, 720f);
        var area = new Rect(
            (Screen.width - width) / 2f,
            (Screen.height - height) / 2f,
            width,
            height);

        _visibleRows = Math.Max(1, (int)((area.height - HeaderHeight - FooterHeight) / RowHeight));
        return area;
    }

    /// <summary>
    /// The rows, rebuilt only when the list itself could have changed. Sorting the whole catalogue
    /// — both enumerations, so over two hundred rows — every frame of an open menu is waste, and the order must not shuffle under the cursor when a row's
    /// setting changes.
    /// </summary>
    private IReadOnlyList<EnemyAttackRow> Rows()
    {
        if (_rowsStale)
        {
            IReadOnlyList<EnemyAttackRow> all = PleasureRuntime.Enemies.Rows();
            _rows = _metOnly && all.Any(row => row.Seen)
                ? all.Where(row => row.Seen).ToArray()
                : all;
            _rowsStale = false;
            Clamp();
        }

        return _rows;
    }

    /// <summary>
    /// Opens on the enemy currently holding the player. That is nearly always the one the screen was
    /// opened to settle, and hunting for it in a list of over two hundred is the tedious part.
    /// </summary>
    private void SelectCurrentCaptor()
    {
        string? captor = PleasureRuntime.BinderEnemyId;
        if (string.IsNullOrEmpty(captor))
        {
            return;
        }

        // Establishes how many rows fit before centring on one; without it the first open centres
        // against a row count of one and lands at the top of the list.
        PanelArea();
        IReadOnlyList<EnemyAttackRow> rows = Rows();
        for (var index = 0; index < rows.Count; index++)
        {
            if (string.Equals(rows[index].Id, captor, StringComparison.Ordinal))
            {
                _selected = index;
                _scroll = Math.Max(0, index - (_visibleRows / 2));
                Clamp();
                return;
            }
        }
    }

    private void Move(int delta)
    {
        _selected += delta;
        Clamp();
    }

    private void Scroll(int notches)
    {
        _scroll += notches;
        Clamp();
    }

    private void Clamp()
    {
        int count = _rows.Count;
        if (count == 0)
        {
            _selected = 0;
            _scroll = 0;
            return;
        }

        _selected = Math.Clamp(_selected, 0, count - 1);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, count - _visibleRows));

        // Keeps the selection on screen when it was moved by the keyboard rather than the wheel.
        if (_selected < _scroll)
        {
            _scroll = _selected;
        }
        else if (_selected >= _scroll + _visibleRows)
        {
            _scroll = _selected - _visibleRows + 1;
        }
    }

    private void Cycle(bool forward)
    {
        IReadOnlyList<EnemyAttackRow> rows = Rows();
        if (rows.Count == 0)
        {
            return;
        }

        EnemyAttackRow row = rows[Math.Clamp(_selected, 0, rows.Count - 1)];
        EnemyAttackSetting next;
        if (forward)
        {
            next = PleasureRuntime.Enemies.Cycle(row.Id);
        }
        else
        {
            next = Previous(row.Setting);
            PleasureRuntime.Enemies.Set(row.Id, next);
        }

        // Only the setting changed, so the order is unaffected and the rebuilt row is patched in
        // place. Re-sorting here would move the row out from under the cursor mid-edit.
        _rows = _rows.Select(existing =>
            string.Equals(existing.Id, row.Id, StringComparison.Ordinal)
                ? existing with { Setting = next }
                : existing).ToArray();
    }

    private static EnemyAttackSetting Previous(EnemyAttackSetting setting) => setting switch
    {
        EnemyAttackSetting.Auto => EnemyAttackSetting.NonSexual,
        EnemyAttackSetting.NonSexual => EnemyAttackSetting.Sexual,
        _ => EnemyAttackSetting.Auto,
    };

    private bool ClickAt(Vector2 position)
    {
        Rect panel = PanelArea();
        if (!panel.Contains(position))
        {
            return false;
        }

        var slot = (int)((position.y - panel.y - HeaderHeight) / RowHeight);
        if (slot < 0 || slot >= _visibleRows)
        {
            // Inside the panel but on the header or footer: still the editor's event, so the click
            // does not fall through to whatever is behind the menu.
            return true;
        }

        int index = slot + _scroll;
        if (index >= Rows().Count)
        {
            return true;
        }

        _selected = index;
        Cycle(forward: true);
        return true;
    }

    private void Commit()
    {
        IsOpen = false;
        _snapshot = null;
        PleasureRuntime.SaveEnemies("edited in game");
    }

    private void Cancel()
    {
        if (_snapshot is not null)
        {
            PleasureRuntime.Enemies.RestoreFrom(_snapshot);
        }

        IsOpen = false;
        _snapshot = null;
        _rowsStale = true;
        PleasureRuntime.Log?.LogInfo("Enemy classification cancelled; the previous settings are restored.");
    }
}
