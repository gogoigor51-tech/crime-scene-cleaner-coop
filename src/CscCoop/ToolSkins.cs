using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CscCoop;

// Other players' tool skins (the game's own: a pipe for the axe, boxing gloves for the akimbo sponge, a cardboard
// bucket, furnace lamps, black hole trash bags...). Each player's choice is in their save (SkinableController, one skin
// per SkinType or none), and the game dresses every tool with the local player's choice, so a friend's bucket or lamp
// showed our skin and their held tool none.
//  - Each player sends its choice as global state per player (Ev.ToolSkins = "type:id;..."), when it changes.
//  - Held tools (ToolLook): the copy is dressed here from the skin's data, without the game's skin scripts (they act on
//    the local player's hands: a glove went into our left hand in toolskinprobe2). The skin's model is loaded through
//    the addressables (Addressables.LoadAssetAsync by its key, not the AssetReference, which the game loads itself),
//    put where the game puts it (the Skinable's skinModelParent, the skin's position and rotation offsets), the tool's
//    default parts hidden (defaultMeshRenderers, defaultSkinElements), the decals put on their parts (SkinableDecal).
//    toolskinprobe3: a first load takes 10-30 s; until then the tool shows without its skin.
//  - Placed tools and trash bags of another player (Dyn puppets): the game's Skinable on them is given that player's
//    skin (Dyn.DressSkins), checked once a second, as the game puts the local choice on every Skinable.
public static class ToolSkins
{
    static readonly Dictionary<byte, Dictionary<int, int>> chosen = new();
    static readonly Dictionary<byte, string> raw = new();
    static string sent;
    static byte sentAs = 255;
    static float checkAt;
    public static bool Applying;   // our own call to a Skinable is running
    public static int Received;
    const uint StateBase = 0x31000;

    // The local player's choice, "type:id;..." sorted; null before the game has its skin controller.
    public static string Mine()
    {
        var sc = SkinableController.Instance;
        if (sc == null || sc.SelectedSkins == null) return null;
        var l = new List<string>();
        foreach (var kv in sc.SelectedSkins) if (kv.Value != null) l.Add($"{(int)kv.Key}:{kv.Value.id}");
        l.Sort(StringComparer.Ordinal);
        return string.Join(";", l);
    }

    public static void OnChosen() { sent = null; checkAt = 0; }

    public static void Tick(CoopRunner run)
    {
        var s = run.Session;
        if (s == null) { sent = null; return; }
        if (s.Me == CoopCore.Session.NoPlayer || Time.unscaledTime < checkAt) return;
        checkAt = Time.unscaledTime + 1;
        string mine;
        try { mine = Mine(); } catch { mine = null; }
        if (mine == null || mine == sent && sentAs == s.Me || Env.Get("CSCCOOP_MUTANT") == "toolskin-local") return;
        sent = mine; sentAs = s.Me;
        WorldEvents.SendGlobal(Ev.ToolSkins, w => w.Write(mine), StateBase + s.Me);
        Plugin.L.LogInfo($"COOP tool skins sent: '{mine}'");
    }

    public static void OnSkins(byte from, BinaryReader r)
    {
        string text = r.ReadString();
        var d = new Dictionary<int, int>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(':');
            if (f.Length == 2 && int.TryParse(f[0], out int type) && int.TryParse(f[1], out int id)) d[type] = id;
        }
        chosen[from] = d; raw[from] = text;
        Received++;
        Plugin.L.LogInfo($"COOP player {from}'s tool skins: '{text}'");
    }

    // The skin a player chose for this kind of tool, -1 none (or not known yet).
    public static int SkinOf(byte player, SkinType type) => chosen.TryGetValue(player, out var d) && d.TryGetValue((int)type, out var id) ? id : -1;
    public static string ChosenBy(byte player) => raw.TryGetValue(player, out var t) ? t : "";

    static readonly Dictionary<int, SkinData> datas = new();
    public static SkinData Data(int id)
    {
        if (id < 0) return null;
        if (datas.TryGetValue(id, out var d) && d != null) return d;
        d = null;
        var sc = SkinableController.Instance;
        if (sc?.SkinDatas != null) foreach (var x in sc.SkinDatas) if (x != null && x.id == id) { d = x; break; }
        if (d != null) datas[id] = d;
        return d;
    }

    // The skin's model (a prefab asset), loaded once and kept; null while loading (waiting) or when it has none.
    static readonly Dictionary<int, AsyncOperationHandle<GameObject>> models = new();
    public static GameObject Model(SkinData d, out bool waiting)
    {
        waiting = false;
        var r = d?.SkinPrefabReference;
        if (r == null || !r.RuntimeKeyIsValid()) return null;
        if (!models.TryGetValue(d.id, out var h))
        {
            try { h = Addressables.LoadAssetAsync<GameObject>(r.RuntimeKey); }
            catch (Exception e) { Plugin.L.LogWarning($"COOP skin {d.id} '{d.DisplayName}': {e.Message}"); h = default; }
            models[d.id] = h;
            Plugin.L.LogInfo($"COOP loading tool skin {d.id} '{d.DisplayName}'");
        }
        if (!h.IsValid()) return null;
        if (!h.IsDone) { waiting = true; return null; }
        return h.Status == AsyncOperationStatus.Succeeded ? h.Result : null;
    }
}
