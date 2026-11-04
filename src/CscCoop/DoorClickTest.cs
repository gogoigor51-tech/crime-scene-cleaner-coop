using System;
using System.Linq;
using System.Text;
using CoopCore;
using UnityEngine;

namespace CscCoop;

// doorclick suite (tools/run-pair.ps1 -Test doorclick -Mission 11, Developers' Hell): doors used the way a player's
// click does (ShortcutDoors.Interact, the IInteractable entry, with the player standing in front of the door), not by
// calling Toggle directly as the doorsync suite does. Logged on both machines: the open/closed flag and where the door
// leaves actually are (the DoorMoveAnimation objects' local rotation and position), since the owner saw doors open on
// the host and closed on the client (owner's test of 0.4.3-test).
public partial class Bot
{
    int[] dcDoors = Array.Empty<int>();

    static string Leaves(ShortcutDoors d)
    {
        var sb = new StringBuilder();
        var list = d.animationsList;
        if (list == null) return "-";
        for (int k = 0; k < list.Count; k++)
        {
            var a = list[k];
            if (a == null) continue;
            var t = a.gameObject.transform;
            sb.Append(FormattableString.Invariant($"[{t.localEulerAngles.y:F0}/{t.localPosition.x:F2},{t.localPosition.z:F2}]"));
        }
        return sb.ToString();
    }

    void DoorClickLog(string label)
    {
        var sb = new StringBuilder();
        foreach (int i in dcDoors) { var d = Doors.Shortcuts[i]; sb.Append($" {i}:{(d.isClosed ? "C" : "O")}{Leaves(d)}"); }
        Plugin.L.LogInfo($"DOORCLICK {label}:{sb}");
    }

    static void ClickReal(GameWorld w, int i, string who)
    {
        var d = Doors.Shortcuts[i];
        var p = d.transform.position;
        // Stand 1 m in front of it, facing it, as a player does.
        PutAt(w, new Vector3(p.x, w.Player.transform.position.y, p.z) + d.transform.forward * 1.0f);
        bool before = d.isClosed;
        d.Interact();
        Plugin.L.LogInfo($"DOORCLICK {who} clicked door {i} '{d.name}' ({d.GetIl2CppType().Name}): closed {before} -> {d.isClosed}");
    }

    void DoorClickTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        switch (step)
        {
            case 0 when t > 3:
                dcDoors = Enumerable.Range(0, Doors.Shortcuts.Count).Where(i => Doors.Shortcuts[i] != null && Doors.Shortcuts[i].gameObject.activeInHierarchy && !Doors.Shortcuts[i].IsLocked && Doors.Shortcuts[i].RequiredKeys.Count == 0 && Doors.Shortcuts[i].animationsList != null && Doors.Shortcuts[i].animationsList.Count > 0).Take(3).ToArray();
                Plugin.L.LogInfo($"DOORCLICK doors {string.Join(",", dcDoors)} of {Doors.Shortcuts.Count}");
                DoorClickLog("before");
                step = 1; break;
            case 1 when t > 5:
                if (host && dcDoors.Length > 1) { ClickReal(w, dcDoors[0], "host"); ClickReal(w, dcDoors[1], "host"); }
                step = 2; break;
            case 2 when t > 10:
                DoorClickLog("after host");
                if (!host && dcDoors.Length > 2) { ClickReal(w, dcDoors[1], "client"); ClickReal(w, dcDoors[2], "client"); }
                step = 3; break;
            case 3 when t > 15:
                DoorClickLog("after client");
                Plugin.L.LogInfo("DOORCLICK dump done");
                step = 4; break;
        }
    }
}
