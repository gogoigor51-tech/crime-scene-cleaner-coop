using System;
using CoopCore;
using UnityEngine;

namespace CscCoop;

// flashlight suite (tools/run-pair.ps1 -Test flashlight -Mission 11): the client switches its personal flashlight on
// the way the key does (FlashlightController.ToggleFlashlight), looks around, and off again. The host must see a light
// on the client's figure while it is on, pointing where the client looks, and none once it is off (owner's test:
// nobody saw the others' flashlight).
public partial class Bot
{
    void FlashLog(Session ses, GameWorld w, string label)
    {
        if (ses.IsHost)
        {
            var g = CoopRunner.Instance.Ghost;
            var l = g?.Flashlight;
            bool on = l != null && l.isActiveAndEnabled;
            var f = l != null ? l.transform.forward : Vector3.zero;
            Plugin.L.LogInfo(FormattableString.Invariant($"FLASH host {label}: figure {(g != null)} light on {on} dir {f.x:F2}/{f.y:F2}/{f.z:F2} type {(l != null ? l.type.ToString() : "-")} range {(l != null ? l.range : 0):F0} cookie {(l != null && l.cookie != null)}"));
        }
        else
        {
            var fl = w.Flashlight;
            var cam = w.Player.mainCamera != null ? w.Player.mainCamera.transform : w.Player.transform;
            var f = cam.forward;
            Plugin.L.LogInfo(FormattableString.Invariant($"FLASH client {label}: flashlight {(fl != null)} on {(fl != null && fl.IsOn)} dir {f.x:F2}/{f.y:F2}/{f.z:F2}"));
        }
    }

    void FlashlightTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        switch (step)
        {
            case 0 when t > 3:
                FlashLog(ses, w, "before");
                step = 1; break;
            case 1 when t > 5:
                if (!host && w.Flashlight != null) { w.Flashlight.ToggleFlashlight(); Plugin.L.LogInfo($"FLASH client switched on: {w.Flashlight.IsOn}"); }
                step = 2; break;
            case 2 when t > 9:
                FlashLog(ses, w, "on");
                step = 3; break;
            case 3 when t > 10:
                if (!host)   // look down, the way the game turns the view (a set rotation is overwritten; LookAt sets only the pitch here)
                {
                    var pt = w.Player.transform;
                    var target = new GameObject("FlashTarget").transform;
                    target.position = pt.position + pt.right * 4 + Vector3.up * 0.2f;
                    w.Player.LookAt(target);
                    UnityEngine.Object.Destroy(target.gameObject, 2);
                }
                step = 4; break;
            case 4 when t > 14:
                FlashLog(ses, w, "turned");
                step = 5; break;
            case 5 when t > 15:
                if (!host && w.Flashlight != null && w.Flashlight.IsOn) { w.Flashlight.ToggleFlashlight(); Plugin.L.LogInfo($"FLASH client switched off: on {w.Flashlight.IsOn}"); }
                step = 6; break;
            case 6 when t > 19:
                FlashLog(ses, w, "off");
                Plugin.L.LogInfo("FLASH dump done");
                step = 7; break;
        }
    }
}
