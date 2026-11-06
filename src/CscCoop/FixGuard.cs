using HarmonyLib;
using UnityEngine;

namespace CscCoop;

// A piece of furniture another player has already stood up must not be put back down here. When their OnFix (a
// replayed action) arrives while this player's FurnitureFixController is still lifting the same piece, the piece
// leaves the fixable layer, the highlight changes, the game breaks this player's fix (TryBreakFixing) and
// RevertTween moves it back to where this lift started: it stood on the other screen and lay here (owner's test on
// Developers' Hell, both players lifting at once). So no revert for a piece that is fixed (IsCorrect): it goes to
// its restore pose instead.
[HarmonyPatch]
static class FixGuard
{
    public static int Kept;

    [HarmonyPatch(typeof(FurnitureFixController), nameof(FurnitureFixController.RevertTween))] [HarmonyPrefix]
    static bool RevertTween(FurnitureFixController __instance, Transform transform)
    {
        var fc = __instance.TryCast<FurnitureFixController>();
        if (fc == null || transform == null || Env.Get("CSCCOOP_MUTANT") == "fix-revert") return true;
        var fo = transform.GetComponent<FixableObject>();
        if (fo == null || !fo.IsCorrect) return true;
        if (fc.positionTween != null) DG.Tweening.TweenExtensions.Kill(fc.positionTween, false);
        if (fc.rotationTween != null) DG.Tweening.TweenExtensions.Kill(fc.rotationTween, false);
        transform.localPosition = fo.restorePosition; transform.localRotation = fo.restoreRotation;
        Kept++;
        Plugin.L.LogInfo($"COOP furniture '{fo.name}' was fixed by another player during this player's lift: kept standing");
        return false;
    }
}
