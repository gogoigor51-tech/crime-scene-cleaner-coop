using System;
using System.Text;
using UnityEngine;

namespace CscCoop;

// One-off investigation (run-probe -Test toolskinprobe, one copy): how the game gives tools their level and skin, for
// showing other players' tools as theirs. Lists the skins (SkinableController), the chosen ones, every tool's data
// (level, prefab, its Skinable / DualSponge parts), the placed tools' prefabs and the trash can's bag; then puts on a
// skin of each tool kind in turn and logs what changed on the held tool and on placed ones.
static class ToolSkinProbe
{
    static float at = -1;
    static int step;
    static System.Collections.Generic.List<SkinData> tryList;
    static int tryAt;

    public static void Tick(GameWorld w)
    {
        if (w.Player == null || !w.Player.CanMove) { at = Time.unscaledTime + 6; return; }
        if (Time.unscaledTime < at) return;
        at = Time.unscaledTime + 4;
        try { Step(w); } catch (Exception e) { Plugin.L.LogInfo($"TSPROBE step {step} failed: {e}"); step++; }
    }

    static void Step(GameWorld w)
    {
        var sc = SkinableController.Instance;
        switch (step)
        {
            case 0:
                step = 1;
                if (sc == null) { Plugin.L.LogInfo("TSPROBE no SkinableController"); return; }
                foreach (var d in sc.SkinDatas)
                    if (d != null) Plugin.L.LogInfo($"TSPROBE skin {d.id} '{d.DisplayName}' type {d.Type} cat {d.Category} anim {d.AnimationType} prefab '{d.SkinPrefabReference?.RuntimeKey}' changers {(d.MaterialChangersScriptable != null ? "yes" : "no")} decals {d.SkinDecals?.Count} unlocked {sc.IsSkinUnlocked(d.id)} equipped {sc.IsSkinEquipped(d.id)}");
                foreach (var kv in sc.SelectedSkins) Plugin.L.LogInfo($"TSPROBE selected {kv.Key}: {kv.Value?.id} '{kv.Value?.DisplayName}'");
                Plugin.L.LogInfo($"TSPROBE BucketLevel {string.Join(",", Enum.GetNames(typeof(BucketLevel)))}");
                return;
            case 1:
                step = 2;
                var c = PlayerInventory.Instance?.ItemsContainer;
                if (c?.toolDatas != null)
                    foreach (var td in c.toolDatas)
                    {
                        if (td == null) continue;
                        var p = td.ItemPrefab;
                        var sb = new StringBuilder();
                        if (p != null)
                        {
                            foreach (var s in p.GetComponentsInChildren<Skinable>(true)) sb.Append($" skinable {s.GetIl2CppType().Name}@{Path(s.transform, p.transform)} type {s.skinType};");
                            var ds = p.GetComponent<DualSponge>();
                            if (ds != null) sb.Append($" dual second '{(ds.secondSponge != null ? Path(ds.secondSponge, p.transform) : "-")}' off {ds.secondModelOffsetPosition} {ds.secondModelOffsetRotation};");
                            foreach (var h in p.GetComponentsInChildren<SkinableSecondHand>(true)) sb.Append($" secondhand@{Path(h.transform, p.transform)} left {h.isInLeftHand} pos {h.positionInHand};");
                            foreach (var g in p.GetComponentsInChildren<SkinableGarbageCollector>(true)) sb.Append($" garbageskin mats {g.materials?.Count};");
                            var gc = p.GetComponent<GarbageCollector>();
                            if (gc != null) sb.Append($" bag '{gc.garbagePrefab?.name}';");
                            var pi = p.GetComponent<PortableItem>();
                            if (pi != null) sb.Append($" portable {pi.GetIl2CppType().Name};");
                            int rn = 0; foreach (var r in p.GetComponentsInChildren<Renderer>(true)) rn++;
                            sb.Append($" renderers {rn}");
                        }
                        Plugin.L.LogInfo($"TSPROBE tool {td.id} '{td.DisplayName}' {td.GetIl2CppType().Name} level {td.itemLevel} base {td.baseStatsId} type {td.Type} cat {td.Category} prefab '{p?.name}'{sb}");
                    }
                foreach (var s in PlayerInventory.Instance.Slots)
                    if (s?.Item != null) Plugin.L.LogInfo($"TSPROBE slot tool {s.Item.id} '{s.Item.DisplayName}' instance '{s.ItemInstance?.name}' available {s.IsItemAvailable}");
                return;
            case 2:
                step = 3;
                foreach (var pi in Resources.FindObjectsOfTypeAll<PortableItem>())
                    if (pi != null) Plugin.L.LogInfo($"TSPROBE portable '{pi.name}' {pi.GetIl2CppType().Name} scene '{pi.gameObject.scene.name}' active {pi.gameObject.activeInHierarchy} skinables {Skins(pi.gameObject)} lamp {(pi.GetComponentInChildren<LampController>(true) != null)} liquid {(pi.GetComponentInChildren<LiquidContainer>(true) != null)}");
                foreach (var b in Resources.FindObjectsOfTypeAll<GarbageBagConnector>())
                    if (b != null) Plugin.L.LogInfo($"TSPROBE bag '{b.name}' scene '{b.gameObject.scene.name}' mats {b.materials?.Count} size {b.CurrentSize} max {b.maxSize} renderer mat '{b.GetComponentInChildren<Renderer>(true)?.sharedMaterial?.name}'");
                foreach (var g in Resources.FindObjectsOfTypeAll<SkinableGarbageCollector>())
                    if (g != null) Plugin.L.LogInfo($"TSPROBE garbageskin '{Path(g.transform, null)}' scene '{g.gameObject.scene.name}' mats [{Names(g.materials)}] connector '{g.garbageBagConnector?.name}'");
                LogHeld("before");
                return;
            case 3:
                // put on one unlocked-or-not skin of each tool type in turn
                if (tryList == null)
                {
                    tryList = new();
                    var seen = new System.Collections.Generic.HashSet<int>();
                    foreach (var d in sc.SkinDatas) if (d != null && seen.Add((int)d.Type)) tryList.Add(d);
                }
                if (tryAt > 0) { LogHeld($"after skin {tryList[tryAt - 1].id}"); LogPlaced(); }
                if (tryAt >= tryList.Count) { step = 4; return; }
                var t = tryList[tryAt++];
                Plugin.L.LogInfo($"TSPROBE ChangeToCustomSkin {t.id} '{t.DisplayName}' type {t.Type}");
                sc.ChangeToCustomSkin(t);
                return;
            case 4:
                foreach (var kv in sc.SelectedSkins) Plugin.L.LogInfo($"TSPROBE selected now {kv.Key}: {kv.Value?.id}");
                Plugin.L.LogInfo("BOT done, quitting");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
        }
    }

    static void LogHeld(string when)
    {
        var inv = InventoryController.Instance;
        if (inv?.EquippedItems == null) return;
        foreach (var u in inv.EquippedItems)
        {
            if (u == null) continue;
            var sb = new StringBuilder();
            foreach (var s in u.GetComponentsInChildren<Skinable>(true)) sb.Append($" [{s.skinType} current {s.CurrentSkin?.id} inst '{s.InstantiatedSkin?.name}']");
            int on = 0; var names = new StringBuilder();
            foreach (var r in u.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.gameObject.activeInHierarchy) { on++; names.Append(r.name).Append('/').Append(r.sharedMaterial?.name).Append(' '); }
            Plugin.L.LogInfo($"TSPROBE held {when} '{u.name}' active {u.gameObject.activeInHierarchy}{sb} drawn {on}: {names}");
        }
        var ds = UnityEngine.Object.FindObjectOfType<DualSponge>();
        if (ds != null) Plugin.L.LogInfo($"TSPROBE dual sponge '{Path(ds.transform, null)}' second '{(ds.secondSponge != null ? Path(ds.secondSponge, null) : "-")}'");
    }

    static void LogPlaced()
    {
        foreach (var pi in UnityEngine.Object.FindObjectsOfType<PortableItem>())
            Plugin.L.LogInfo($"TSPROBE placed '{pi.name}' skins {Skins(pi.gameObject)}");
    }

    static string Skins(GameObject go)
    {
        var sb = new StringBuilder();
        foreach (var s in go.GetComponentsInChildren<Skinable>(true)) sb.Append($"{s.GetIl2CppType().Name}:{s.skinType}:{s.CurrentSkin?.id} ");
        return sb.ToString();
    }

    static string Names(Il2CppSystem.Collections.Generic.List<Material> m)
    {
        if (m == null) return "";
        var sb = new StringBuilder();
        foreach (var x in m) sb.Append(x?.name).Append(", ");
        return sb.ToString();
    }

    static string Path(Transform t, Transform top)
    {
        var p = t.name;
        for (t = t.parent; t != null && t != top; t = t.parent) p = t.name + "/" + p;
        return p;
    }
}
