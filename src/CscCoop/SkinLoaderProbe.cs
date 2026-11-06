using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CscCoop.Skins;
using UnityEngine;

namespace CscCoop;

// skinloader (tools/run-probe.ps1 -Test skinloader): the catalog of people and the background loader as the skins
// window will use them. "On screen" is people 0-5 at first; after 3 loaded it moves to 60-65 (a scroll); after 7 the
// loader is stopped (the window closed), and nothing more may load. The worst frame time while loading is compared
// with the frames before; the loaded people stand in a row for a screenshot (shot_skinloader.png).
static class SkinLoaderProbe
{
    static int step;
    static float at, worstBefore, worstLoading;
    static SkinLoader loader;
    static readonly List<(Person p, GameObject go, Transform[] hum)> got = new();
    static readonly List<PlayerFigure> figs = new();
    static readonly HashSet<string> done = new();
    static int window;   // first index "on screen"
    static int loadedAtStop = -1;

    public static void Tick(GameWorld w)
    {
        if (w.Player == null || !w.Player.CanMove) { at = Time.unscaledTime + 4; return; }
        float now = Time.unscaledTime;
        if (step == 0 && now >= at) { SkinCatalog.Load(); step = 1; at = now + 3; }
        if (step == 1)
        {
            worstBefore = Mathf.Max(worstBefore, Time.unscaledDeltaTime);
            if (!SkinCatalog.Ready || now < at) return;
            var ppl = SkinCatalog.People;
            Plugin.L.LogInfo($"SKINLOADER catalog {ppl.Count} people; first: {string.Join(", ", ppl.Take(6).Select(p => p.Display))}; at 60: {string.Join(", ", ppl.Skip(60).Take(6).Select(p => p.Display))}");
            loader = new SkinLoader(2, Next, (p, go, hum) =>
            {
                done.Add(p.Key);
                Plugin.L.LogInfo(FormattableString.Invariant($"SKINLOADER loaded #{loader.Loaded + loader.Failed} '{p.Display}' (index {IndexOf(p)}) {(go != null ? "ok" : "FAILED")} at {Time.unscaledTime:F2}"));
                if (go != null) got.Add((p, go, hum));
                if (done.Count == 3) { window = 60; Plugin.L.LogInfo("SKINLOADER scrolled to 60"); }
                if (done.Count == 7) { loader.Stop(); loadedAtStop = done.Count; Plugin.L.LogInfo("SKINLOADER window closed: stopped"); }
            });
            step = 2;
        }
        if (step == 2)
        {
            worstLoading = Mathf.Max(worstLoading, Time.unscaledDeltaTime);
            loader.Tick();
            if (loadedAtStop > 0) { step = 3; at = now + 4; }
        }
        if (step == 3)
        {
            loader.Tick();
            if (now < at) return;
            Plugin.L.LogInfo(FormattableString.Invariant($"SKINLOADER after stop: {done.Count} loaded (was {loadedAtStop} at stop), loader busy {loader.Busy}; worst frame before {worstBefore * 1000:F0} ms, while loading {worstLoading * 1000:F0} ms"));
            // the loaded people in a row in front of the player, as skins (bind pose, then the figure's standing pose)
            var pt = w.Player.transform;
            for (int i = 0; i < got.Count; i++)
            {
                var fig = new PlayerFigure((byte)(30 + i), Color.cyan);
                fig.SetSkin(SkinModel.FromBuilt(got[i].go, got[i].hum, got[i].p.Display, fig.Root.transform));
                var feet = pt.position + pt.forward * 4f + pt.right * ((i - (got.Count - 1) / 2f) * 0.9f);
                fig.Pose(feet, pt.eulerAngles.y + 180, 0, PlayerFigure.Carry.None, CoopCore.PlayerPose.ToolNone, Vector3.zero, Quaternion.identity, 1, false, 0.02f);
                figs.Add(fig);
            }
            var look = new GameObject("SkinLoaderLook").transform;
            look.position = pt.position + pt.forward * 4f + Vector3.up * 1f;
            w.Player.LookAt(look);
            step = 4; at = now + 2;
        }
        if (step == 4 && now >= at)
        {
            string dir = Env.Get("CSCCOOP_SHOT_DIR");
            if (dir != "") ScreenCapture.CaptureScreenshot(Path.Combine(dir, "shot_skinloader.png"));
            step = 5; at = now + 2;
        }
        if (step == 5 && now >= at)
        {
            Plugin.L.LogInfo("SKINLOADER done");
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }

    // The first person "on screen" that has none yet.
    static Person Next()
    {
        var ppl = SkinCatalog.People;
        for (int i = window; i < Math.Min(ppl.Count, window + 6); i++) if (!done.Contains(ppl[i].Key)) return ppl[i];
        return null;
    }

    static int IndexOf(Person p) { var ppl = SkinCatalog.People; for (int i = 0; i < ppl.Count; i++) if (ppl[i].Key == p.Key) return i; return -1; }
}
