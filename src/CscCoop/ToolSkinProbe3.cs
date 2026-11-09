using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CscCoop;

// One-off investigation (run-probe -Test toolskinprobe3, one copy): what a skin is made of, to dress copies of other
// players' tools without the game's skin scripts (which act on the local player's hands). For each tool prefab: its
// Skinable's parent for the skin model, default parts and material changers. For a skin of each type: its model and
// material sets, loaded from the game's addressables.
static class ToolSkinProbe3
{
    static float at = -1;
    static int step, waits;
    static readonly List<(SkinData d, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> model, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> mats, bool hasModel, bool hasMats)> loads = new();

    public static void Tick(GameWorld w)
    {
        if (w.Player == null || !w.Player.CanMove) { at = Time.unscaledTime + 6; return; }
        if (Time.unscaledTime < at) return;
        at = Time.unscaledTime + 5;
        try { Step(); } catch (Exception e) { Plugin.L.LogInfo($"TS3 step {step} failed: {e}"); step++; }
    }

    static void Step()
    {
        var sc = SkinableController.Instance;
        switch (step)
        {
            case 0:
                step = 1;
                foreach (var td in PlayerInventory.Instance.ItemsContainer.toolDatas)
                {
                    var p = td?.ItemPrefab;
                    if (p == null) continue;
                    foreach (var s in p.GetComponentsInChildren<Skinable>(true))
                    {
                        var sb = new StringBuilder();
                        sb.Append($" parent '{(s.skinModelParent != null ? Path(s.skinModelParent, p.transform) : "-")}'");
                        sb.Append(" elements [");
                        if (s.defaultSkinElements != null) foreach (var e in s.defaultSkinElements) sb.Append(e != null ? Path(e.transform, p.transform) : "null").Append(", ");
                        sb.Append("] renderers [");
                        if (s.defaultMeshRenderers != null) foreach (var e in s.defaultMeshRenderers) sb.Append(e != null ? Path(e.transform, p.transform) : "null").Append(", ");
                        sb.Append("] changers [");
                        foreach (var m in p.GetComponentsInChildren<SkinableMaterialChanger>(true)) sb.Append($"{m.partName}@{Path(m.transform, p.transform)}, ");
                        sb.Append("] decals [");
                        foreach (var m in p.GetComponentsInChildren<SkinableDecal>(true)) sb.Append($"{m.partName}@{Path(m.transform, p.transform)}, ");
                        sb.Append("] others [");
                        foreach (var m in p.GetComponentsInChildren<MonoBehaviour>(true)) { var n = m.GetIl2CppType().Name; if (n.Contains("Skin") && !n.StartsWith("Skinable")) sb.Append($"{n}@{Path(m.transform, p.transform)}, "); }
                        sb.Append(']');
                        Plugin.L.LogInfo($"TS3 tool {td.id} '{p.name}' {s.GetIl2CppType().Name} {s.skinType}{sb}");
                    }
                }
                var types = new HashSet<int>();
                foreach (var d in sc.SkinDatas)
                {
                    if (d == null) continue;
                    if (!types.Add((int)d.Type) && d.id != 7 && d.id != 39) continue;
                    bool hm = d.SkinPrefabReference != null && d.SkinPrefabReference.RuntimeKeyIsValid();
                    bool hc = d.MaterialChangersScriptable != null && d.MaterialChangersScriptable.RuntimeKeyIsValid();
                    var model = hm ? d.SkinPrefabReference.LoadAssetAsync<GameObject>() : default;
                    var mats = hc ? d.MaterialChangersScriptable.LoadAssetAsync<GameObject>() : default;
                    loads.Add((d, model, mats, hm, hc));
                    Plugin.L.LogInfo($"TS3 skin {d.id} '{d.DisplayName}' {d.Type}: model '{(hm ? d.SkinPrefabReference.AssetGUID : "-")}' materials '{(hc ? d.MaterialChangersScriptable.AssetGUID : "-")}' offset {d.ModelPositionOffset} {d.ModelRotationOffset} layer {d.skinLayerMask.value} decals {d.SkinDecals?.Count}");
                }
                return;
            case 1:
                if (++waits < 7)
                {
                    foreach (var (d, model, _, hm, _) in loads)
                        if (hm) Plugin.L.LogInfo($"TS3 wait {waits} {d.id}: valid {model.IsValid()} done {(model.IsValid() ? model.IsDone : false)} status {(model.IsValid() ? model.Status : default)} pct {(model.IsValid() ? model.PercentComplete : -1)} ex '{(model.IsValid() ? model.OperationException?.Message : "")}' ref asset '{d.SkinPrefabReference.Asset?.name}' ref valid {d.SkinPrefabReference.IsValid()}");
                    return;
                }
                step = 2;
                foreach (var (d, model, mats, hm, hc) in loads)
                {
                    var sb = new StringBuilder();
                    if (hm)
                    {
                        sb.Append($" model {model.Status}");
                        var go = model.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded ? model.Result : null;
                        if (go != null)
                        {
                            sb.Append($" '{go.name}' layer {go.layer} parts [");
                            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) sb.Append($"{Path(r.transform, go.transform)}:{r.GetIl2CppType().Name}:{r.enabled && r.gameObject.activeSelf}:{r.sharedMaterial?.name}, ");
                            sb.Append("] scripts [");
                            foreach (var m in go.GetComponentsInChildren<MonoBehaviour>(true)) sb.Append($"{m.GetIl2CppType().Name}@{Path(m.transform, go.transform)}, ");
                            sb.Append(']');
                        }
                    }
                    if (hc)
                    {
                        sb.Append($" materials {mats.Status}");
                        var go = mats.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded ? mats.Result : null;
                        var cd = go != null ? go.GetComponent<SkinChangerMaterialData>() : null;
                        if (cd?.Materials != null)
                        {
                            sb.Append(" [");
                            foreach (var sm in cd.Materials) sb.Append(sm?.name).Append(", ");
                            sb.Append(']');
                        }
                    }
                    if (d.SkinDecals != null) foreach (var dc in d.SkinDecals) sb.Append($" decal {dc?.name}:{dc?.decal?.GetIl2CppType().Name}");
                    Plugin.L.LogInfo($"TS3 loaded {d.id} '{d.DisplayName}'{sb}");
                }
                return;
            case 2:
                Plugin.L.LogInfo("BOT done, quitting");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
        }
    }

    static string Path(Transform t, Transform top)
    {
        var p = t.name;
        for (t = t.parent; t != null && t != top; t = t.parent) p = t.name + "/" + p;
        return p;
    }
}
