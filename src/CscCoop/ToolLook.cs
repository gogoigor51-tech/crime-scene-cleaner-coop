using System;
using System.Collections.Generic;
using CoopCore;
using HarmonyLib;
using UnityEngine;

namespace CscCoop;

// Other players' tools (mop, sponge, axe, power washer lance...).
//  - The owner sends in its pose which tool it holds (the game's ToolData id, PlayerPose.ToolLance for the
//    lance, which has none), where its first-person view holds it relative to the player, and whether it is
//    being used right now (CleaningItem / PowerWasherLance cleaning, an axe hit in the last 0.4 s).
//  - The others show a copy of that tool's look: the enabled MeshRenderers of the tool's prefab
//    (ToolData.ItemPrefab; for the lance the local player's own lance), meshes and materials shared, no
//    scripts, colliders or particles, on the default layer (first-person models are on layer 8, which only
//    the hands camera draws).
//  - Tool probe (2026-10-27, Dealer's Apartment): every held tool is an instance of its prefab under
//    ObjAttachRotation_R/L of the first-person arms; URP Lit materials.
//  - Skins and levels (owner's request): each level of a tool is its own tool (Basic Sponge, Sponge II, Akimbo Sponge:
//    two sponges), so the id gives the level. The akimbo sponge's second sponge (DualSponge.secondSponge) goes to a
//    second copy in the left hand (the right hand's hold mirrored), else both sponges sat in the right hand as one.
//    The player's skin for the tool's kind (ToolSkins) is put on the copy: the skin's model where the game puts it,
//    the tool's default parts it replaces hidden, its decals on their parts.
[HarmonyPatch]
public sealed class ToolLook
{
    readonly GameObject root, left;
    readonly byte player;
    short shown = PlayerPose.ToolNone;
    int shownSkin = -1;
    public float Length;      // longest side of the copy's bounds, metres
    public Vector3 Centre;    // world centre of the copy's bounds after Place
    Vector3 centreLocal;
    public Vector3 CentreLocal => centreLocal;   // bounds centre in the tool's own space (the root is the grip)
    public Vector3 LeftCentreLocal { get; private set; }
    public bool HasLeft => leftParts > 0;
    int leftParts;
    public int Renderers;
    public string SourceName = "";
    public int SkinShown => shownSkin;
    public int Drawn { get { int n = 0; foreach (var r in root.GetComponentsInChildren<MeshRenderer>()) if (r.enabled) n++; return n; } }   // parts drawn now (not the ones shown only while used)
    // water stream and the like: shown only while the tool is used. "water" as a word of its own: the water pistols'
    // own meshes (skin 105, WaterPistol...) were hidden until the player cleaned (owner's test)
    readonly List<GameObject> useParts = new();
    static readonly System.Text.RegularExpressions.Regex UsePart = new(@"water|stream|beam|spray|splash|vfx|particle|muzzle", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public ToolLook(Transform figure, byte player = 0)
    {
        this.player = player;
        root = new GameObject("tool");
        root.transform.SetParent(figure, false);
        root.SetActive(false);
        left = new GameObject("tool (left hand)");
        left.transform.SetParent(figure, false);
        left.SetActive(false);
    }

    // Builds the copy for this tool (in its player's skin) if needed; false if this machine can't find what it looks like.
    public bool Show(short tool)
    {
        var src = Source(tool);
        if (!skinables.TryGetValue(tool, out var sk) && src != null) skinables[tool] = sk = src.GetComponentInChildren<Skinable>(true);
        var data = sk != null ? ToolSkins.Data(ToolSkins.SkinOf(player, sk.skinType)) : null;
        GameObject model = null; bool waiting = false;
        if (data != null) model = ToolSkins.Model(data, out waiting);
        int skin = data == null ? -1 : waiting ? -2 : data.id;   // -2: still loading, shown without it for now
        if (tool != shown || skin != shownSkin)
        {
            shown = tool; shownSkin = skin;
            foreach (var r in new[] { root, left })
                for (int i = r.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(r.transform.GetChild(i).gameObject);
            Renderers = 0; Length = 0; leftParts = 0; useParts.Clear(); names.Clear();
            SourceName = src != null ? src.name : "";
            if (src != null) Build(src, sk, skin >= 0 ? data : null, model);
            // A tool this machine's copy shows nothing of (friend's test of 0.4.1: the host's MopTool came out with 0
            // renderers on the client, which only has the WeakMop, so the friend saw empty hands): show the nearest
            // tool of the same kind that does draw (mop, sponge, detergent, axe...).
            if (Renderers == 0 && src != null)
                foreach (var alt in SameKind(src.name))
                {
                    Build(alt, null, null, null);
                    if (Renderers > 0) { SourceName = alt.name + " (in place of " + src.name + ")"; break; }
                }
            string what = skin >= 0 ? $"{skin} '{data.DisplayName}'" : skin == -2 ? $"{data.id} loading" : "none";
            Plugin.L.LogInfo($"COOP tool look {tool}: '{SourceName}' skin {what}, {Renderers} renderers ({leftParts} in the left hand), {Length:F2} m ({string.Join(", ", names)}; only while used: {useParts.Count})");
        }
        if (Renderers == 0) return false;
        if (!root.activeSelf) root.SetActive(true);
        if (left.activeSelf != HasLeft) left.SetActive(HasLeft);
        return true;
    }

    public void Hide() { if (root.activeSelf) root.SetActive(false); if (left.activeSelf) left.SetActive(false); }

    public void SetUsing(bool on) { foreach (var p in useParts) if (p != null && p.activeSelf != on) p.SetActive(on); }
    readonly List<string> names = new();

    public void Place(Vector3 pos, Quaternion rot, float scale) => Place(root, pos, rot, scale);
    public void PlaceLeft(Vector3 pos, Quaternion rot, float scale) => Place(left, pos, rot, scale);

    void Place(GameObject r, Vector3 pos, Quaternion rot, float scale)
    {
        r.transform.SetPositionAndRotation(pos, rot);
        var parent = r.transform.parent.lossyScale.x;
        r.transform.localScale = Vector3.one * ((scale > 0.01f && scale < 10 ? scale : 1) / Mathf.Max(parent, 0.01f));
        if (r == root) Centre = root.transform.TransformPoint(centreLocal);
    }

    Bounds bounds, leftBounds;
    bool any, leftAny;

    // The tool's drawn parts (without those its skin replaces), then the skin's model and decals. The second sponge's
    // parts, and whatever the skin puts on it, go to the left-hand copy, placed from the second sponge.
    void Build(GameObject src, Skinable sk, SkinData skin, GameObject model)
    {
        var s = src.transform;
        Transform second = null;
        try { var ds = src.GetComponent<DualSponge>(); second = ds != null ? ds.secondSponge : null; } catch { second = null; }
        var hidden = new HashSet<IntPtr>();
        var hiddenTrees = new List<Transform>();
        if (skin != null && sk != null)
        {
            if (sk.defaultMeshRenderers != null) foreach (var r in sk.defaultMeshRenderers) if (r != null) hidden.Add(r.Pointer);
            if (sk.defaultSkinElements != null) foreach (var g in sk.defaultSkinElements) if (g != null) hiddenTrees.Add(g.transform);
        }
        bool Hidden(Renderer r)
        {
            if (hidden.Contains(r.Pointer)) return true;
            foreach (var h in hiddenTrees) if (Under(r.transform, h)) return true;
            return false;
        }
        any = leftAny = false;
        bounds = leftBounds = new Bounds();
        Copy(s, Matrix4x4.identity, false, Hidden, second, s);
        if (second != null) Copy(second, Matrix4x4.identity, true, Hidden, null, s);
        if (skin != null)
        {
            var parent = sk.skinModelParent != null ? sk.skinModelParent : sk.transform;
            if (model != null)
            {
                bool l = second != null && Under(parent, second);
                var m0 = Rel(l ? second : s, parent) * Matrix4x4.TRS(skin.ModelPositionOffset, Quaternion.Euler(skin.ModelRotationOffset), model.transform.localScale);
                Copy(model.transform, m0, l, null, null, model.transform);
            }
            if (skin.SkinDecals != null)
                foreach (var dc in skin.SkinDecals)
                {
                    if (dc?.decal == null) continue;
                    foreach (var part in src.GetComponentsInChildren<SkinableDecal>(true))
                    {
                        if (part.partName != dc.name) continue;
                        bool l = second != null && Under(part.transform, second);
                        var d = dc.decal.transform;
                        var m0 = Rel(l ? second : s, part.transform) * Matrix4x4.TRS(d.localPosition, d.localRotation, d.localScale);
                        Copy(d, m0, l, null, null, d);
                    }
                }
        }
        Length = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        centreLocal = bounds.center;
        LeftCentreLocal = leftBounds.center;
    }

    // Where t is, in the space of origin.
    static Matrix4x4 Rel(Transform origin, Transform t) => origin.worldToLocalMatrix * t.localToWorldMatrix;

    static bool Under(Transform t, Transform top)
    {
        for (; t != null; t = t.parent) if (t == top) return true;
        return false;
    }

    // The drawn renderers under top (skipping those under skip, and hidden ones), each a mesh copy placed at m0 times its
    // place under top, into the right- or left-hand copy.
    void Copy(Transform top, Matrix4x4 m0, bool toLeft, Func<Renderer, bool> hidden, Transform skip, Transform activeTop)
    {
        var parts = new List<(Renderer r, Mesh mesh)>();
        foreach (var mr in top.GetComponentsInChildren<MeshRenderer>(true))
        {
            var f = mr != null ? mr.GetComponent<MeshFilter>() : null;
            if (f != null && f.sharedMesh != null) parts.Add((mr, f.sharedMesh));
        }
        foreach (var sr in top.GetComponentsInChildren<SkinnedMeshRenderer>(true))   // some upgraded tools draw skinned
            if (sr != null && sr.sharedMesh != null) parts.Add((sr, sr.sharedMesh));
        var into = toLeft ? left : root;
        var toTop = top.worldToLocalMatrix;
        foreach (var (mr, mesh) in parts)
        {
            if (mr == null || !mr.enabled || !ActiveUnder(mr.transform, activeTop)) continue;
            if (skip != null && Under(mr.transform, skip)) continue;
            if (hidden != null && hidden(mr)) continue;
            var m = m0 * toTop * mr.transform.localToWorldMatrix;
            var go = new GameObject(mr.name);
            go.transform.SetParent(into.transform, false);
            go.transform.localPosition = m.GetColumn(3);
            go.transform.localRotation = m.rotation;
            go.transform.localScale = m.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mr.sharedMaterials;
            Renderers++;
            if (toLeft) leftParts++;
            names.Add(toLeft ? mr.name + " (left)" : mr.name);
            if (UsePart.IsMatch(PathUnder(mr.transform, top))) { useParts.Add(go); go.SetActive(false); continue; }   // not in the bounds either
            // Bounds in the copy's space: the mesh's corners through the part's local transform.
            var mb = mesh.bounds;
            var pm = Matrix4x4.TRS(go.transform.localPosition, go.transform.localRotation, go.transform.localScale);
            for (int c = 0; c < 8; c++)
            {
                var p = pm.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                if (toLeft) { if (!leftAny) { leftBounds = new Bounds(p, Vector3.zero); leftAny = true; } else leftBounds.Encapsulate(p); }
                else if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
            }
        }
    }

    static string PathUnder(Transform t, Transform top)
    {
        var p = t.name;
        for (t = t.parent; t != null && t != top; t = t.parent) p = t.name + "/" + p;
        return p;
    }

    // Active within the tool (its own root may be switched off: a lance waiting in the inventory).
    static bool ActiveUnder(Transform t, Transform top)
    {
        for (; t != null && t != top; t = t.parent) if (!t.gameObject.activeSelf) return false;
        return true;
    }

    static readonly Dictionary<short, GameObject> sources = new();
    static readonly Dictionary<short, Skinable> skinables = new();   // the tool prefab's (none: null)

    static GameObject Source(short tool)
    {
        if (sources.TryGetValue(tool, out var go) && go != null) return go;
        go = null;
        try
        {
            if (tool == PlayerPose.ToolLance)
            {
                var lance = PlayerInventory.Instance != null ? PlayerInventory.Instance.PowerWasherLance : null;
                if (lance == null) foreach (var l in Resources.FindObjectsOfTypeAll<PowerWasherLance>()) { lance = l; break; }
                go = lance != null ? lance.gameObject : null;
            }
            else foreach (var td in ToolDatas()) if (td != null && td.id == tool) { go = td.ItemPrefab; break; }
        }
        catch (Exception e) { Plugin.L.LogWarning($"COOP tool look {tool}: {e.Message}"); }
        if (go != null) sources[tool] = go;
        return go;
    }

    static readonly string[] Kinds = { "Mop", "Sponge", "Detergent", "Axe", "Garbage", "Lamp", "Ozon", "Brush", "Scraper" };

    static IEnumerable<GameObject> SameKind(string name)
    {
        string kind = null;
        foreach (var k in Kinds) if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { kind = k; break; }
        if (kind == null) yield break;
        foreach (var td in ToolDatas())
            if (td?.ItemPrefab != null && td.ItemPrefab.name != name && td.ItemPrefab.name.IndexOf(kind, StringComparison.OrdinalIgnoreCase) >= 0)
                yield return td.ItemPrefab;
    }

    static IEnumerable<ToolData> ToolDatas()
    {
        var c = PlayerInventory.Instance != null ? PlayerInventory.Instance.ItemsContainer : null;
        if (c == null || c.toolDatas == null) yield break;
        foreach (var td in c.toolDatas) yield return td;
    }

    // ---- owner side ----

    static readonly Dictionary<string, short> idByPrefab = new();
    static float lastHit = -10;
    public static short ForceTool = short.MinValue;   // tests: pretend to hold this tool
    public static bool ForceUse;

    [HarmonyPatch(typeof(AxeTool), nameof(AxeTool.ExecuteHit))] [HarmonyPostfix]
    static void AxeHit() => lastHit = Time.unscaledTime;

    // The selected tool and where it is, relative to the player transform (feet, yaw).
    public static void Sample(Transform player, ref PlayerPose pose)
    {
        pose.Tool = PlayerPose.ToolNone; pose.ToolRot = Q4.Identity;
        UsableItem u = null;
        try { var inv = InventoryController.Instance; u = inv != null ? inv.SelectedItem?.TryCast<UsableItem>() : null; }
        catch { u = null; }
        if (u != null && u.gameObject.activeInHierarchy)
        {
            pose.Tool = IdOf(u);
            if (pose.Tool != PlayerPose.ToolNone)
            {
                var t = u.transform;
                pose.ToolPos = GameWorld.V(player.InverseTransformPoint(t.position));
                pose.ToolRot = GameWorld.Q(Quaternion.Inverse(player.rotation) * t.rotation);
                pose.ToolScale = t.lossyScale.x;
                bool use = u.TryCast<CleaningItem>()?.isCleaning == true || u.TryCast<PowerWasherLance>()?.isCleaning == true
                    || u.TryCast<AxeTool>() != null && Time.unscaledTime - lastHit < 0.4f;
                if (use) pose.Flags |= PlayerPose.PoseUsing;
            }
        }
        if (ForceTool != short.MinValue)
        {
            if (pose.Tool == PlayerPose.ToolNone && ForceTool != PlayerPose.ToolNone)
            {
                // No real hold to copy: roughly where the first-person arms hold a tool (probe: mop root 0.59 m right, 0.57 m below the eyes).
                pose.ToolPos = new V3(0.32f, 1.05f, 0.35f);
                // The lance's barrel runs along its -z (figure suite picture), the axe's head along +z.
                pose.ToolRot = GameWorld.Q(Quaternion.Euler(-35, ForceTool == PlayerPose.ToolLance ? 180 : 0, 0));
                pose.ToolScale = 1;
            }
            pose.Tool = ForceTool;
        }
        if (ForceUse && pose.Tool != PlayerPose.ToolNone) pose.Flags |= PlayerPose.PoseUsing;
    }

    static short IdOf(UsableItem u)
    {
        string name = u.name.Replace("(Clone)", "").Trim();
        if (idByPrefab.TryGetValue(name, out var id)) return id;
        id = PlayerPose.ToolNone;
        foreach (var td in ToolDatas()) if (td?.ItemPrefab != null && td.ItemPrefab.name == name) { id = (short)td.id; break; }
        // The lance by its level's prefab (Basic Lance, Lance II, III); one of no tool data: the local player's lance.
        if (id == PlayerPose.ToolNone && u.TryCast<PowerWasherLance>() != null) id = PlayerPose.ToolLance;
        idByPrefab[name] = id;
        Plugin.L.LogInfo($"COOP my tool '{name}' is tool {id}");
        return id;
    }
}
