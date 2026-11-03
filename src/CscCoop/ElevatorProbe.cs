using UnityEngine;

namespace CscCoop;

// elevatorprobe (tools/run-probe.ps1 -Test elevatorprobe -Mission 11): the job's elevators (ElevatorDoor +
// TeleportPlayer) and which interactables' OnInteract call into them, so a suite can press the real button.
public partial class Bot
{
    bool epDone;

    void ElevatorProbe(string scene, float inScene, GameWorld w)
    {
        string only = Env.Get("CSCCOOP_TEST_SCENE");
        if (epDone || w.Player == null || !w.Player.CanMove || inScene < 8 || (only != "" && scene != only)) return;
        epDone = true;
        foreach (var d in Object.FindObjectsOfType<ElevatorDoor>(true))
        {
            var tp = d.teleportPlayer;
            Plugin.L.LogInfo($"ELEVATORPROBE door '{GameWorld.IndexedPathOf(d.transform)}' at {P3(d.transform.position)} open {d.isOpen} teleport {(tp != null ? GameWorld.IndexedPathOf(tp.transform) : "none")}"
                + (tp != null ? $" target {(tp.targetPosition != null ? P3(tp.targetPosition.position) : "-")} localRef {(tp.localReferenceTarget != null ? P3(tp.localReferenceTarget.position) : "-")} area {(tp.rigidbodiesProvider != null ? GameWorld.IndexedPathOf(tp.rigidbodiesProvider.transform) : "-")} other {(d.otherDoors != null ? GameWorld.IndexedPathOf(d.otherDoors.transform) : "-")}" : ""));
            if (tp != null)
                foreach (var mb in tp.GetComponents<MonoBehaviour>())
                {
                    var it = mb.TryCast<Interactable>();
                    string evs = "";
                    if (it != null && it.OnInteract != null)
                        for (int k = 0; k < it.OnInteract.GetPersistentEventCount(); k++)
                            evs += $" {(it.OnInteract.GetPersistentTarget(k) != null ? it.OnInteract.GetPersistentTarget(k).GetIl2CppType().Name : "null")}.{it.OnInteract.GetPersistentMethodName(k)}";
                    Plugin.L.LogInfo($"ELEVATORPROBE   button component {mb.GetIl2CppType().Name} interactable {(it != null)} actions index {Actions.IndexOfAny(mb)} events[{evs}]");
                }
            if (tp?.rigidbodiesProvider != null)
                foreach (var c in tp.rigidbodiesProvider.GetComponents<Collider>())
                    Plugin.L.LogInfo($"ELEVATORPROBE   area collider {c.GetIl2CppType().Name} trigger {c.isTrigger} bounds {P3(c.bounds.center)} size {P3(c.bounds.size)}");
        }
        foreach (var it in Object.FindObjectsOfType<Interactable>(true))
        {
            var ev = it.OnInteract;
            if (ev == null) continue;
            for (int k = 0; k < ev.GetPersistentEventCount(); k++)
            {
                var target = ev.GetPersistentTarget(k);
                string m = ev.GetPersistentMethodName(k);
                if (target == null) continue;
                string tn = target.GetIl2CppType().Name;
                if (!(tn.Contains("Elevator") || tn.Contains("Teleport") || m.Contains("Teleport") || m.Contains("Elevator"))) continue;
                Plugin.L.LogInfo($"ELEVATORPROBE button '{GameWorld.IndexedPathOf(it.transform)}' ({it.GetIl2CppType().Name}) at {P3(it.transform.position)} -> {tn}.{m} index in actions table {Actions.IndexOfAny(it)}");
            }
        }
        Plugin.L.LogInfo("ELEVATORPROBE done");
        System.Diagnostics.Process.GetCurrentProcess().Kill();
    }
}
