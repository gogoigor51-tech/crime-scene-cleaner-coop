using System.Collections.Generic;
using System.IO;
using CscCoop.Skins;
using UnityEngine;

namespace CscCoop;

// Players' skins: each player's choice (Plugin.Skin, a person's key "levelN#pathID": every player's game has the same
// files) goes to everyone as global state per player (Ev.Skin), so a player who joins later gets it too, whatever scene
// they are in. Each machine loads the others' people in the background (SkinLoader, textures at half size) and puts
// them on their figures (PlayerFigure.SetSkin); "" brings the mod's own figure back.
public static class SkinSync
{
    static readonly Dictionary<byte, string> wanted = new(), worn = new();
    // each player's eye colour ("#RRGGBB", Plugin.Iris), sent after the skin's key; what each figure shows now
    static readonly Dictionary<byte, string> irisWanted = new(), irisShown = new();
    static readonly HashSet<byte> loading = new();
    static SkinLoader loader;
    static string sent, sentIris;
    static float sentAt;
    static byte sentAs = 255;
    public static int Applied;

    const uint StateBase = 0x30000;

    public static void OnChosen() { sent = null; sentAt = 0; }   // sent again on the next tick

    // From CoopRunner every frame, before the figures are drawn.
    public static void Tick(CoopRunner run)
    {
        var s = run.Session;
        if (s == null) { sent = null; return; }
        bool irisChanged = sentIris != Plugin.Iris.Value && Env.Get("CSCCOOP_MUTANT") != "iris-local";
        if (s.Me != CoopCore.Session.NoPlayer && (sent != Plugin.Skin.Value || sentAs != s.Me || irisChanged) && Env.Get("CSCCOOP_MUTANT") != "skin-local"
            && Time.unscaledTime - sentAt > 0.3f)
        {
            sent = Plugin.Skin.Value; sentAs = s.Me; sentIris = Plugin.Iris.Value; sentAt = Time.unscaledTime;
            string key = sent, iris = Env.Get("CSCCOOP_MUTANT") == "iris-local" ? "" : sentIris;
            WorldEvents.SendGlobal(Ev.Skin, w => { w.Write(key); w.Write(iris); }, StateBase + s.Me);
            Plugin.L.LogInfo($"COOP skin sent: '{key}' eyes {iris}");
        }
        loader ??= new SkinLoader(1, Next, Loaded);
        loader.Tick();
    }

    public static void OnSkin(byte from, BinaryReader r)
    {
        wanted[from] = r.ReadString();
        // older versions sent the key alone
        irisWanted[from] = r.BaseStream.Position < r.BaseStream.Length ? r.ReadString() : "";
        Plugin.L.LogInfo($"COOP player {from} wears '{wanted[from]}' eyes {irisWanted[from]}");
    }

    // Called for each figure drawn: takes the skin off when its player chose none.
    public static void Dress(byte n, PlayerFigure g)
    {
        wanted.TryGetValue(n, out var want); want ??= "";
        worn.TryGetValue(n, out var have); have ??= "";
        if (want == have && want != "" && !loading.Contains(n)) Recolor(n, g);
        if (want == have || loading.Contains(n)) return;
        if (want == "")
        {
            g.SetSkin(null); irisShown.Remove(n);
            worn[n] = "";
            Plugin.L.LogInfo($"COOP player {n}: the mod's figure again");
        }
    }

    // The next player whose skin isn't on yet.
    static Person Next()
    {
        foreach (var kv in wanted)
        {
            worn.TryGetValue(kv.Key, out var have);
            if (kv.Value == "" || kv.Value == (have ?? "") || loading.Contains(kv.Key)) continue;
            if (CoopRunner.Instance?.GhostOf(kv.Key) == null) continue;   // not drawn here (another scene): later
            var p = Parse(kv.Value);
            if (p == null) { worn[kv.Key] = kv.Value; continue; }
            p.Name = $"player {kv.Key}";
            loading.Add(kv.Key);
            pendingFor[p.Key] = (kv.Key, kv.Value);
            return p;
        }
        return null;
    }
    static readonly Dictionary<string, (byte n, string key)> pendingFor = new();

    static void Loaded(Person p, GameObject go, Transform[] hum)
    {
        if (!pendingFor.TryGetValue(p.Key, out var who)) { SkinFiles.Destroy(go); return; }
        pendingFor.Remove(p.Key);
        loading.Remove(who.n);
        worn[who.n] = who.key;   // also when it failed: not tried again until the player chooses another
        var g = CoopRunner.Instance?.GhostOf(who.n);
        wanted.TryGetValue(who.n, out var still);
        if (go == null || g == null || still != who.key) { SkinFiles.Destroy(go); if (go == null) Plugin.L.LogWarning($"COOP player {who.n}'s skin '{who.key}' couldn't be made"); return; }
        g.SetSkin(SkinModel.FromBuilt(go, hum, who.key, g.Root.transform));
        irisShown.Remove(who.n);
        Recolor(who.n, g);
        Applied++;
        Plugin.L.LogInfo($"COOP player {who.n} now shown as '{who.key}'");
    }

    // The player's eye colour on their skin, when it changed.
    static void Recolor(byte n, PlayerFigure g)
    {
        irisWanted.TryGetValue(n, out var want); want ??= "";
        irisShown.TryGetValue(n, out var have);
        if (want == have || g.Skin == null) return;
        Eyeballs.SetColor(g.Skin.Root, Eyeballs.Parse(want == "" ? Eyeballs.DefaultHex : want));
        irisShown[n] = want;
        Plugin.L.LogInfo($"COOP player {n}'s eyes now {(want == "" ? Eyeballs.DefaultHex : want)}");
    }

    public static string IrisShownOn(byte n) => irisShown.TryGetValue(n, out var k) ? k : "";

    // "level41#12323" -> the person in that scene file.
    static Person Parse(string key)
    {
        int i = key.IndexOf('#');
        if (i <= 0 || !long.TryParse(key.Substring(i + 1), out long root)) return null;
        var file = key.Substring(0, i);
        if (!File.Exists(Path.Combine(SkinFiles.DataDir, file))) return null;
        return new Person { File = file, Root = root };
    }

    public static string WornBy(byte n) => worn.TryGetValue(n, out var k) ? k : "";
}
