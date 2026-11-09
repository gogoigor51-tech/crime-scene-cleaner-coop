using System;
using System.IO;
using System.Linq;
using CoopCore;
using CscCoop.Skins;
using UnityEngine;

namespace CscCoop;

// skins suite (tools/run-pair.ps1 -Test skins): the host chooses GDGreg, the client the Senator (the skins window's
// Choose); each must see the other as that person (its figure wearing the skin, the mod's own mesh hidden). Then the
// client goes back to no skin: the host must see the mod's figure again. The choice is put back to none at the end
// (it is saved in the test copy's config). The client also makes its eyes blue (#2E6FD0): the host must see them so. Screenshot from the host: shot_skins_host.png.
public partial class Bot
{
    static Person ByName(string display) => SkinCatalog.People?.FirstOrDefault(p => p.Display == display);

    void SkinsLog(Session ses, string label)
    {
        foreach (var rp in ses.Players.Values)
        {
            var g = CoopRunner.Instance.GhostOf(rp.Number);
            Plugin.L.LogInfo($"SKINS {label}: player {rp.Number} figure {(g != null)} skin '{(g?.Skin != null ? g.Skin.Source : "")}' own mesh {(g != null && g.OwnMeshShown)} worn '{SkinSync.WornBy(rp.Number)}'; mine '{Plugin.Skin.Value}' eyes '{EyesOf(g)}' my eyes '{Plugin.Iris.Value}'");
        }
    }

    // The colour of the eyes on a figure's skin (their material is named after it), "" without.
    static string EyesOf(PlayerFigure g)
    {
        if (g?.Skin == null) return "";
        foreach (var mr in g.Skin.Root.GetComponentsInChildren<MeshRenderer>(true))
            if (mr.name == "CoopEyeball" && mr.sharedMaterial != null) return mr.sharedMaterial.name.Replace("CoopEye ", "");
        return "";
    }

    void SkinsTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        switch (step)
        {
            case 0 when t > 3:
                SkinCatalog.Load();
                step = 1; break;
            case 1 when t > 6 && SkinCatalog.Ready:
                var me = ByName(host ? "GDGreg" : "Senator");
                Plugin.L.LogInfo($"SKINS {(host ? "host" : "client")} chooses '{me?.Display}' ({me?.Key})");
                SkinsWindow.Instance.Choose(me);
                // the client's eyes blue (the colour ring), the host's left as they are
                SkinsWindow.Instance.SetIris(Eyeballs.Parse(host ? Eyeballs.DefaultHex : "#2E6FD0"));
                step = 2; break;
            case 2 when t > 24:
                SkinsLog(ses, "chosen");
                if (host)
                {
                    var g = CoopRunner.Instance.GhostOf(1);
                    if (g != null)
                    {
                        var at = g.Root.transform.position;
                        PutAt(w, at + new Vector3(0, 0, -3f));   // 3 m away, to see the whole figure
                        var look = new GameObject("SkinsLook").transform; look.position = at + Vector3.up * 1.0f; w.Player.LookAt(look);
                    }
                }
                step = 3; break;
            case 3 when t > 26:
                if (host) { string dir = Env.Get("CSCCOOP_SHOT_DIR"); if (dir != "") ScreenCapture.CaptureScreenshot(Path.Combine(dir, "shot_skins_host.png")); }
                step = 4; break;
            case 4 when t > 27:
                if (!host) SkinsWindow.Instance.Choose(null);
                step = 5; break;
            case 5 when t > 33:
                SkinsLog(ses, "cleared");
                SkinsWindow.Instance.Choose(null);
                SkinsWindow.Instance.SetIris(Eyeballs.Parse(Eyeballs.DefaultHex));
                Plugin.L.LogInfo("SKINS dump done");
                step = 6; break;
        }
    }
}
