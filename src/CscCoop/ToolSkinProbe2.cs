using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CscCoop;

// One-off investigation (run-probe -Test toolskinprobe2, one copy): can a tool be dressed in a chosen skin away from
// the player? Each held tool's prefab is copied under a hidden holder far below the level, its scripts other than the
// skin ones taken off before they wake, then given a skin of its type; a few seconds later its drawn parts are logged
// (and whether anything turned up in the local player's hands). Also the trash bag prefab's parts and lamp lights.
static class ToolSkinProbe2
{
    static float at = -1;
    static int step;
    static readonly List<(string name, GameObject go, int skin)> made = new();
    static int leftBefore, rightBefore;

    public static void Tick(GameWorld w)
    {
        if (w.Player == null || !w.Player.CanMove) { at = Time.unscaledTime + 6; return; }
        if (Time.unscaledTime < at) return;
        at = Time.unscaledTime + 5;
        try { Step(); } catch (Exception e) { Plugin.L.LogInfo($"TS2 step {step} failed: {e}"); step++; }
    }

    public static bool KeepScript(string type) => type.Contains("Skin") || type == "DualSponge";

    static void Step()
    {
        var sc = SkinableController.Instance;
        var inv = InventoryController.Instance;
        switch (step)
        {
            case 0:
                step = 1;
                leftBefore = inv.leftAttachPoint.childCount; rightBefore = inv.rightAttachPoint.childCount;
                var holder = new GameObject("TS2 holder"); holder.SetActive(false);
                holder.transform.position = new Vector3(0, -500, 0);
                foreach (var td in PlayerInventory.Instance.ItemsContainer.toolDatas)
                {
                    var p = td?.ItemPrefab;
                    if (p == null || p.GetComponentInChildren<Skinable>(true) == null) continue;
                    var skinType = p.GetComponentInChildren<Skinable>(true).skinType;
                    SkinData skin = null;
                    foreach (var d in sc.SkinDatas) if (d != null && d.Type == skinType) { skin = d; break; }
                    var go = UnityEngine.Object.Instantiate(p, holder.transform);
                    var removed = new StringBuilder();
                    foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        var tn = mb.GetIl2CppType().Name;
                        if (KeepScript(tn)) continue;
                        removed.Append(tn).Append(' ');
                        UnityEngine.Object.DestroyImmediate(mb);
                    }
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(rb);
                    made.Add((p.name, go, skin?.id ?? -1));
                    Plugin.L.LogInfo($"TS2 made '{p.name}' skin type {skinType} -> skin {skin?.id} '{skin?.DisplayName}'; removed {removed}");
                }
                holder.SetActive(true);
                foreach (var (name, go, _) in made) Log("awake", name, go);
                return;
            case 1:
                step = 2;
                foreach (var (name, go, skin) in made)
                {
                    var s = go.GetComponentInChildren<Skinable>(true);
                    var d = sc.GetSkinByID(skin);
                    try { s.TrySetCustomSkin(d); Plugin.L.LogInfo($"TS2 '{name}' TrySetCustomSkin {skin}"); }
                    catch (Exception e) { Plugin.L.LogInfo($"TS2 '{name}' TrySetCustomSkin failed: {e.Message}"); }
                }
                return;
            case 2:
                step = 3;
                foreach (var (name, go, _) in made) Log("skinned", name, go);
                Plugin.L.LogInfo($"TS2 player's attach points: left {leftBefore} -> {inv.leftAttachPoint.childCount}, right {rightBefore} -> {inv.rightAttachPoint.childCount}");
                foreach (var (name, go, skin) in made)
                {
                    var s = go.GetComponentInChildren<Skinable>(true);
                    try { s.TrySetDefaultSkin(); } catch (Exception e) { Plugin.L.LogInfo($"TS2 '{name}' default failed: {e.Message}"); }
                }
                return;
            case 3:
                step = 4;
                foreach (var (name, go, _) in made) Log("default again", name, go);
                // the bag and the lamps
                foreach (var gc in Resources.FindObjectsOfTypeAll<GarbageCollector>())
                    if (gc.garbagePrefab != null)
                    {
                        var sb = new StringBuilder();
                        foreach (var c in gc.garbagePrefab.GetComponentsInChildren<Component>(true)) sb.Append(c.GetIl2CppType().Name).Append(' ');
                        Plugin.L.LogInfo($"TS2 bag prefab '{gc.garbagePrefab.name}' of '{gc.name}': {sb}");
                        break;
                    }
                foreach (var l in Resources.FindObjectsOfTypeAll<LampController>())
                    Plugin.L.LogInfo($"TS2 lamp '{l.name}' scene '{l.gameObject.scene.name}' light {l.lightSource?.intensity} range {l.lightSource?.range} item data {l.GetComponent<Item>()?.toolData?.id}");
                return;
            case 4:
                Plugin.L.LogInfo("BOT done, quitting");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
        }
    }

    static void Log(string when, string name, GameObject go)
    {
        var sb = new StringBuilder();
        int n = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r.enabled && r.gameObject.activeInHierarchy) { n++; sb.Append($"{Path(r.transform, go.transform)}[{r.gameObject.layer}]/{r.sharedMaterial?.name}; "); }
        var s = go.GetComponentInChildren<Skinable>(true);
        Plugin.L.LogInfo($"TS2 {when} '{name}' current {s?.CurrentSkin?.id} inst '{s?.InstantiatedSkin?.name}' drawn {n}: {sb}");
    }

    static string Path(Transform t, Transform top)
    {
        var p = t.name;
        for (t = t.parent; t != null && t != top; t = t.parent) p = t.name + "/" + p;
        return p;
    }
}
