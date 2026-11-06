using System;
using System.Collections.Generic;
using System.IO;
using CoopCore;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CscCoop;

// skinprobe (tools/run-probe.ps1 -Test skinprobe): the job's bodies as skins. Lists each body (humanoid Animator,
// meshes), then puts up to three PlayerFigures with a body's skin in front of the player: one walking, one carrying,
// one standing and looking up and down. Screenshots go to CSCCOOP_SHOT_DIR (shot_skin_*.png).
static class SkinProbe
{
    static int step;
    static float at, t0;
    static readonly List<PlayerFigure> figs = new();
    static Transform look;

    public static void Tick(GameWorld w)
    {
        if (w.Player == null || !w.Player.CanMove) { at = Time.unscaledTime + 6; return; }
        float now = Time.unscaledTime;
        if (step == 0 && now >= at)
        {
            step = 1;
            var pt = w.Player.transform;
            var bodies = Object.FindObjectsOfType<BodyRagdollController>(true);
            Plugin.L.LogInfo($"SKINPROBE {bodies.Length} bodies");
            int n = 0;
            foreach (var b in bodies)
            {
                var anim = b.GetComponentInChildren<Animator>(true);
                var smrs = b.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                string meshes = "";
                foreach (var s in smrs) meshes += $" {s.name}({(s.sharedMesh != null ? s.sharedMesh.vertexCount : 0)}v,{s.bones.Length}b,{(s.sharedMaterial != null && s.sharedMaterial.shader != null ? s.sharedMaterial.shader.name : "-")})";
                Plugin.L.LogInfo($"SKINPROBE body '{b.name}' animator '{(anim != null ? anim.name : "-")}' human {(anim != null && anim.isHuman)} avatar {(anim?.avatar != null ? anim.avatar.name : "-")} meshes{meshes}");
                if (n >= 3) continue;
                var fig = new PlayerFigure((byte)(10 + n), Color.cyan);
                var skin = SkinModel.From(b.gameObject, fig.Root.transform, out var why);
                if (skin == null) { Plugin.L.LogInfo($"SKINPROBE   no skin: {why}"); fig.Destroy(); continue; }
                fig.SetSkin(skin);
                Plugin.L.LogInfo($"SKINPROBE   skin '{skin.Source}': {skin.Meshes} meshes, {skin.Mapped} of 17 bones mapped");
                figs.Add(fig);
                n++;
            }
            look = new GameObject("SkinProbeLook").transform;
            look.position = pt.position + pt.forward * 3f + Vector3.up * 1.0f;
            t0 = now;
            at = now + 2.5f;
            return;
        }
        if (step >= 1 && figs.Count > 0)
        {
            var pt = w.Player.transform;
            float t = now - t0;
            for (int i = 0; i < figs.Count; i++)
            {
                var basePos = pt.position + pt.forward * 3f + pt.right * ((i - (figs.Count - 1) / 2f) * 1.1f);
                var feet = i == 0 ? basePos + pt.right * Mathf.Sin(t * 1.2f) * 0.35f : basePos;   // the first one walks to and fro
                float yaw = pt.eulerAngles.y + 180 + (i == 0 ? (Mathf.Cos(t * 1.2f) > 0 ? 90 : -90) : 0);
                float pitch = i == 2 ? Mathf.Sin(t * 1.5f) * 50 : 0;
                var carry = i == 1 ? PlayerFigure.Carry.Item : PlayerFigure.Carry.None;
                figs[i].Pose(feet, yaw, pitch, carry, PlayerPose.ToolNone, Vector3.zero, Quaternion.identity, 1, false, Time.deltaTime);
            }
            if (look != null) w.Player.LookAt(look);
        }
        if (step >= 1 && step <= 3 && now >= at)
        {
            string dir = Env.Get("CSCCOOP_SHOT_DIR");
            if (dir != "") ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"shot_skin_{step}.png"));
            Plugin.L.LogInfo($"SKINPROBE screenshot {step}");
            step++; at = now + 1.7f;
            return;
        }
        if (step == 4 && now >= at)
        {
            Plugin.L.LogInfo("SKINPROBE done");
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
