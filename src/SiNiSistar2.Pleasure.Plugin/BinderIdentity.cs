using SiNiSistar2.Lc;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;
using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>Who is holding the player, as the catalogue names them (SPEC003 5.3.1).</summary>
internal sealed record BinderIdentity(string Id, string? DisplayName, string Source);

/// <summary>
/// Works out which enemy has hold of the player (SPEC003 FR-280).
///
/// The game has two separate identifiers for an enemy and neither covers all of them.
/// <c>m_GalleryEnemyID</c> is the gallery's key and is left unset on enemies that were never given a
/// gallery entry; <c>m_EnemyID</c> is what the defeat flags and spawners use. Reading only the first
/// meant every enemy the author left unset resolved to <c>None</c> and shared a single row in the
/// catalogue, so the mouthy worm could not be declared sexual without declaring every other
/// unidentified captor sexual at the same time.
///
/// The gallery id is still tried first. That is not because it is the better key but because it is
/// the one already written into players' catalogues: putting anything ahead of it would silently
/// retire every decision made so far (FR-283).
/// </summary>
internal static class BinderIdentityResolver
{
    private static string? _heldId;
    private static string? _heldName;

    /// <summary>
    /// Resolves the captor, or null when the player is not held or the captor names nothing. Null is
    /// the honest answer for "unidentified": 5.3 then skips the per-enemy rules rather than looking
    /// up a row that stands for no enemy in particular.
    ///
    /// Called every frame of a hold, so the display name is looked up once per hold rather than once
    /// per frame: it cannot change while the same enemy keeps hold of the player, and a localisation
    /// lookup across the interop boundary is not something to do sixty times a second (FR-230). The
    /// cache is dropped when the hold ends, which is what makes a language change show up on the
    /// next hold rather than never.
    /// </summary>
    internal static BinderIdentity? Resolve(Lelia lelia)
    {
        (EnemyObject? Enemy, SiNiObject? Any) captor = Captor(lelia);
        if (captor.Enemy is null && captor.Any is null)
        {
            Forget();
            return null;
        }

        EnemyObject? enemy = captor.Enemy;
        (string Id, string Source)? found = enemy is null
            ? null
            : FromEnum(() => enemy.GalleryEnemyID.ToString(), "GalleryEnemyID")
              ?? FromEnum(() => enemy.m_EnemyID.ToString(), "EnemyID");

        Component? named = (Component?)enemy ?? captor.Any;
        found ??= FromObjectName(named) ?? FromTypeName(named);

        if (found is null)
        {
            Forget();
            return null;
        }

        if (!string.Equals(_heldId, found.Value.Id, StringComparison.Ordinal))
        {
            _heldId = found.Value.Id;
            _heldName = enemy is null ? null : DisplayNameOf(enemy);
        }

        return new BinderIdentity(found.Value.Id, _heldName, found.Value.Source);
    }

    /// <summary>
    /// Who has hold of the player, by both of the game's routes.
    ///
    /// <c>Bind.Binder</c> is the authoritative one — its type is <c>IBinder</c>, which
    /// <c>SiNiObject</c> implements — and <c>Bind.BinderEnemy</c> is the subset of those that happen
    /// to be enemies. Reading only the second one made holds by <c>ParasiteTentacle</c>,
    /// <c>ParasiteBullet</c> and <c>StoneEye</c> look like no hold at all, so they could not be
    /// declared sexual or non-sexual by any means.
    /// </summary>
    private static (EnemyObject? Enemy, SiNiObject? Any) Captor(Lelia lelia)
    {
        try
        {
            Bind? bind = lelia.Bind;
            if (bind is null)
            {
                return (null, null);
            }

            SiNiObject? any = bind.Binder?.TryCast<SiNiObject>();
            return (bind.BinderEnemy ?? any?.TryCast<EnemyObject>(), any);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    /// <summary>Drops the cached name so the next hold reads it again.</summary>
    internal static void Forget()
    {
        _heldId = null;
        _heldName = null;
    }

    private static (string Id, string Source)? FromEnum(Func<string> read, string source)
    {
        try
        {
            string name = read();
            return EnemyIds.IsUsable(name) ? (name, source) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (string Id, string Source)? FromObjectName(Component? binder)
    {
        if (binder is null)
        {
            return null;
        }

        try
        {
            string? id = EnemyIds.FromObjectName(binder.name);
            return id is null ? null : (id, "object name");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The binder's class, for when its object name is structural. Hold colliders in this build hang
    /// off objects called <c>Root</c>, and a row keyed on that would stand for whichever binder
    /// happened to sit there — the same collapse the `None` row used to cause.
    /// </summary>
    private static (string Id, string Source)? FromTypeName(Component? binder)
    {
        if (binder is null)
        {
            return null;
        }

        try
        {
            string? id = EnemyIds.FromTypeName(binder.GetIl2CppType()?.Name ?? binder.GetType().Name);
            return id is null ? null : (id, "component type");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// What the game calls this enemy on screen (SPEC003 FR-282), or null.
    ///
    /// Taken from the enemy in front of the player rather than mapped from the identifier by naming
    /// convention. The convention exists — <c>ID_EnmNm_&lt;suffix&gt;</c> — but it resolves 63 of the
    /// 108 <c>EnemyID</c> names, which is to say it fails on precisely the half this change exists to
    /// reach. A missing name is not an error: the row is still selectable by its identifier.
    /// </summary>
    private static string? DisplayNameOf(EnemyObject binder)
    {
        try
        {
            LocalizeID id = binder.DisplayEnemyNameID;
            if (id == LocalizeID.None)
            {
                return null;
            }

            LocalizeManager? localize = ManagerList.Localize;
            if (localize is null)
            {
                return null;
            }

            string? text = localize.GetLcText(id);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            // An unresolved key comes back as the table's own error text, which echoes the key
            // (A-45 hit the same wall with status names). Storing that would put "ID_EnmNm_..." in
            // the list where a name belongs.
            string trimmed = text!.Trim();
            return trimmed.Contains(id.ToString(), StringComparison.Ordinal) ? null : trimmed;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
