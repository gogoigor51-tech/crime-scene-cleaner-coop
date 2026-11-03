using System;
using CoopCore;
using UnityEngine;

namespace CscCoop;

// elevator suite (tools/run-pair.ps1 -Test elevator -Mission 11, Developers' Hell): ride 1: the client stands in the
// lower car and presses its button (the game's InteractablePanel.Interact), the host stays outside: the client goes
// up, the host must not move (owner's test: everyone was moved). Ride 2: the host steps into the upper car, where
// the client now is, and presses its button: both go down.
public partial class Bot
{
    TeleportPlayer elUp, elDown;

    static void PutAt(GameWorld w, Vector3 p)
    {
        var cc = w.Player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        w.Player.transform.position = p;
        if (cc != null) cc.enabled = true;
    }

    static Vector3 CarFloor(TeleportPlayer tp)
    {
        var a = Elevators.Area(tp);
        var b = a ?? new Bounds(tp.transform.position, Vector3.one);
        return new Vector3(b.center.x, b.min.y + 0.1f, b.center.z);
    }

    static void Press(TeleportPlayer tp, string who)
    {
        var ip = tp.GetComponent<InteractablePanel>();
        Plugin.L.LogInfo($"ELEVATOR {who} presses '{tp.name}' (panel {(ip != null)}, active {tp.gameObject.activeInHierarchy})");
        ip?.Interact();
    }

    void ElevatorLog(GameWorld w, string label)
    {
        var p = w.Player.transform.position;
        Plugin.L.LogInfo(FormattableString.Invariant($"ELEVATOR {label} y {p.y:F2} at {P3(p)} in lower {(elUp != null && Elevators.Inside(elUp, p))} in upper {(elDown != null && Elevators.Inside(elDown, p))} skipped {Elevators.Skipped} rode {Elevators.Rode}"));
    }

    void ElevatorTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        switch (step)
        {
            case 0 when t > 3:
                // the cars' own buttons (ElevatorDoor.teleportPlayer); the scene has other objects with the same names
                foreach (var d in UnityEngine.Object.FindObjectsOfType<ElevatorDoor>(true))
                {
                    var tp = d.teleportPlayer;
                    if (tp == null) continue;
                    if (tp.name.Contains("GoUp")) elUp = tp;
                    if (tp.name.Contains("GoDown")) elDown = tp;
                }
                // The buttons are switched off in this save until the story enables them (test only: on now).
                foreach (var tp in new[] { elUp, elDown }) if (tp != null && !tp.gameObject.activeSelf) tp.gameObject.SetActive(true);
                foreach (var tp in new[] { elUp, elDown })
                    if (tp != null)
                    {
                        var ar = Elevators.Area(tp);
                        string boxes = "";
                        if (tp.rigidbodiesProvider != null) foreach (var x in tp.rigidbodiesProvider.GetComponents<BoxCollider>()) boxes += $" [{(x.isTrigger ? "trigger" : "solid")} enabled {x.enabled} center {P3(x.center)} size {P3(x.size)} on {x.name}]";
                        Plugin.L.LogInfo($"ELEVATOR area of {tp.name}: {(ar.HasValue ? P3(ar.Value.center) + " size " + P3(ar.Value.size) : "none")} provider {(tp.rigidbodiesProvider != null ? tp.rigidbodiesProvider.name : "-")} at {(tp.rigidbodiesProvider != null ? P3(tp.rigidbodiesProvider.transform.position) : "-")}{boxes}");
                    }
                Plugin.L.LogInfo($"ELEVATOR buttons up {(elUp != null)} down {(elDown != null)}; active up {(elUp != null && elUp.gameObject.activeInHierarchy)} down {(elDown != null && elDown.gameObject.activeInHierarchy)}");
                step = 1; break;
            case 1 when t > 5:
                if (!host && elUp != null) PutAt(w, CarFloor(elUp));
                if (host && elUp != null) PutAt(w, CarFloor(elUp) + new Vector3(0f, 0f, 8f));   // outside, on the same floor
                step = 2; break;
            case 2 when t > 8:
                ElevatorLog(w, "before ride 1");
                if (!host && elUp != null) Press(elUp, "client");
                step = 3; break;
            case 3 when t > 22:
                ElevatorLog(w, "after ride 1");
                step = 4; break;
            case 4 when t > 23:
                if (host && elDown != null) PutAt(w, CarFloor(elDown) + new Vector3(0.8f, 0f, 0f));
                step = 5; break;
            case 5 when t > 26:
                ElevatorLog(w, "before ride 2");
                if (host && elDown != null) Press(elDown, "host");
                step = 6; break;
            case 6 when t > 40:
                ElevatorLog(w, "after ride 2");
                Plugin.L.LogInfo("ELEVATOR dump done");
                step = 7; break;
        }
    }
}
