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
                if (anim != null && anim.isHuman)
                {
                    string map = "";
                    foreach (HumanBodyBones hb in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightUpperArm })
                    { var t = anim.GetBoneTransform(hb); map += $" {hb}={(t != null ? t.name : "-")}"; }
                    Plugin.L.LogInfo($"SKINPROBE map{map}");
                }
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
            // CSCCOOP_SKIN_FILES=level38,level6: people built from other jobs' files instead (SkinFiles).
            string files = Env.Get("CSCCOOP_SKIN_FILES");
            if (files != "")
            {
                foreach (var fg in figs) fg.Destroy();
                figs.Clear();
                var set = new Skins.AssetSet(Skins.SkinFiles.DataDir);
                var people = new List<Skins.Person>();
                foreach (var lv in files.Split(','))
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var found = Skins.PersonReader.FindPeople(set, lv);
                    Plugin.L.LogInfo($"SKINPROBE {lv}: {found.Count} people in {sw.ElapsedMilliseconds} ms: {string.Join(", ", found.ConvertAll(x => $"{x.Name}({x.Meshes})"))}");
                    people.AddRange(found.GetRange(0, Math.Min(found.Count, lv == files.Split(',')[0] ? 1 : 2)));
                }
                foreach (var person in people)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var fig = new PlayerFigure((byte)(20 + figs.Count), Color.cyan);
                    var go = Skins.SkinFiles.Build(set, person, null, 1, out var hum, out var why);
                    if (go == null) { Plugin.L.LogInfo($"SKINPROBE   {person.Name}: {why}"); fig.Destroy(); continue; }
                    var skin = SkinModel.FromBuilt(go, hum, person.Name, fig.Root.transform);
                    fig.SetSkin(skin);
                    Plugin.L.LogInfo($"SKINPROBE   built '{person.Name}' from {person.File} in {sw.ElapsedMilliseconds} ms: {skin.Meshes} meshes, {skin.Mapped} of 17 bones");
                    figs.Add(fig);
                }
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
