using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CscCoop.Skins;

// A person from any job, built from the game's own files (AssetFile) without loading that job: the skeleton (the
// body's transforms), its skinned meshes, their materials and textures. The game's people are Character Creator rigs,
// so the humanoid bones are found by name (CcBones, the names the bodies' humanoid Animators use, skinprobe).
public static class SkinFiles
{
    public static readonly string[] CcBones =
    {
        "CC_Base_Hip", "CC_Base_Waist", "CC_Base_Spine01", "CC_Base_NeckTwist01", "CC_Base_Head",
        "CC_Base_L_Upperarm", "CC_Base_R_Upperarm", "CC_Base_L_Forearm", "CC_Base_R_Forearm", "CC_Base_L_Hand", "CC_Base_R_Hand",
        "CC_Base_L_Thigh", "CC_Base_R_Thigh", "CC_Base_L_Calf", "CC_Base_R_Calf", "CC_Base_L_Foot", "CC_Base_R_Foot",
    };

    static string dataDir;
    public static string DataDir => dataDir ??= Application.dataPath;   // ...\CrimeCleaner_Data

    // A person found in a scene file: its root game object (the one with the Animator) and how many skinned meshes
    // with a CC rig are under it.
    public readonly struct Person
    {
        public readonly string File; public readonly long Root; public readonly string Name; public readonly int Meshes;
        public Person(string f, long r, string n, int m) { File = f; Root = r; Name = n; Meshes = m; }
        public string Key => $"{File}#{Root}";
    }

    // The people in one scene file: Animators whose game object holds skinned meshes on a CC rig.
    public static List<Person> FindPeople(AssetSet set, string level)
    {
        var f = set.Get(level);
        var found = new List<Person>();
        foreach (var id in f.OfClass(ClassId.Animator))
        {
            var r = f.Read(id);
            var goPtr = r.Ptr();
            if (goPtr.File != 0) continue;
            var go = GameObjectData.Read(f.Read(goPtr.Id));
            int meshes = 0;
            foreach (var smr in SkinnedUnder(f, goPtr.Id))
                if (smr.Bones.Count >= 60) meshes++;
            if (meshes > 0) found.Add(new Person(level, goPtr.Id, go.Name, meshes));
        }
        return found;
    }

    // Every skinned mesh renderer under a game object (by its transform's children), in this file.
    static IEnumerable<SkinnedRendererData> SkinnedUnder(AssetFile f, long goId)
    {
        var stack = new Stack<long>();
        stack.Push(goId);
        while (stack.Count > 0)
        {
            var go = GameObjectData.Read(f.Read(stack.Pop()));
            foreach (var c in go.Components)
            {
                if (c.File != 0 || !f.Objects.TryGetValue(c.Id, out var o)) continue;
                if (o.ClassId == ClassId.SkinnedMeshRenderer) yield return SkinnedRendererData.Read(f.Read(c.Id));
                else if (o.ClassId == ClassId.Transform)
                {
                    var t = TransformData.Read(f.Read(c.Id));
                    foreach (var ch in t.Children)
                        if (ch.File == 0 && f.Objects.ContainsKey(ch.Id)) stack.Push(TransformData.Read(f.Read(ch.Id)).GameObject.Id);
                }
            }
        }
    }

    // ---- building one person ----

    // Builds the person under parent (inactive until done), returns its root and the 17 humanoid bones.
    // mipSkip: leave out that many of the textures' largest mip levels (previews).
    public static GameObject Build(AssetSet set, Person p, Transform parent, int mipSkip, out Transform[] humanoid, out string why)
    {
        why = ""; humanoid = null;
        var f = set.Get(p.File);
        var made = new Dictionary<long, Transform>();
        var root = BuildTransforms(f, p.Root, parent, made);
        root.gameObject.SetActive(false);
        var meshCache = new Dictionary<string, Mesh>();
        var matCache = new Dictionary<string, Material>();
        var texCache = new Dictionary<string, Texture2D>();
        int built = 0;
        foreach (var kv in made)
        {
            var go = GameObjectData.Read(f.Read(kv.Key));
            foreach (var c in go.Components)
            {
                if (c.File != 0 || !f.Objects.TryGetValue(c.Id, out var o) || o.ClassId != ClassId.SkinnedMeshRenderer) continue;
                var s = SkinnedRendererData.Read(f.Read(c.Id));
                if (!s.Enabled) continue;
                var mf = set.Resolve(f, s.Mesh);
                if (mf == null) continue;
                var mesh = BuildMesh(mf, s.Mesh.Id, meshCache);
                if (mesh == null) continue;
                var bones = new Transform[s.Bones.Count];
                for (int i = 0; i < bones.Length; i++) bones[i] = s.Bones[i].File == 0 && TransformOf(f, s.Bones[i].Id, made, out var bt) ? bt : null;
                var smr = kv.Value.gameObject.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = bones;
                if (s.RootBone.File == 0 && TransformOf(f, s.RootBone.Id, made, out var rb)) smr.rootBone = rb;
                smr.updateWhenOffscreen = true;
                var mats = new Material[s.Materials.Count];
                for (int i = 0; i < mats.Length; i++) mats[i] = BuildMaterial(set, f, s.Materials[i], mipSkip, matCache, texCache);
                smr.sharedMaterials = mats;
                built++;
            }
        }
        if (built == 0) { why = "no skinned meshes built"; Object.Destroy(root.gameObject); return null; }
        humanoid = new Transform[CcBones.Length];
        var byName = new Dictionary<string, Transform>();
        foreach (var t in made.Values) byName[t.name] = t;
        for (int k = 0; k < CcBones.Length; k++) byName.TryGetValue(CcBones[k], out humanoid[k]);
        if (humanoid[0] == null) { why = "no CC_Base_Hip"; Object.Destroy(root.gameObject); return null; }
        return root.gameObject;
    }

    // The transforms map game objects (by their pathID) to the transforms made for them.
    static Transform BuildTransforms(AssetFile f, long goId, Transform parent, Dictionary<long, Transform> made)
    {
        var go = GameObjectData.Read(f.Read(goId));
        TransformData td = null;
        foreach (var c in go.Components)
            if (c.File == 0 && f.Objects.TryGetValue(c.Id, out var o) && o.ClassId == ClassId.Transform) { td = TransformData.Read(f.Read(c.Id)); break; }
        var t = new GameObject(go.Name).transform;
        t.SetParent(parent, false);
        if (td != null)
        {
            t.localPosition = new Vector3(td.Pos[0], td.Pos[1], td.Pos[2]);
            t.localRotation = new Quaternion(td.Rot[0], td.Rot[1], td.Rot[2], td.Rot[3]);
            t.localScale = new Vector3(td.Scale[0], td.Scale[1], td.Scale[2]);
        }
        if (!go.Active && parent != null) t.gameObject.SetActive(false);
        made[goId] = t;
        if (td != null)
            foreach (var ch in td.Children)
                if (ch.File == 0 && f.Objects.ContainsKey(ch.Id)) BuildTransforms(f, TransformData.Read(f.Read(ch.Id)).GameObject.Id, t, made);
        return t;
    }

    static bool TransformOf(AssetFile f, long transformId, Dictionary<long, Transform> made, out Transform t)
    {
        t = null;
        if (!f.Objects.ContainsKey(transformId)) return false;
        return made.TryGetValue(TransformData.Read(f.Read(transformId)).GameObject.Id, out t);
    }

    // ---- mesh ----

    static readonly int[] FormatSize = { 4, 2, 1, 1, 2, 2, 1, 1, 2, 2, 4, 4 };

    static Mesh BuildMesh(AssetFile f, long id, Dictionary<string, Mesh> cache)
    {
        string key = f.FilePath + "#" + id;
        if (cache.TryGetValue(key, out var cached)) return cached;
        var m = MeshData.Read(f.Read(id));
        if (m.Compression != 0) return null;
        var data = m.StreamSize > 0 ? f.ReadStream(m.StreamPath, m.StreamOffset, m.StreamSize) : m.VertexBytes;
        int n = (int)m.VertexCount;
        // Streams: each holds its channels interleaved; a stream starts 16-byte aligned after the one before.
        var stride = new int[4];
        foreach (var c in m.Channels) if (c.Dimension > 0) stride[c.Stream] = Math.Max(stride[c.Stream], c.Offset + FormatSize[c.Format] * c.Dimension);
        var start = new int[4];
        for (int s = 1; s < 4; s++) start[s] = (start[s - 1] + stride[s - 1] * n + 15) & ~15;
        float Get(int ch, int v, int comp)
        {
            var c = m.Channels[ch];
            int at = start[c.Stream] + v * stride[c.Stream] + c.Offset + comp * FormatSize[c.Format];
            switch (c.Format)
            {
                case 0: return BitConverter.ToSingle(data, at);
                case 1: return Mathf.HalfToFloat(BitConverter.ToUInt16(data, at));
                case 2: return data[at] / 255f;
                case 3: return Math.Max((sbyte)data[at] / 127f, -1f);
                case 4: return BitConverter.ToUInt16(data, at) / 65535f;
                case 5: return Math.Max(BitConverter.ToInt16(data, at) / 32767f, -1f);
                case 6: return data[at];
                case 7: return (sbyte)data[at];
                case 8: return BitConverter.ToUInt16(data, at);
                case 9: return BitConverter.ToInt16(data, at);
                case 10: return BitConverter.ToUInt32(data, at);
                default: return BitConverter.ToInt32(data, at);
            }
        }
        bool Has(int ch) => ch < m.Channels.Count && m.Channels[ch].Dimension > 0;
        var mesh = new Mesh { name = m.Name, indexFormat = m.IndexFormat == 1 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        var pos = new Vector3[n];
        for (int v = 0; v < n; v++) pos[v] = new Vector3(Get(0, v, 0), Get(0, v, 1), Get(0, v, 2));
        mesh.vertices = pos;
        if (Has(1)) { var a = new Vector3[n]; for (int v = 0; v < n; v++) a[v] = new Vector3(Get(1, v, 0), Get(1, v, 1), Get(1, v, 2)); mesh.normals = a; }
        if (Has(2)) { var a = new Vector4[n]; for (int v = 0; v < n; v++) a[v] = new Vector4(Get(2, v, 0), Get(2, v, 1), Get(2, v, 2), m.Channels[2].Dimension > 3 ? Get(2, v, 3) : 1); mesh.tangents = a; }
        for (int uv = 0; uv < 2; uv++)
            if (Has(4 + uv)) { var a = new Vector2[n]; for (int v = 0; v < n; v++) a[v] = new Vector2(Get(4 + uv, v, 0), Get(4 + uv, v, 1)); mesh.SetUVs(uv, a); }
        if (Has(13))
        {
            int dw = Has(12) ? m.Channels[12].Dimension : 0, di = m.Channels[13].Dimension;
            var w = new BoneWeight[n];
            for (int v = 0; v < n; v++)
            {
                var bw = new BoneWeight();
                float W(int k) => k < dw ? Get(12, v, k) : (k == 0 && dw == 0 ? 1 : 0);
                int I(int k) => k < di ? (int)Get(13, v, k) : 0;
                bw.boneIndex0 = I(0); bw.weight0 = di > 0 ? W(0) : 0;
                bw.boneIndex1 = I(1); bw.weight1 = di > 1 ? W(1) : 0;
                bw.boneIndex2 = I(2); bw.weight2 = di > 2 ? W(2) : 0;
                bw.boneIndex3 = I(3); bw.weight3 = di > 3 ? W(3) : 0;
                if (dw == di - 1 && di > 1) { float sum = 0; for (int k = 0; k < dw; k++) sum += W(k); SetWeight(ref bw, dw, 1 - sum); }
                w[v] = bw;
            }
            mesh.boneWeights = w;
        }
        var poses = new Matrix4x4[m.BindPoses.Count];
        for (int i = 0; i < poses.Length; i++) { var mt = new Matrix4x4(); for (int k = 0; k < 16; k++) mt[k] = m.BindPoses[i][k]; poses[i] = mt; }
        mesh.bindposes = poses;
        mesh.subMeshCount = m.SubMeshes.Count;
        int isz = m.IndexFormat == 1 ? 4 : 2;
        for (int sm = 0; sm < m.SubMeshes.Count; sm++)
        {
            var s = m.SubMeshes[sm];
            var tri = new int[s.IndexCount];
            for (int i = 0; i < tri.Length; i++)
            {
                int at = (int)s.FirstByte + i * isz;
                tri[i] = (isz == 4 ? (int)BitConverter.ToUInt32(m.IndexBuffer, at) : BitConverter.ToUInt16(m.IndexBuffer, at)) + (int)s.BaseVertex;
            }
            mesh.SetTriangles(tri, sm, false);
        }
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        cache[key] = mesh;
        return mesh;
    }

    static void SetWeight(ref BoneWeight bw, int k, float w)
    {
        switch (k) { case 1: bw.weight1 = w; break; case 2: bw.weight2 = w; break; case 3: bw.weight3 = w; break; }
    }

    // ---- materials and textures ----

    static Shader lit;

    // The game's blood shader (most Act I and II people): its texture names -> URP Lit's.
    static readonly Dictionary<string, string> BloodShaderMaps = new()
    {
        ["_Base_Color"] = "_BaseMap", ["_Base_Map"] = "_BaseMap", ["_Normal_Map"] = "_BumpMap",
        ["_MetalllicSmoothness"] = "_MetallicGlossMap", ["_Occlusion_Map"] = "_OcclusionMap", ["_Emmision_Map"] = "_EmissionMap",
    };

    static Material BuildMaterial(AssetSet set, AssetFile from, PPtr p, int mipSkip, Dictionary<string, Material> cache, Dictionary<string, Texture2D> texCache)
    {
        var f = set.Resolve(from, p);
        if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit");
        if (f == null) return new Material(lit);
        string key = f.FilePath + "#" + p.Id;
        if (cache.TryGetValue(key, out var cached)) return cached;
        var md = MaterialData.Read(f.Read(p.Id));
        // The game's people use URP Lit (a few hair materials a Lux shader whose properties URP Lit shares).
        var mat = new Material(lit) { name = md.Name };
        foreach (var kv in md.Floats) if (mat.HasProperty(kv.Key)) mat.SetFloat(kv.Key, kv.Value);
        foreach (var kv in md.Ints) if (mat.HasProperty(kv.Key)) mat.SetInt(kv.Key, kv.Value);
        foreach (var kv in md.Colors) if (mat.HasProperty(kv.Key)) mat.SetColor(kv.Key, new Color(kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3]));
        foreach (var kv in md.Textures)
        {
            // The game's blood shader names its maps its own way; the blood maps themselves are left out (a clean skin).
            string prop = BloodShaderMaps.TryGetValue(kv.Key, out var urp) ? urp : kv.Key;
            if (kv.Value.tex.IsNull || !mat.HasProperty(prop)) continue;
            if (prop != kv.Key && md.Textures.TryGetValue(prop, out var own) && !own.tex.IsNull) continue;   // URP's own slot wins
            var tex = BuildTexture(set, f, kv.Value.tex, mipSkip, texCache);
            if (tex == null) continue;
            mat.SetTexture(prop, tex);
            mat.SetTextureScale(prop, new Vector2(kv.Value.sx, kv.Value.sy));
            mat.SetTextureOffset(prop, new Vector2(kv.Value.ox, kv.Value.oy));
            if (prop == "_BumpMap") mat.EnableKeyword("_NORMALMAP");
            if (prop == "_MetallicGlossMap") mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (prop == "_OcclusionMap") mat.EnableKeyword("_OCCLUSIONMAP");
            if (prop == "_EmissionMap") mat.EnableKeyword("_EMISSION");
        }
        foreach (var k in md.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)) mat.EnableKeyword(k);
        if (md.Keywords.Contains("_ALPHATEST_ON")) { mat.SetFloat("_AlphaClip", 1); if (!md.Floats.ContainsKey("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f); }
        if (md.RenderQueue >= 0) mat.renderQueue = md.RenderQueue;
        cache[key] = mat;
        return mat;
    }

    // Block-compressed formats and the bytes per 4x4 block; other formats per pixel.
    static int MipBytes(int format, int w, int h)
    {
        int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
        return format switch
        {
            10 => bw * bh * 8,                 // DXT1
            12 or 25 or 24 or 26 or 27 => bw * bh * 16,   // DXT5, BC7, BC6H, BC4/BC5 (BC4 is 8: rare here)
            4 or 5 or 14 => w * h * 4,         // RGBA32, ARGB32, BGRA32
            3 => w * h * 3,                    // RGB24
            1 or 63 => w * h,                  // Alpha8, R8
            _ => -1,
        };
    }

    static Texture2D BuildTexture(AssetSet set, AssetFile from, PPtr p, int mipSkip, Dictionary<string, Texture2D> cache)
    {
        var f = set.Resolve(from, p);
        if (f == null) return null;
        string key = f.FilePath + "#" + p.Id + "@" + mipSkip;
        if (cache.TryGetValue(key, out var cached)) return cached;
        var t = TextureData.Read(f.Read(p.Id));
        int w = t.Width, h = t.Height, mips = Math.Max(1, t.MipCount);
        long skip = 0;
        int drop = Math.Min(mipSkip, mips - 1);
        for (int i = 0; i < drop; i++)
        {
            int b = MipBytes(t.Format, w, h);
            if (b < 0) { drop = 0; skip = 0; w = t.Width; h = t.Height; break; }
            skip += b; w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
        }
        long total = t.StreamSize > 0 ? t.StreamSize : t.Image.Length;
        byte[] bytes = t.StreamSize > 0 ? f.ReadStream(t.StreamPath, t.StreamOffset + skip, (uint)(total - skip))
                                        : t.Image.AsSpan((int)skip).ToArray();
        try
        {
            var tex = new Texture2D(w, h, (TextureFormat)t.Format, mips - drop > 1, t.ColorSpace == 0) { name = t.Name };
            tex.LoadRawTextureData(bytes);
            tex.Apply(false, true);   // to the GPU only: no copy kept in memory
            cache[key] = tex;
            return tex;
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"COOP skin texture {t.Name} ({t.Width}x{t.Height} format {t.Format}): {e.Message}");
            return null;
        }
    }
}
