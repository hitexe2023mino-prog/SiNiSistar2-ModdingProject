namespace SiNiSistar2.Pleasure.Core;

/// <summary>What to do with the accumulated values when the run points at a different slot.</summary>
public enum SlotAction
{
    /// <summary>The slot has a sidecar; its values are the truth.</summary>
    Restore,

    /// <summary>The slot is where the run in progress now lives; keep what is in hand.</summary>
    Carry,

    /// <summary>The slot holds nothing this MOD wrote; start from zero.</summary>
    Reset,
}

/// <summary>
/// The one rule for what happens to corruption, climaxes, swelling and milk when the slot changes
/// (SPEC003 5.9, FR-284).
///
/// It lives here, as a function of three facts, because getting it wrong is the single mistake this
/// MOD has made most often and the plugin is the one place it cannot be tested. Three cases have
/// each been shipped wrong at least once:
///
/// * Every playthrough shared one key, so a second new game inherited the first one's corruption
///   (付録A A-44). Fixed by treating "no save loaded" as not a slot.
/// * An unknown key cleared the values, so saving a fresh run into a new file wiped it (付録A A-44).
///   Fixed by carrying into an unknown key — which then caused the third.
/// * Carrying into any unknown key meant loading somebody else's save, or dying and coming back,
///   inherited the run in progress (付録A A-55).
///
/// The distinction the third case needs is not "is this key known" but "how did we get here". A
/// save just written by the player is the only way an unknown key can legitimately receive the run
/// in hand. A defeat, or a load, is the save speaking, and the save is authoritative even when it
/// has nothing to say.
/// </summary>
public static class SlotTransition
{
    /// <summary>
    /// Decides what the slot change means.
    /// </summary>
    /// <param name="hasSidecar">Whether this MOD has written values for the slot before.</param>
    /// <param name="authoritative">
    /// Whether the slot is speaking rather than being written to — a load, or a return from a
    /// defeat. An authoritative change never carries: the values belong to the save, and a save
    /// with nothing recorded means nothing was accumulated, not "keep what you had".
    /// </param>
    /// <param name="justSaved">
    /// Whether the player wrote a save a moment ago. This is what separates "the run was saved into
    /// a new file" from "a different save was loaded"; both change the key to one with no sidecar,
    /// and nothing else about them looks different from here.
    /// </param>
    public static SlotAction Decide(bool hasSidecar, bool authoritative, bool justSaved)
    {
        if (hasSidecar)
        {
            return SlotAction.Restore;
        }

        if (authoritative)
        {
            return SlotAction.Reset;
        }

        return justSaved ? SlotAction.Carry : SlotAction.Reset;
    }
}
