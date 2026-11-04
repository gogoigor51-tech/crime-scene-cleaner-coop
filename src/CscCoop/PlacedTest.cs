using System;
using CoopCore;
using UnityEngine;

namespace CscCoop;

// placed suite (tools/run-pair.ps1 -Test placed): the client picks up a piece of furniture that belongs on a hologram
// and releases it onto its spot the game's way (FurnitureHologram.OnFurnitureRelease: TryReserveFurniture, Fix,
// UpdateQuest), then lets it go back to the host. The host then pushes it hard. A placed piece is kinematic in the
// game and stays put; the owner's test of 0.4.3-test: one placed by the client kept its physics, could be knocked off
// its spot or fell by itself.
public partial class Bot
{
    int plH = -1, plF = -1, plItem = -1;
    Vector3 plSpot;

    void PlacedLog(Session ses, GameWorld w, string label)
    {
        if (plItem < 0) { Plugin.L.LogInfo($"PLACED {label}: no piece"); return; }
        var g = w.Items[plItem];
        var rb = g.Rb;
        var p = g.transform.position;
        Plugin.L.LogInfo(FormattableString.Invariant($"PLACED {label}: fixed {Furniture.Pieces[plF].IsFixed} correct {Furniture.Holograms[plH].IsCorrect} kinematic {(rb != null && rb.isKinematic)} at {P3(p)} off {Vector3.Distance(p, plSpot):F3} owner {ses.OwnerOf(plItem)} {w.Describe(plItem)}"));
    }

    void PlacedTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        switch (step)
        {
            case 0 when t > 3:
                for (int h = 0; h < Furniture.Holograms.Count && plH < 0; h++)
                {
                    var holo = Furniture.Holograms[h];
                    if (holo == null || holo.IsCorrect) continue;
                    for (int f = 0; f < Furniture.Pieces.Count; f++)
                    {
                        var pc = Furniture.Pieces[f];
                        if (pc == null || pc.IsFixed || pc.FurnitureMesh != holo.FurnitureMesh || pc.GrabController == null) continue;
                        int it = w.IndexOf(pc.GrabController);
                        if (it < 0) continue;
                        plH = h; plF = f; plItem = it; break;
                    }
                }
                Plugin.L.LogInfo($"PLACED roles hologram {plH} piece {plF} item {plItem} '{(plF >= 0 ? Furniture.Pieces[plF].name : "-")}'");
                PlacedLog(ses, w, "before");
                step = 1; break;
            case 1 when t > 4:
                if (!host && plItem >= 0) { Approach(w, plItem); }
                step = 2; break;
            case 2 when t > 5:
                if (!host && plItem >= 0) Grab(w, plItem, "client");
                step = 3; break;
            case 3 when t > 7:
                if (!host && plItem >= 0)
                {
                    var g = w.Items[plItem];
                    var holo = Furniture.Holograms[plH];
                    var piece = Furniture.Pieces[plF];
                    // Held over its hologram, as a player lines it up: the game marks the hologram active and closest
                    // (ChangeHologramState / SetClosestHologram), and the release puts it there.
                    g.transform.SetPositionAndRotation(holo.transform.position, holo.transform.rotation);
                    holo.ChangeHologramState(true, piece, 0f);
                    holo.SetClosestHologram(true);
                    w.Grabber.TryRelease();
                    if (!piece.IsFixed) holo.OnFurnitureRelease(piece);
                    Plugin.L.LogInfo($"PLACED client put '{g.name}' on its spot: fixed {Furniture.Pieces[plF].IsFixed}");
                }
                step = 4; break;
            case 4 when t > 9:
                if (plItem >= 0) plSpot = w.Items[plItem].transform.position;
                PlacedLog(ses, w, "placed");
                step = 5; break;
            case 5 when t > 20:   // back with the host by now (it rests)
                PlacedLog(ses, w, "rested");
                if (host && plItem >= 0)
                {
                    var g = w.Items[plItem];
                    PutAt(w, g.transform.position + new Vector3(0.6f, 0, 0));   // the host's physics runs near it
                    var rb = g.Rb;
                    if (rb != null && !rb.isKinematic) { rb.WakeUp(); rb.AddForce(new Vector3(-1, 0.5f, 0.3f) * 8, ForceMode.VelocityChange); }
                    Plugin.L.LogInfo($"PLACED host pushed it (kinematic {(rb != null && rb.isKinematic)})");
                }
                step = 6; break;
            case 6 when t > 30:
                PlacedLog(ses, w, "after push");
                Plugin.L.LogInfo("PLACED dump done");
                step = 7; break;
        }
    }
}
