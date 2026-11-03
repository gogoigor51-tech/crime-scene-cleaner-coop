using System;
using System.Linq;
using CoopCore;
using UnityEngine;

namespace CscCoop;

// fixlift suite (tools/run-pair.ps1 -Test fixlift -Mission 11): both players stand the same knocked-over piece of
// furniture up at the same moment (FixableObject). The host's lift ends first; its OnFix reaches the client while the
// client's own lift is still running in the game's FurnitureFixController (selectedObject, isFixing). The fixed piece
// leaves the fixable layer, the client's highlight changes and the game breaks the client's fix
// (FurnitureFixController.TryBreakFixing -> RevertTween back to the start pose): owner's test, it ended visually not
// lifted on one player. The suite drives that controller state directly (a bot can't aim the game's highlighter
// reliably), then calls TryBreakFixing as the highlight change does. Both must end with it standing in place.
public partial class Bot
{
    FixableObject flX; Vector3 flFrom; Quaternion flFromRot; float flStart = -1, flTime;

    void FixLiftTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        var fc = UnityEngine.Object.FindObjectOfType<FurnitureFixController>();
        switch (step)
        {
            case 0 when t > 3:
            {
                var list = UnityEngine.Object.FindObjectsOfType<FixableObject>()
                    .Where(fo => fo != null && fo.GetIl2CppType().Name == "FixableObject" && fo.isFixable && !fo.IsCorrect && fo.IsClean)
                    .Select(fo => (GameWorld.IndexedPathOf(fo.transform), fo)).OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
                flX = list.Count > 0 ? list[0].fo : null;
                if (flX != null) { var tr = flX.transform; flFrom = tr.localPosition; flFromRot = tr.localRotation; }
                Plugin.L.LogInfo($"FIXLIFT piece {(flX != null ? list[0].Item1 : "NONE")} of {list.Count}, fix time {(flX != null ? flX.fixTime : 0):F2}, controller {(fc != null)}, {FixState(w)}");
                step = 1; break;
            }
            case 1 when t > 6:   // both start in the same lockstep step; the client's lift takes 3 times as long
                if (flX == null || fc == null) { step = 3; break; }
                flStart = t; flTime = Mathf.Max(0.5f, flX.fixTime) * (host ? 1 : 3);
                fc.selectedObject = flX; fc.objectStartPosition = flX.transform.position; fc.objectStartRotation = flX.transform.rotation; fc.isFixing = true;
                step = 2; break;
            case 2:
            {
                if (!host && flX.IsCorrect)
                {
                    Plugin.L.LogInfo($"FIXLIFT client: the host's fix arrived mid-lift, the highlight changes: TryBreakFixing; {FixState(w)}");
                    fc.TryBreakFixing();
                    step = 3; break;
                }
                float k = Mathf.Clamp01((t - flStart) / flTime);
                flX.transform.localPosition = Vector3.Lerp(flFrom, flX.restorePosition, k);
                flX.transform.localRotation = Quaternion.Slerp(flFromRot, flX.restoreRotation, k);
                if (k >= 1)
                {
                    fc.isFixing = false;
                    flX.OnFix();
                    Plugin.L.LogInfo($"FIXLIFT {(host ? "host" : "client")} finished the lift, {FixState(w)}");
                    step = 3;
                }
                break;
            }
            case 3 when t > 18:
                Plugin.L.LogInfo($"FIXLIFT final {FixState(w)}");
                Plugin.L.LogInfo("FIXLIFT dump done");
                step = 4; break;
        }
    }

    string FixState(GameWorld w)
    {
        if (flX == null) return "none";
        var tr = flX.transform;
        return FormattableString.Invariant($"correct {flX.IsCorrect} pos {P3(tr.position)} off {Vector3.Distance(tr.localPosition, flX.restorePosition):F3} angle {Quaternion.Angle(tr.localRotation, flX.restoreRotation):F1}");
    }
}
