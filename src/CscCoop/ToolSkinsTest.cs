using System;
using System.Text;
using CoopCore;
using UnityEngine;

namespace CscCoop;

// toolskins suite (tools/run-pair.ps1 -Test toolskins -QuitAfter 80): each player's tools wear their own skins and
// level on the other's screen. The client chooses the boxing gloves for the akimbo sponge (7), the cardboard bucket (9)
// and the black hole trash bag (29), and holds the akimbo sponge (tool 19, two sponges); the host chooses the Bogo
// bucket (14). Each puts its bucket down and makes a trash bag. Then each logs the skin on the other's bucket and bag
// and on its own, and the host what the client's figure holds (skin, parts in the left hand). Screenshot from the host:
// shot_toolskins_host.png. The saves (where the game keeps the choice) are restored by run-pair.
public partial class Bot
{
    PortableItem tsBucket;
    GameObject tsBag;
    PlayerFigure tsLook;   // the host keeps looking at the client's figure until the shot

    static string SkinsOn(GameObject go)
    {
        if (go == null) return "none";
        var sb = new StringBuilder();
        foreach (var s in go.GetComponentsInChildren<Skinable>(true)) sb.Append($"{s.skinType}={(s.CurrentSkin != null ? s.CurrentSkin.id : -1)} ");
        return sb.ToString().Trim();
    }

    static GameObject OthersBag()
    {
        foreach (var b in UnityEngine.Object.FindObjectsOfType<GarbageBagConnector>()) if (b != null && b.gameObject.activeInHierarchy && Dyn.IsPuppet(b.gameObject)) return b.gameObject;
        return null;
    }

    void ToolSkinsTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        if (!host && step >= 2 && step <= 30)
        {
            var hg = CoopRunner.Instance.GhostOf(0);
            if (hg != null)
            {
                var d = hg.Root.transform.position - w.Player.transform.position; d.y = 0;
                if (d.sqrMagnitude > 0.01f && Quaternion.Angle(w.Player.transform.rotation, Quaternion.LookRotation(d)) > 3)
                {
                    // the game's own turn (the controller keeps its own yaw: setting the transform didn't hold)
                    var to = new GameObject("ToolSkinsTurn").transform;
                    to.SetPositionAndRotation(w.Player.transform.position, Quaternion.LookRotation(d));
                    w.Player.SetPositionAndRotation(to);
                    UnityEngine.Object.Destroy(to.gameObject);
                }
            }
        }
        if (host && tsLook != null && step >= 2 && step <= 30)
        {
            var look = new GameObject("ToolSkinsLook").transform; look.position = tsLook.Root.transform.position + Vector3.up * 1.05f;
            w.Player.LookAt(look); UnityEngine.Object.Destroy(look.gameObject);
        }
        if (host) TickShots();
        var sc = SkinableController.Instance;
        switch (step)
        {
            case 0 when t > 3:
                foreach (int id in host ? new[] { 14 } : new[] { 7, 9, 29 })
                {
                    var d = sc.GetSkinByID(id);
                    try { sc.ChangeToCustomSkin(d); } catch (Exception e) { Plugin.L.LogInfo($"TOOLSKINS skin {id}: {e.Message}"); }
                }
                Plugin.L.LogInfo($"TOOLSKINS {(host ? "host" : "client")} chose '{ToolSkins.Mine()}'");
                if (!host) ToolLook.ForceTool = 19;   // the akimbo sponge
                try { InventoryController.Instance.Equip(ItemCategory.bucket, 0); } catch (Exception e) { Plugin.L.LogInfo("TOOLSKINS equip: " + e.Message); }
                step = 1; break;
            case 1 when t > 7:
                foreach (var pi in UnityEngine.Object.FindObjectsOfType<PortableItem>())
                    if (pi != null && !pi.name.Contains("(other player)") && pi.GetComponentInChildren<LiquidContainer>(true) != null) { tsBucket = pi; break; }
                var fwd = w.Player.transform.forward; var right = w.Player.transform.right;
                if (tsBucket != null)
                {
                    if (w.Grabber.GrabbedRb != null) w.Grabber.TryRelease();
                    tsBucket.transform.position = w.Player.transform.position + fwd * 1.5f + right * (host ? 0.6f : -0.6f) + Vector3.up * 0.3f;
                    var g = tsBucket.GetComponent<GrabableItemController>() ?? tsBucket.GetComponentInParent<GrabableItemController>();
                    if (g != null && g.Rb != null) { g.Rb.isKinematic = false; g.Rb.velocity = Vector3.zero; }
                }
                tsBag = MakeBag(w.Player.transform.position + fwd * 2.2f + right * (host ? 0.6f : -0.6f) + Vector3.up * 0.5f);
                Plugin.L.LogInfo($"TOOLSKINS {(host ? "host" : "client")} put down {(tsBucket != null ? tsBucket.name : "NO BUCKET")} and {(tsBag != null ? "a bag" : "NO BAG")}");
                step = 2; break;
            case 2 when t > 40:
                // the host goes to a free spot in the first job's yard next to the client (from elsewhere a car or the fence
                // is in the way) and looks at its hands; the client keeps turning to the host (above)
                if (host)
                {
                    var g = CoopRunner.Instance.GhostOf(1);
                    if (g != null) { PutAt(w, g.Root.transform.position + new Vector3(0.5f, 0, -0.9f)); tsLook = g; }
                }
                step = 20; break;
            case 20 when t > 50:
                // skins load in the background (a first load takes 10-30 s)
                var other = OtherPlayersTool();
                Plugin.L.LogInfo($"TOOLSKINS {(host ? "host" : "client")} mine: bucket [{SkinsOn(tsBucket?.gameObject)}] bag [{SkinsOn(tsBag)}]; other's: bucket '{other?.name}' [{SkinsOn(other?.gameObject)}] bag [{SkinsOn(OthersBag())}]; other's choice '{ToolSkins.ChosenBy((byte)(host ? 1 : 0))}'");
                if (host && tsLook != null)
                    Plugin.L.LogInfo($"TOOLSKINS host sees the client hold '{tsLook.Tool.SourceName}' skin {tsLook.Tool.SkinShown} renderers {tsLook.Tool.Renderers} left hand {tsLook.Tool.HasLeft}");
                step = 3; break;
            case 3 when t > 51:
                step = 30; break;
            case 30 when t > 54:
                // pictures from a camera of our own (FigureTest.Shot: the yard is cramped, and the bodies' yaw lags)
                if (host && tsLook != null) { Shot("toolskins_front", tsLook, Vector3.forward, 1.1f); Shot("toolskins_side", tsLook, new Vector3(0.8f, 0, 0.6f), 1.1f); }
                if (host)
                {
                    var cam = Camera.main;
                    var vp = cam != null && tsLook != null ? cam.WorldToViewportPoint(tsLook.Tool.Centre) : Vector3.zero;
                    int vis = 0; if (tsLook != null) foreach (var r in tsLook.Root.GetComponentsInChildren<Renderer>()) if (r.isVisible) vis++;
                    if (tsLook != null)
                        foreach (var r in tsLook.Root.GetComponentsInChildren<MeshRenderer>())
                        {
                            var m = r.sharedMaterial;
                            if (m != null && m.shader != null)
                            {
                                var sb = new StringBuilder();
                                for (int i = 0; i < m.shader.GetPropertyCount(); i++)
                                {
                                    var pn = m.shader.GetPropertyName(i); var pt = m.shader.GetPropertyType(i);
                                    sb.Append(pn).Append('=');
                                    if (pt == UnityEngine.Rendering.ShaderPropertyType.Float || pt == UnityEngine.Rendering.ShaderPropertyType.Range) sb.Append(m.GetFloat(pn).ToString("F2"));
                                    else if (pt == UnityEngine.Rendering.ShaderPropertyType.Color || pt == UnityEngine.Rendering.ShaderPropertyType.Vector) sb.Append(m.GetVector(pn).ToString("F2"));
                                    else sb.Append(m.GetTexture(pn)?.name);
                                    sb.Append("; ");
                                }
                                Plugin.L.LogInfo($"TOOLSKINS part '{r.name}' material queue {m.renderQueue} passes {m.shader.passCount} keywords [{string.Join(",", m.shaderKeywords)}] {sb}");
                            }
                            Plugin.L.LogInfo($"TOOLSKINS part '{r.name}' at {P3(r.transform.position)} size {P3(r.bounds.size)} visible {r.isVisible} layer {r.gameObject.layer} material '{r.sharedMaterial?.name}' shader '{r.sharedMaterial?.shader?.name}' colour {(r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor").ToString() : "-")}");
                        }
                    if (tsLook != null) Plugin.L.LogInfo($"TOOLSKINS host: client figure yaw {tsLook.Root.transform.eulerAngles.y:F0}");
                    Plugin.L.LogInfo($"TOOLSKINS host shot from {P3(w.Player.transform.position)} camera '{cam?.name}' at {P3(cam != null ? cam.transform.position : Vector3.zero)} looking {P3(cam != null ? cam.transform.forward : Vector3.zero)}; tool on screen at {vp.x:F2},{vp.y:F2} depth {vp.z:F2}; figure renderers visible {vis}");
                }
                if (host) { string dir = Env.Get("CSCCOOP_SHOT_DIR"); if (dir != "") ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_toolskins_host.png")); }
                step = 4; break;
            case 4 when t > 58:
                if (!host) Plugin.L.LogInfo($"TOOLSKINS client yaw {w.Player.transform.eulerAngles.y:F0}");
                ToolLook.ForceTool = short.MinValue;
                foreach (var ty in new[] { SkinType.dualSponge, SkinType.bucket, SkinType.garbageBag }) try { sc.ChangeToDefaultSkin(ty); } catch { }
                Plugin.L.LogInfo("TOOLSKINS dump done");
                step = 5; break;
        }
    }
}
