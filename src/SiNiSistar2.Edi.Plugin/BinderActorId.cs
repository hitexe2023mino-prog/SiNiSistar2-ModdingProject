using SiNiSistar2.Edi.Core;
using SiNiSistar2.Obj;
using UnityEngine;

namespace SiNiSistar2.Edi.Plugin;

/// <summary>
/// Names the enemy holding the player, for the <c>actorId</c> of a hold trigger (SPEC001 6.2.1).
///
/// Two things were wrong with reading <c>Bind.BinderEnemy.GalleryEnemyID</c> directly.
///
/// The gallery id is unset on enemies that were never given a gallery entry, and it then reads as
/// <c>None</c> — a value that names no enemy. Six unrelated holds in the user's own trigger catalog
/// had collapsed onto <c>hold/None/…</c>, which means a funscript authored for one of them would
/// have played for the others.
///
/// Worse, <c>BinderEnemy</c> is typed <c>EnemyObject</c> while the binding contract is
/// <c>IBinder</c>, which <c>SiNiObject</c> implements. <c>ParasiteTentacle</c>,
/// <c>ParasiteBullet</c> and <c>StoneEye</c> bind the player without being enemy objects, so the
/// property is null for them and the observer — which required it — emitted nothing at all.
/// </summary>
internal static class BinderActorId
{
    internal const string Unidentified = ActorIds.UnidentifiedBinder;

    /// <summary>
    /// The actor id of whoever holds the player, or null when nothing holds them.
    ///
    /// Null means "no binder", not "unnameable binder": callers observing a hold substitute
    /// <see cref="Unidentified"/> so the trigger still exists (FR-059), while the game-over context
    /// uses it to fall back to the player.
    /// </summary>
    internal static string? Resolve(Lelia lelia)
    {
        (EnemyObject? enemy, SiNiObject? any) = Captor(lelia);
        if (enemy is null && any is null)
        {
            return null;
        }

        Component? named = (Component?)enemy ?? any;
        return FromEnum(() => enemy?.GalleryEnemyID.ToString())
            ?? FromEnum(() => enemy?.m_EnemyID.ToString())
            ?? FromObjectName(named)
            ?? FromTypeName(named)
            ?? Unidentified;
    }

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

    private static string? FromEnum(Func<string?> read)
    {
        try
        {
            string? name = read();
            return ActorIds.IsUsable(name) ? name : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? FromObjectName(Component? binder)
    {
        if (binder is null)
        {
            return null;
        }

        try
        {
            return ActorIds.FromObjectName(binder.name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The binder's class, for when its object name is structural. Hold colliders in this build hang
    /// off objects called <c>Root</c>; keying a trigger on that would hand unrelated binders the
    /// same funscript, which is exactly what 6.2.1 exists to prevent.
    /// </summary>
    private static string? FromTypeName(Component? binder)
    {
        if (binder is null)
        {
            return null;
        }

        try
        {
            return ActorIds.FromTypeName(binder.GetIl2CppType()?.Name ?? binder.GetType().Name);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
