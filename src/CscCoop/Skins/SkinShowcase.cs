using System;
using CscCoop.Skins;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CscCoop;

// The skins window's big picture of the chosen skin: the person (textures at half size) on a PlayerFigure standing
// in its own little studio far below the level, rendered every frame while the window is open, turned by Yaw and
// brought closer to the face by Zoom (0 = the whole body, 1 = the head). Kept between openings; another choice
// replaces it. Layer 31 like the cell pictures, 100 m away from their studio so neither camera sees the other.
public static class SkinShowcase
{
    const int Layer = 31;
    public static float Yaw = 180, Zoom;
    public static bool Loading => loader != null && (loader.Busy || want != null);
    public static RenderTexture Texture { get; private set; }

    static Transform studio;
    static Camera cam;
    static PlayerFigure fig;
    static string shown = "\0", wantKey;
    static Person want;
    static SkinLoader loader;

    // Called by the window every frame with the chosen person (null: the mod's figure).
    public static void Show(Person p)
    {
        string key = p?.Key ?? "";
        if (key == shown) { if (wantKey != key) { loader?.Stop(); want = null; wantKey = key; } return; }
        if (key == wantKey) return;
        wantKey = key;
        Setup();
        loader?.Stop();
        if (p == null) { Put(null, null, ""); return; }
        want = p;
        loader = new SkinLoader(1, () => { var w = want; want = null; return w; }, (person, go, hum) =>
        {
            if (person.Key != wantKey) { SkinFiles.Destroy(go); return; }
            Put(go, hum, person.Display);
        });
    }

    static void Put(GameObject go, Transform[] hum, string name)
    {
        if (fig != null) { fig.SetSkin(null); fig.Destroy(); }
        fig = new PlayerFigure(41, new Color(0.85f, 0.62f, 0.12f));
        if (go != null) { fig.SetSkin(SkinModel.FromBuilt(go, hum, name, fig.Root.transform)); Eyeballs.SetColor(go, iris ?? Eyeballs.Parse(Plugin.Iris.Value)); }
        foreach (var t in fig.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        shown = wantKey;
    }

    public static void Tick()
    {
        loader?.Tick();
        if (fig == null || cam == null) return;
        fig.Pose(studio.position, Yaw, 0, PlayerFigure.Carry.None, CoopCore.PlayerPose.ToolNone, Vector3.zero, Quaternion.identity, 1, false, 0.02f);
        // whole body at 3.4 m, the head at 0.7 m
        float dist = Mathf.Lerp(3.4f, 0.7f, Zoom), height = Mathf.Lerp(0.95f, 1.62f, Zoom);
        cam.transform.position = studio.position + new Vector3(0, height, -dist);
        cam.transform.rotation = Quaternion.identity;
        cam.Render();
    }

    // The colour being picked (the window), shown at once.
    static Color32? iris;
    public static void SetIris(Color32 c) { iris = c; if (fig?.Skin != null) Eyeballs.SetColor(fig.Skin.Root, c); }

    public static void Hide() { loader?.Stop(); loader = null; want = null; wantKey = shown; }

    static void Setup()
    {
        if (studio != null) return;
        studio = new GameObject("CoopSkinShowcase").transform;
        Object.DontDestroyOnLoad(studio.gameObject);
        studio.position = new Vector3(0, -3000, 100);
        Texture = new RenderTexture(512, 704, 24) { name = "CoopSkinShowcase", hideFlags = HideFlags.DontUnloadUnusedAsset };
        cam = new GameObject("CoopSkinShowcaseCamera").AddComponent<Camera>();
        cam.transform.SetParent(studio, false);
        cam.fieldOfView = 34; cam.nearClipPlane = 0.05f; cam.farClipPlane = 8;
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1);
        cam.targetTexture = Texture;
        cam.enabled = false;
        foreach (var (pos, intensity) in new[] { (new Vector3(-1.2f, 2.2f, -2f), 3.5f), (new Vector3(1.5f, 1.4f, -1.5f), 2f), (new Vector3(0, 2f, 1.5f), 1.5f) })
        {
            var l = new GameObject("CoopSkinShowcaseLight").AddComponent<Light>();
            l.transform.SetParent(studio, false);
            l.transform.localPosition = pos;
            l.type = LightType.Point; l.range = 6; l.intensity = intensity; l.cullingMask = 1 << Layer;
        }
    }
}
