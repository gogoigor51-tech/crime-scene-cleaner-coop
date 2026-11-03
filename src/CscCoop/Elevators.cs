using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CscCoop;

// Elevators (Developers' Hell: ElevatorGameDevParter / ElevatorGameDevCEO). The button inside is an InteractablePanel
// on the TeleportButton object; its OnInteract runs ElevatorDoor.Teleport, which closes the doors and, when they are
// shut, TeleportPlayer.Teleport moves "the player" (each machine's own FirstPersonController) plus the rigidbodies in
// the car (RigidbodiesInsideAreaChecker). InteractablePanel.Interact is a replayed action (story objects), so one
// player's ride moved every player, wherever they stood (owner's test). Now a ride another player started moves this
// machine's player only if they stand in the car; otherwise only what is in it (TeleportPlayer.TeleportNoPlayer, the
// game's own call for a summoned elevator).
[HarmonyPatch]
public static class Elevators
{
    static readonly HashSet<IntPtr> remoteRide = new();
    public static int Skipped, Rode;

    [HarmonyPatch(typeof(ElevatorDoor), nameof(ElevatorDoor.Teleport))] [HarmonyPrefix]
    static void DoorTeleport(ElevatorDoor __instance)
    {
        var d = __instance.TryCast<ElevatorDoor>();
        var tp = d != null ? d.teleportPlayer : null;
        if (tp == null) return;
        if (WorldEvents.Applying) remoteRide.Add(tp.Pointer); else remoteRide.Remove(tp.Pointer);
    }

    [HarmonyPatch(typeof(TeleportPlayer), nameof(TeleportPlayer.Teleport))] [HarmonyPrefix]
    static bool Teleport(TeleportPlayer __instance)
    {
        var tp = __instance.TryCast<TeleportPlayer>();
        if (tp == null || !remoteRide.Remove(tp.Pointer) || Env.Get("CSCCOOP_MUTANT") == "elevator-all") return true;
        var player = CoopRunner.Instance?.World?.Player;
        if (player == null || Inside(tp, player.transform.position)) { Rode++; Plugin.L.LogInfo("COOP elevator: another player's ride, and we are in the car: riding along"); return true; }
        Skipped++;
        Plugin.L.LogInfo("COOP elevator: another player's ride, we are not in the car: only what is in it moves");
        tp.TeleportNoPlayer();
        return false;
    }

    // In the car: within its area trigger (expanded a little). The box is read from its own size and transform: the
    // game switches the trigger off around a ride, and a disabled collider's bounds are empty.
    public static bool Inside(TeleportPlayer tp, Vector3 feet)
    {
        var area = Area(tp);
        if (area == null) return tp.localReferenceTarget != null && Vector3.Distance(tp.localReferenceTarget.position, feet) < 2.5f;
        var b = area.Value; b.Expand(0.4f);
        return b.Contains(feet + Vector3.up * 0.5f);
    }

    public static Bounds? Area(TeleportPlayer tp)
    {
        if (tp == null || tp.rigidbodiesProvider == null) return null;
        foreach (var c in tp.rigidbodiesProvider.GetComponents<BoxCollider>())
        {
            if (c == null || !c.isTrigger) continue;
            var t = c.transform;
            var b = new Bounds(t.TransformPoint(c.center), Vector3.zero);
            var e = c.size * 0.5f;
            for (int k = 0; k < 8; k++) b.Encapsulate(t.TransformPoint(c.center + new Vector3((k & 1) == 0 ? -e.x : e.x, (k & 2) == 0 ? -e.y : e.y, (k & 4) == 0 ? -e.z : e.z)));
            return b;
        }
        return null;
    }
}
