using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CscCoop.Skins;

// Eyes for the opened eyelids (EyeOpener). The game's people mostly have no usable eyeballs: no eye mesh at all, or
// one textured with the body's skin or left untextured (they were never meant to be seen). So each eye gets a small
// sphere made here, with a painted eye (white, iris, pupil), placed at the eye bone in bind pose and hung on the
// head bone, so it turns with the head; the game's own eye meshes are hidden. Made from nothing of the game's.
public static class Eyeballs
{
    static Mesh sphere;
    static readonly System.Collections.Generic.Dictionary<int, Material> materials = new();
    public const string DefaultHex = "#5C5530";   // the brown-green the eyes had before the colour could be chosen

    public static int Add(GameObject copy, Transform head)
    {
        if (!EyeOpener.Enabled || head == null) return 0;
        Transform l = null, r = null;
        // Some people hold a second rig with the same names, not in bind pose: the eyes nearest the (skinned) head.
        foreach (var t in copy.GetComponentsInChildren<Transform>(true))
        {
            float d = Vector3.Distance(t.position, head.position);
            if ((t.name == "CC_Base_L_Eye" || t.name == "eye_l") && (l == null || d < Vector3.Distance(l.position, head.position))) l = t;
            if ((t.name == "CC_Base_R_Eye" || t.name == "eye_r") && (r == null || d < Vector3.Distance(r.position, head.position))) r = t;
        }
        if (l == null || r == null) return 0;
        float ipd = Vector3.Distance(l.position, r.position);
        if (ipd < 0.02f || ipd > 0.2f) return 0;
        foreach (var smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.name.StartsWith("CC_Base_Eye", StringComparison.Ordinal)) smr.enabled = false;
        float radius = ipd * 0.2f;
        // the eyes look where the face does: the copy stands upright facing +z, but some people bow their head
        var right = (r.position - l.position).normalized;
        var up = EyeOpener.BestAxis(new[] { head.right, head.up, head.forward }, Vector3.up);
        up = (up - right * Vector3.Dot(up, right)).normalized;
        var fwd = Vector3.Cross(right, up);
        if (Vector3.Dot(fwd, Vector3.forward) < 0) fwd = -fwd;
        var look = Quaternion.LookRotation(fwd, up);
        foreach (var eye in new[] { l, r })
        {
            var go = new GameObject("CoopEyeball");
            go.layer = copy.layer;
            go.transform.SetParent(head, true);
            // the copy is placed upright facing +z at this point (SkinModel): the eye looks along +z
            go.transform.position = eye.position;
            go.transform.rotation = look;
            go.transform.localScale = Vector3.one * radius / Mathf.Max(1e-6f, head.lossyScale.x);
            go.AddComponent<MeshFilter>().sharedMesh = Sphere();
            go.AddComponent<MeshRenderer>().sharedMaterial = Material(Parse(DefaultHex));
        }
        return 2;
    }

    // A unit sphere, the front (+z) mapped flat onto the eye picture.
    static Mesh Sphere()
    {
        if (sphere != null) return sphere;
        const int Rings = 16, Segs = 24;
        var v = new Vector3[(Rings + 1) * (Segs + 1)]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length];
        for (int i = 0; i <= Rings; i++)
            for (int j = 0; j <= Segs; j++)
            {
                float th = Mathf.PI * i / Rings, ph = 2 * Mathf.PI * j / Segs;
                var p = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th));   // th = 0 straight ahead
                int k = i * (Segs + 1) + j;
                v[k] = p; n[k] = p;
                var u = new Vector2(); u.x = 0.5f + p.x * 0.5f; u.y = 0.5f + p.y * 0.5f;
                if (p.z < 0) { u.x = 0.02f; u.y = 0.5f; }   // the back: white
                uv[k] = u;
            }
        var tri = new int[Rings * Segs * 6]; int t = 0;
        for (int i = 0; i < Rings; i++)
            for (int j = 0; j < Segs; j++)
            {
                int a = i * (Segs + 1) + j, b = a + Segs + 1;
                tri[t++] = a; tri[t++] = b; tri[t++] = a + 1;   // outward (Unity: clockwise seen from outside)
                tri[t++] = a + 1; tri[t++] = b; tri[t++] = b + 1;
            }
        sphere = new Mesh { name = "CoopEyeball", vertices = v, normals = n, uv = uv, triangles = tri, hideFlags = HideFlags.DontUnloadUnusedAsset };
        return sphere;
    }

    // The eyes of a person made here (each player's own colour; SkinSync, SkinShowcase).
    public static void SetColor(GameObject root, Color32 c)
    {
        if (root == null) return;
        var m = Material(c);
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            if (mr.name == "CoopEyeball") mr.sharedMaterial = m;
    }

    // "#RRGGBB" (or "RRGGBB"); anything else: the default.
    public static Color32 Parse(string hex)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int v))
            return new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
        return Parse(DefaultHex);
    }

    public static string Hex(Color32 c) => $"#{c.r:X2}{c.g:X2}{c.b:X2}";

    // White, an iris of the colour with darker rays and rim (a little lighter round the pupil), a black pupil, a soft
    // highlight from the material. One per colour, kept.
    static Material Material(Color32 iris)
    {
        int key = iris.r << 16 | iris.g << 8 | iris.b;
        if (materials.TryGetValue(key, out var have) && have != null) return have;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "CoopEye", hideFlags = HideFlags.DontUnloadUnusedAsset };
        var px = new Color32[S * S];
        var rnd = new System.Random(7);
        var rays = new float[64]; for (int i = 0; i < rays.Length; i++) rays[i] = (float)rnd.NextDouble();
        Color col = iris;
        var inner = Color.Lerp(col, new Color(0.62f, 0.48f, 0.22f), 0.25f) * 1.15f; var outer = col * 0.8f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2 - 1, dy = (y + 0.5f) / S * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
                Color c;
                if (d < 0.16f) c = new Color(0.02f, 0.02f, 0.02f);
                else if (d < 0.40f)
                {
                    float a = (Mathf.Atan2(dy, dx) / (2 * Mathf.PI) + 0.5f) * rays.Length;
                    float ray = rays[(int)a % rays.Length];
                    float f = (d - 0.16f) / 0.24f;
                    c = Color.Lerp(inner, outer, f) * (0.75f + 0.35f * ray);
                    if (f > 0.85f) c *= 0.55f;   // the dark rim
                }
                else
                {
                    float e = Mathf.Clamp01((d - 0.4f) / 0.6f);
                    c = Color.Lerp(new Color(0.93f, 0.91f, 0.88f), new Color(0.86f, 0.74f, 0.72f), e * e);
                }
                c.a = 1;
                px[y * S + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply(true, true);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CoopEye " + Hex(iris), hideFlags = HideFlags.DontUnloadUnusedAsset };
        material.SetTexture("_BaseMap", tex);
        material.SetFloat("_Smoothness", 0.85f);
        materials[key] = material;
        return material;
    }
}
