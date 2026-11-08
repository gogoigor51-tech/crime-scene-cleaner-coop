using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CscCoop.Skins;

// The Unity side of a person from the game's files: makes the skeleton, meshes, textures and materials from a
// PersonData (PersonReader) on the main thread. The game's people are Character Creator rigs, so the humanoid bones
// are found by name (CcBones: the names the bodies' humanoid Animators use, skinprobe).
public static class SkinFiles
{
    public static readonly string[] CcBones =
    {
        "CC_Base_Hip", "CC_Base_Waist", "CC_Base_Spine01", "CC_Base_NeckTwist01", "CC_Base_Head",
        "CC_Base_L_Upperarm", "CC_Base_R_Upperarm", "CC_Base_L_Forearm", "CC_Base_R_Forearm", "CC_Base_L_Hand", "CC_Base_R_Hand",
        "CC_Base_L_Thigh", "CC_Base_R_Thigh", "CC_Base_L_Calf", "CC_Base_R_Calf", "CC_Base_L_Foot", "CC_Base_R_Foot",
    };

    // A few people (Tyler's apartment: Daughter, Husband, PoolBoy, wife, Gamerson) are on an Unreal-style rig instead
    // (root/pelvis/spine_01..., upperarm_l...): they "couldn't load" (no CC_Base_Hip) until these names were known.
    public static readonly string[] UeBones =
    {
        "pelvis", "spine_01", "spine_02", "neck_01", "head",
        "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r", "hand_l", "hand_r",
        "thigh_l", "thigh_r", "calf_l", "calf_r", "foot_l", "foot_r",
    };

    static string dataDir;
    public static string DataDir => dataDir ??= Application.dataPath;   // ...\CrimeCleaner_Data

    // Read and make in one go (the probe; the loader reads in the background and calls Create).
    public static GameObject Build(AssetSet set, Person p, Transform parent, int mipSkip, out Transform[] humanoid, out string why)
    {
        humanoid = null;
        var d = PersonReader.Read(set, p, mipSkip);
        if (d == null) { why = "no skinned meshes"; return null; }
        return Create(d, parent, out humanoid, out why);
    }

    // The person under parent, inactive (the caller places it and switches it on); null if it has no CC hips.
    public static GameObject Create(PersonData d, Transform parent, out Transform[] humanoid, out string why)
    {
        why = ""; humanoid = null;
        EyeOpener.Apply(d);
        var nodes = new Transform[d.Nodes.Count];
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = d.Nodes[i];
            var t = new GameObject(n.Name).transform;
            if (i == 0) { t.gameObject.SetActive(false); t.SetParent(parent, false); }
            else t.SetParent(nodes[n.Parent], false);
            t.localPosition = V3(n.Pos, 0);
            var q = new Quaternion(); q.x = n.Rot[0]; q.y = n.Rot[1]; q.z = n.Rot[2]; q.w = n.Rot[3];
            t.localRotation = q;
            t.localScale = V3(n.Scale, 0);
            if (i > 0 && !n.Active) t.gameObject.SetActive(false);
            nodes[i] = t;
        }
        var meshes = new Mesh[d.Meshes.Count];
        for (int i = 0; i < meshes.Length; i++) meshes[i] = MakeMesh(d.Meshes[i]);
        var textures = new Texture2D[d.Textures.Count];
        for (int i = 0; i < textures.Length; i++) textures[i] = MakeTexture(d.Textures[i]);
        var materials = new Material[d.Materials.Count];
        for (int i = 0; i < materials.Length; i++) materials[i] = MakeMaterial(d.Materials[i], textures);
        foreach (var r in d.Renderers)
        {
            var smr = nodes[r.Node].gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = meshes[r.Mesh];
            var bones = new Transform[r.Bones.Length];
            for (int i = 0; i < bones.Length; i++) bones[i] = r.Bones[i] >= 0 ? nodes[r.Bones[i]] : null;
            smr.bones = bones;
            if (r.RootBone >= 0) smr.rootBone = nodes[r.RootBone];
            smr.updateWhenOffscreen = true;
            var mats = new Material[r.Materials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = r.Materials[i] >= 0 ? materials[r.Materials[i]] : Fallback;
            smr.sharedMaterials = mats;
        }
        // By name, preferring the transforms the meshes are skinned to: a person can hold a second rig with the same
        // bone names (posing that one moved nothing: the previews stood in their T pose).
        var used = new HashSet<int>();
        foreach (var r in d.Renderers) foreach (var b in r.Bones) if (b >= 0) used.Add(b);
        humanoid = new Transform[CcBones.Length];
        int dupes = 0;
        for (int k = 0; k < CcBones.Length; k++)
        {
            int pick = -1, seen = 0;
            foreach (var name in new[] { CcBones[k], UeBones[k] })
            {
                for (int i = 0; i < nodes.Length; i++)
                    if (d.Nodes[i].Name == name) { seen++; if (pick < 0 || (used.Contains(i) && !used.Contains(pick))) pick = i; }
                if (pick >= 0) break;
            }
            if (seen > 1) dupes++;
            humanoid[k] = pick >= 0 ? nodes[pick] : null;
        }
        if (dupes > 0) Plugin.L.LogInfo($"COOP skin '{d.Person?.Display}': {dupes} humanoid bone names twice, took the skinned ones");
        if (humanoid[0] == null) { why = "no hips (CC_Base_Hip or pelvis)"; Destroy(nodes[0].gameObject); return null; }
        return nodes[0].gameObject;
    }

    // A person's root and everything made for it (meshes, materials, textures are not destroyed with the objects).
    public static void Destroy(GameObject root)
    {
        if (root == null) return;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh != null) Object.Destroy(smr.sharedMesh);
            foreach (var m in smr.sharedMaterials)
            {
                if (m == null || m == fallback) continue;
                foreach (var prop in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap", "_SpecGlossMap", "_DetailAlbedoMap", "_DetailNormalMap" })
                    if (m.HasProperty(prop) && m.GetTexture(prop) != null) Object.Destroy(m.GetTexture(prop));
                Object.Destroy(m);
            }
        }
        Object.Destroy(root);
    }

    static Vector3 V3(float[] a, int i) { var v = new Vector3(); v.x = a[i]; v.y = a[i + 1]; v.z = a[i + 2]; return v; }

    static Mesh MakeMesh(MeshArrays a)
    {
        int n = a.Vertices;
        var mesh = new Mesh { name = a.Name, indexFormat = a.Index32 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        var pos = new Vector3[n];
        for (int v = 0; v < n; v++) pos[v] = V3(a.Pos, v * 3);
        mesh.vertices = pos;
        if (a.Normal != null) { var nr = new Vector3[n]; for (int v = 0; v < n; v++) nr[v] = V3(a.Normal, v * 3); mesh.normals = nr; }
        if (a.Tangent != null)
        {
            var tg = new Vector4[n];
            for (int v = 0; v < n; v++) { var t = new Vector4(); t.x = a.Tangent[v * 4]; t.y = a.Tangent[v * 4 + 1]; t.z = a.Tangent[v * 4 + 2]; t.w = a.Tangent[v * 4 + 3]; tg[v] = t; }
            mesh.tangents = tg;
        }
        if (a.Uv0 != null) mesh.uv = Uv(a.Uv0, n);
        if (a.Uv1 != null) mesh.uv2 = Uv(a.Uv1, n);
        if (a.BoneIndex != null)
        {
            var bw = new BoneWeight[n];
            for (int v = 0; v < n; v++)
            {
                var w = new BoneWeight();
                w.boneIndex0 = a.BoneIndex[v * 4]; w.weight0 = a.BoneWeight[v * 4];
                w.boneIndex1 = a.BoneIndex[v * 4 + 1]; w.weight1 = a.BoneWeight[v * 4 + 1];
                w.boneIndex2 = a.BoneIndex[v * 4 + 2]; w.weight2 = a.BoneWeight[v * 4 + 2];
                w.boneIndex3 = a.BoneIndex[v * 4 + 3]; w.weight3 = a.BoneWeight[v * 4 + 3];
                bw[v] = w;
            }
            mesh.boneWeights = bw;
        }
        var poses = new Matrix4x4[a.BindPoses.Length];
        for (int i = 0; i < poses.Length; i++)
        {
            // The file keeps a matrix row by row (e00 e01 e02 e03 e10 ...); Unity's indexer goes column by column.
            // Read as columns, the translation landed in the bottom row: every bone bound at the origin, the people
            // stuck in their T pose (the limbs had no direction to follow).
            var f = a.BindPoses[i]; var m = new Matrix4x4();
            for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) m[row + col * 4] = f[row * 4 + col];
            poses[i] = m;
        }
        mesh.bindposes = poses;
        mesh.subMeshCount = a.SubMeshes.Length;
        for (int sm = 0; sm < a.SubMeshes.Length; sm++) mesh.SetTriangles(a.SubMeshes[sm], sm, false);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        return mesh;
    }

    static Vector2[] Uv(float[] a, int n)
    {
        var uv = new Vector2[n];
        for (int v = 0; v < n; v++) { var p = new Vector2(); p.x = a[v * 2]; p.y = a[v * 2 + 1]; uv[v] = p; }
        return uv;
    }

    static Texture2D MakeTexture(TexBytes t)
    {
        try
        {
            var tex = new Texture2D(t.Width, t.Height, (TextureFormat)t.Format, t.Mips > 1, t.Linear) { name = t.Name };
            tex.LoadRawTextureData(t.Data);
            tex.Apply(false, true);   // to the GPU only: no copy kept in memory
            return tex;
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"COOP skin texture {t.Name} ({t.Width}x{t.Height} format {t.Format}): {e.Message}");
            return null;
        }
    }

    static Shader lit;
    static Material fallback;
    static Material Fallback => fallback ??= new Material(lit ??= Shader.Find("Universal Render Pipeline/Lit"));

    static Material MakeMaterial(MatInfo md, Texture2D[] textures)
    {
        if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit");
        // The game's people use URP Lit or the blood shader (its maps already mapped to URP Lit's by PersonReader).
        var mat = new Material(lit) { name = md.Name };
        foreach (var kv in md.Floats) if (mat.HasProperty(kv.Key)) mat.SetFloat(kv.Key, kv.Value);
        foreach (var kv in md.Ints) if (mat.HasProperty(kv.Key)) mat.SetInt(kv.Key, kv.Value);
        foreach (var kv in md.Colors) if (mat.HasProperty(kv.Key)) { var c = new Color(); c.r = kv.Value[0]; c.g = kv.Value[1]; c.b = kv.Value[2]; c.a = kv.Value[3]; mat.SetColor(kv.Key, c); }
        foreach (var (prop, ti, sx, sy, ox, oy) in md.Textures)
        {
            var tex = textures[ti];
            if (tex == null) continue;
            mat.SetTexture(prop, tex);
            var s = new Vector2(); s.x = sx; s.y = sy; mat.SetTextureScale(prop, s);
            var o = new Vector2(); o.x = ox; o.y = oy; mat.SetTextureOffset(prop, o);
            if (prop == "_BumpMap") mat.EnableKeyword("_NORMALMAP");
            if (prop == "_MetallicGlossMap") mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (prop == "_OcclusionMap") mat.EnableKeyword("_OCCLUSIONMAP");
            if (prop == "_EmissionMap") mat.EnableKeyword("_EMISSION");
        }
        foreach (var k in md.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)) mat.EnableKeyword(k);
        if (md.Keywords.Contains("_ALPHATEST_ON")) { mat.SetFloat("_AlphaClip", 1); if (!md.Floats.ContainsKey("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f); }
        if (md.RenderQueue >= 0) mat.renderQueue = md.RenderQueue;
        return mat;
    }
}
