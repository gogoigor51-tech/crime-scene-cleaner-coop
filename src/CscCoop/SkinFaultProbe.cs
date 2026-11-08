using System.Collections.Generic;
using System.IO;
using System.Linq;
using CscCoop.Skins;
using UnityEngine;

namespace CscCoop;

// skinfaults (tools/run-probe.ps1 -Test skinfaults): the people the owner saw empty or white in the skins window
// (0.4.4-test2), each loaded as a cell picture is (mip skip 3), rendered by SkinPreview and saved as
// local/skinfault_<name>.png, with every renderer logged (bounds, materials, textures, missing bones).
static class SkinFaultProbe
{
    static readonly string[] Names = { "PoolBoy new", "wife new", "Doctor Body Normal", "Doctor", "BigJim", "Pizza Italian2 Cts", "Husband new", "DrugDealer" };
    static int step, index;
    static float at;
    static SkinLoader loader;
    static Person current;

    public static void Tick(GameWorld w)
    {
        if (w.Player == null || !w.Player.CanMove) { at = Time.unscaledTime + 4; return; }
        float now = Time.unscaledTime;
        if (now < at) return;
        if (step == 0) { SkinCatalog.Load(); step = 1; return; }
        if (step == 1 && SkinCatalog.Ready)
        {
            loader = new SkinLoader(3, () =>
            {
                if (current != null || index >= Names.Length) return null;
                current = SkinCatalog.People.FirstOrDefault(p => p.Display == Names[index]);
                if (current == null) { Plugin.L.LogInfo($"SKINFAULT '{Names[index]}' not in the catalog"); index++; }
                return current;
            }, Loaded);
            step = 2;
        }
        if (step == 2)
        {
            loader.Tick();
            SkinPreview.Tick();
            if (index >= Names.Length) { Plugin.L.LogInfo("SKINFAULT done"); step = 3; at = now + 1; }
        }
        if (step == 3) System.Diagnostics.Process.GetCurrentProcess().Kill();
    }

    static void Loaded(Person p, GameObject go, Transform[] hum)
    {
        if (go == null) { Plugin.L.LogInfo($"SKINFAULT '{p.Display}' ({p.Key}): not made"); current = null; index++; return; }
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = smr.sharedMaterials;
            string m = string.Join(" | ", mats.Select(x => x == null ? "null" : $"{x.name} base {(x.HasProperty("_BaseMap") && x.GetTexture("_BaseMap") != null ? x.GetTexture("_BaseMap").width.ToString() : "none")} col {(x.HasProperty("_BaseColor") ? x.GetColor("_BaseColor").ToString() : "-")} q {x.renderQueue}"));
            Plugin.L.LogInfo($"SKINFAULT '{p.Display}' renderer '{smr.name}' active {smr.gameObject.activeInHierarchy} enabled {smr.enabled} mesh {(smr.sharedMesh != null ? smr.sharedMesh.vertexCount : -1)} null bones {smr.bones.Count(b => b == null)}/{smr.bones.Length} root '{(smr.rootBone != null ? smr.rootBone.name : "-")}' mats {m}");
        }
        var name = p.Display;
        SkinPreview.Render(p, go, hum, rt =>
        {
            if (rt != null)
            {
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                string dir = Env.Get("CSCCOOP_SHOT_DIR");
                if (dir != "") File.WriteAllBytes(Path.Combine(dir, "skinfault_" + name.Replace(' ', '_') + ".png"), ImageConversion.EncodeToPNG(tex).ToArray());
            }
            Plugin.L.LogInfo($"SKINFAULT '{name}' picture {(rt != null)}");
            current = null; index++;
        });
    }
}
