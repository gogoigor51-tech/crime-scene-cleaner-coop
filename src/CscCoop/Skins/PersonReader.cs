using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CscCoop.Skins;

// The part of building a person that needs no Unity: finding the people in the scene files and reading one person's
// skeleton, meshes (decoded to plain arrays), materials and textures (raw compressed bytes). Thread-safe, so the
// skins load in the background (SkinLoader); SkinFiles makes the Unity objects from a PersonData on the main thread.
// tools/assetdump runs it outside the game.

// A person found in a scene file: its root game object (the one with the Animator). MeshKey: the meshes and materials
// it wears, to merge the same person found in several scene files (a job, its Nightmare version, its intro).
public sealed class Person
{
    public string File = ""; public long Root; public string Name = ""; public int Meshes; public string MeshKey = "";
    public string Key => $"{File}#{Root}";
    public string Display => PersonReader.CleanName(Name);
}

public sealed class NodeData { public string Name = ""; public int Parent = -1; public float[] Pos, Rot, Scale; public bool Active; }

public sealed class MeshArrays
{
    public string Name = ""; public int Vertices;
    public float[] Pos, Normal, Tangent, Uv0, Uv1;      // 3, 3, 4, 2, 2 floats per vertex (null if absent)
    public int[] BoneIndex; public float[] BoneWeight;   // 4 per vertex (null if not skinned)
    public float[][] BindPoses;                         // 16 floats, row by row
    public int[][] SubMeshes; public bool Index32;
}

public sealed class TexBytes { public string Name = ""; public int Width, Height, Format, Mips; public bool Linear; public byte[] Data; }

public sealed class MatInfo
{
    public string Name = ""; public string Keywords = ""; public int RenderQueue;
    public Dictionary<string, float> Floats = new(); public Dictionary<string, int> Ints = new(); public Dictionary<string, float[]> Colors = new();
    public List<(string prop, int tex, float sx, float sy, float ox, float oy)> Textures = new();
}

public sealed class RendererInfo { public int Node, Mesh; public int[] Bones; public int RootBone = -1; public int[] Materials; }

public sealed class PersonData
{
    public Person Person; public List<NodeData> Nodes = new(); public List<RendererInfo> Renderers = new();
    public List<MeshArrays> Meshes = new(); public List<MatInfo> Materials = new(); public List<TexBytes> Textures = new();
    public long Bytes;   // read from disk, for the log
}

public static class PersonReader
{
    // ---- finding people ----

    // The people in one scene file: Animators whose game object holds skinned meshes on a CC rig (60+ bones).
    public static List<Person> FindPeople(AssetSet set, string level)
    {
        var f = set.Get(level);
        var found = new List<Person>();
        foreach (var id in f.OfClass(ClassId.Animator))
        {
            var goPtr = f.Read(id).Ptr();
            if (goPtr.File != 0 || !f.Objects.ContainsKey(goPtr.Id)) continue;
            var go = GameObjectData.Read(f.Read(goPtr.Id));
            int meshes = 0;
            var keys = new List<string>();
            string bodyMaterial = "";
            foreach (var (smrGo, smr) in SkinnedUnder(f, goPtr.Id))
            {
                if (smr.Bones.Count < 60 || !smr.Enabled) continue;
                meshes++;
                var mf = set.Resolve(f, smr.Mesh);
                if (mf != null) keys.Add(Path.GetFileName(mf.FilePath) + ":" + smr.Mesh.Id);
                foreach (var mp in smr.Materials)
                {
                    var matf = set.Resolve(f, mp);
                    if (matf == null) continue;
                    keys.Add(Path.GetFileName(matf.FilePath) + ":" + mp.Id);
                    if (bodyMaterial == "" && smrGo.StartsWith("CC_Base_Body", StringComparison.Ordinal)) bodyMaterial = MaterialData.Read(matf.Read(mp.Id)).Name;
                }
            }
            if (meshes == 0) continue;
            keys.Sort(StringComparer.Ordinal);
            // Act II roots are mostly "RagdollBase": the body's material names them ("Guest_Geralt_Body").
            string name = go.Name;
            if ((name.StartsWith("RagdollBase", StringComparison.Ordinal) || name.EndsWith(" new", StringComparison.OrdinalIgnoreCase) || name.EndsWith(" New", StringComparison.Ordinal)) && bodyMaterial != "")
                name = bodyMaterial.EndsWith("_Body", StringComparison.Ordinal) ? bodyMaterial.Substring(0, bodyMaterial.Length - 5) : bodyMaterial;
            found.Add(new Person { File = level, Root = goPtr.Id, Name = name, Meshes = meshes, MeshKey = string.Join(",", keys) });
        }
        return found;
    }

    // Every scene file's people, the same person (the same meshes) once.
    public static List<Person> Catalog(string dataDir, Action<string> log = null)
    {
        var all = new List<Person>();
        var seen = new HashSet<string>();
        for (int n = 0; n < 200; n++)
        {
            string lv = "level" + n;
            if (!File.Exists(Path.Combine(dataDir, lv))) { if (n > 60) break; continue; }
            var set = new AssetSet(dataDir);
            try
            {
                foreach (var p in FindPeople(set, lv)) if (p.MeshKey != "" && seen.Add(p.MeshKey)) all.Add(p);
            }
            catch (Exception e) { log?.Invoke($"{lv}: {e.Message}"); }
            finally { set.Close(); }
        }
        return all;
    }

    // "Pref_SC_Mother", "Girl01 Ragdoll (1)", "DrugDealerAnim Variant" -> "SC Mother", "Girl01", "DrugDealer".
    public static string CleanName(string n)
    {
        n = System.Text.RegularExpressions.Regex.Replace(n, @"\s*\(\d+\)$", "");
        foreach (var cut in new[] { " Variant", " Ragdoll", "Ragdoll", " Anim", "Anim", "_Ragdoll", " ragdoll" })
            if (n.EndsWith(cut, StringComparison.Ordinal)) n = n.Substring(0, n.Length - cut.Length);
        if (n.StartsWith("Pref_", StringComparison.Ordinal)) n = n.Substring(5);
        return n.Replace('_', ' ').Trim();
    }

    // Every skinned mesh renderer under a game object (by its transform's children), in this file.
    static IEnumerable<(string go, SkinnedRendererData smr)> SkinnedUnder(AssetFile f, long goId)
    {
        var stack = new Stack<long>();
        stack.Push(goId);
        while (stack.Count > 0)
        {
            var go = GameObjectData.Read(f.Read(stack.Pop()));
            foreach (var c in go.Components)
            {
                if (c.File != 0 || !f.Objects.TryGetValue(c.Id, out var o)) continue;
                if (o.ClassId == ClassId.SkinnedMeshRenderer) yield return (go.Name, SkinnedRendererData.Read(f.Read(c.Id)));
                else if (o.ClassId == ClassId.Transform)
                    foreach (var ch in TransformData.Read(f.Read(c.Id)).Children)
                        if (ch.File == 0 && f.Objects.ContainsKey(ch.Id)) stack.Push(TransformData.Read(f.Read(ch.Id)).GameObject.Id);
            }
        }
    }

    // ---- reading one person ----

    // mipSkip: leave out that many of the textures' largest mip levels (previews: smaller, faster).
    public static PersonData Read(AssetSet set, Person p, int mipSkip, Func<bool> cancelled = null)
    {
        var d = new PersonData { Person = p };
        var f = set.Get(p.File);
        var nodeOfGo = new Dictionary<long, int>();
        var nodeOfTransform = new Dictionary<long, int>();
        ReadNodes(f, p.Root, -1, d, nodeOfGo, nodeOfTransform);
        var meshIndex = new Dictionary<string, int>();
        var matIndex = new Dictionary<string, int>();
        var texIndex = new Dictionary<string, int>();
        foreach (var kv in nodeOfGo)
        {
            if (cancelled != null && cancelled()) return null;
            var go = GameObjectData.Read(f.Read(kv.Key));
            foreach (var c in go.Components)
            {
                if (c.File != 0 || !f.Objects.TryGetValue(c.Id, out var o) || o.ClassId != ClassId.SkinnedMeshRenderer) continue;
                var s = SkinnedRendererData.Read(f.Read(c.Id));
                if (!s.Enabled) continue;
                var mf = set.Resolve(f, s.Mesh);
                if (mf == null) continue;
                string mk = mf.FilePath + "#" + s.Mesh.Id;
                if (!meshIndex.TryGetValue(mk, out int mi))
                {
                    var arr = ReadMesh(mf, s.Mesh.Id, d);
                    if (arr == null) continue;
                    meshIndex[mk] = mi = d.Meshes.Count; d.Meshes.Add(arr);
                }
                var r = new RendererInfo { Node = kv.Value, Mesh = mi, Bones = new int[s.Bones.Count], Materials = new int[s.Materials.Count] };
                for (int i = 0; i < r.Bones.Length; i++) r.Bones[i] = s.Bones[i].File == 0 && nodeOfTransform.TryGetValue(s.Bones[i].Id, out int bn) ? bn : -1;
                if (s.RootBone.File == 0 && nodeOfTransform.TryGetValue(s.RootBone.Id, out int rb)) r.RootBone = rb;
                for (int i = 0; i < r.Materials.Length; i++) r.Materials[i] = ReadMaterial(set, f, s.Materials[i], mipSkip, d, matIndex, texIndex);
                d.Renderers.Add(r);
            }
        }
        return d.Renderers.Count > 0 ? d : null;
    }

    static void ReadNodes(AssetFile f, long goId, int parent, PersonData d, Dictionary<long, int> nodeOfGo, Dictionary<long, int> nodeOfTransform)
    {
        var go = GameObjectData.Read(f.Read(goId));
        TransformData td = null; long tid = 0;
        foreach (var c in go.Components)
            if (c.File == 0 && f.Objects.TryGetValue(c.Id, out var o) && o.ClassId == ClassId.Transform) { td = TransformData.Read(f.Read(c.Id)); tid = c.Id; break; }
        int me = d.Nodes.Count;
        d.Nodes.Add(new NodeData { Name = go.Name, Parent = parent, Active = go.Active || parent < 0, Pos = td?.Pos ?? new float[3], Rot = td?.Rot ?? new float[] { 0, 0, 0, 1 }, Scale = td?.Scale ?? new float[] { 1, 1, 1 } });
        nodeOfGo[goId] = me;
        if (td == null) return;
        nodeOfTransform[tid] = me;
        foreach (var ch in td.Children)
            if (ch.File == 0 && f.Objects.ContainsKey(ch.Id)) ReadNodes(f, TransformData.Read(f.Read(ch.Id)).GameObject.Id, me, d, nodeOfGo, nodeOfTransform);
    }

    static readonly int[] FormatSize = { 4, 2, 1, 1, 2, 2, 1, 1, 2, 2, 4, 4 };

    static MeshArrays ReadMesh(AssetFile f, long id, PersonData d)
    {
        var m = MeshData.Read(f.Read(id));
        if (m.Compression != 0) return null;
        var data = m.StreamSize > 0 ? f.ReadStream(m.StreamPath, m.StreamOffset, m.StreamSize) : m.VertexBytes;
        d.Bytes += data.Length + m.IndexBuffer.Length;
        int n = (int)m.VertexCount;
        // Streams: each holds its channels interleaved; a stream starts 16-byte aligned after the one before.
        var stride = new int[4];
        foreach (var c in m.Channels) if (c.Dimension > 0) stride[c.Stream] = Math.Max(stride[c.Stream], c.Offset + FormatSize[c.Format] * c.Dimension);
        var start = new int[4];
        for (int s = 1; s < 4; s++) start[s] = (start[s - 1] + stride[s - 1] * n + 15) & ~15;
        bool Has(int ch) => ch < m.Channels.Count && m.Channels[ch].Dimension > 0;
        float Get(int ch, int v, int comp)
        {
            var c = m.Channels[ch];
            int at = start[c.Stream] + v * stride[c.Stream] + c.Offset + comp * FormatSize[c.Format];
            return c.Format switch
            {
                0 => BitConverter.ToSingle(data, at),
                1 => (float)BitConverter.ToHalf(data, at),
                2 => data[at] / 255f,
                3 => Math.Max((sbyte)data[at] / 127f, -1f),
                4 => BitConverter.ToUInt16(data, at) / 65535f,
                5 => Math.Max(BitConverter.ToInt16(data, at) / 32767f, -1f),
                6 => data[at],
                7 => (sbyte)data[at],
                8 => BitConverter.ToUInt16(data, at),
                9 => BitConverter.ToInt16(data, at),
                10 => BitConverter.ToUInt32(data, at),
                _ => BitConverter.ToInt32(data, at),
            };
        }
        float[] Channel(int ch, int dims, float fill)
        {
            if (!Has(ch)) return null;
            int have = m.Channels[ch].Dimension;
            var a = new float[n * dims];
            for (int v = 0; v < n; v++) for (int k = 0; k < dims; k++) a[v * dims + k] = k < have ? Get(ch, v, k) : fill;
            return a;
        }
        var arr = new MeshArrays
        {
            Name = m.Name, Vertices = n, Pos = Channel(0, 3, 0), Normal = Channel(1, 3, 0), Tangent = Channel(2, 4, 1),
            Uv0 = Channel(4, 2, 0), Uv1 = Channel(5, 2, 0), Index32 = m.IndexFormat == 1,
        };
        if (Has(13))
        {
            int dw = Has(12) ? m.Channels[12].Dimension : 0, di = m.Channels[13].Dimension;
            arr.BoneIndex = new int[n * 4]; arr.BoneWeight = new float[n * 4];
            for (int v = 0; v < n; v++)
                for (int k = 0; k < 4 && k < di; k++)
                {
                    arr.BoneIndex[v * 4 + k] = (int)Get(13, v, k);
                    arr.BoneWeight[v * 4 + k] = k < dw ? Get(12, v, k) : (dw == 0 && k == 0 ? 1 : 0);
                }
        }
        arr.BindPoses = m.BindPoses.ToArray();
        int isz = arr.Index32 ? 4 : 2;
        arr.SubMeshes = new int[m.SubMeshes.Count][];
        for (int sm = 0; sm < m.SubMeshes.Count; sm++)
        {
            var s = m.SubMeshes[sm];
            var tri = new int[s.IndexCount];
            for (int i = 0; i < tri.Length; i++)
            {
                int at = (int)s.FirstByte + i * isz;
                tri[i] = (isz == 4 ? (int)BitConverter.ToUInt32(m.IndexBuffer, at) : BitConverter.ToUInt16(m.IndexBuffer, at)) + (int)s.BaseVertex;
            }
            arr.SubMeshes[sm] = tri;
        }
        return arr;
    }

    // The game's blood shader (most Act I and II people): its texture names -> URP Lit's. Its blood maps are left out.
    public static readonly Dictionary<string, string> BloodShaderMaps = new()
    {
        ["_Base_Color"] = "_BaseMap", ["_Base_Map"] = "_BaseMap", ["_Normal_Map"] = "_BumpMap",
        ["_MetalllicSmoothness"] = "_MetallicGlossMap", ["_Occlusion_Map"] = "_OcclusionMap", ["_Emmision_Map"] = "_EmissionMap",
    };
    static readonly HashSet<string> LitMaps = new() { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap", "_SpecGlossMap", "_DetailAlbedoMap", "_DetailNormalMap" };

    static int ReadMaterial(AssetSet set, AssetFile from, PPtr p, int mipSkip, PersonData d, Dictionary<string, int> matIndex, Dictionary<string, int> texIndex)
    {
        var f = set.Resolve(from, p);
        if (f == null) return -1;
        string key = f.FilePath + "#" + p.Id;
        if (matIndex.TryGetValue(key, out int have)) return have;
        var md = MaterialData.Read(f.Read(p.Id));
        var info = new MatInfo { Name = md.Name, Keywords = md.Keywords, RenderQueue = md.RenderQueue, Floats = md.Floats, Ints = md.Ints, Colors = md.Colors };
        foreach (var kv in md.Textures)
        {
            string prop = BloodShaderMaps.TryGetValue(kv.Key, out var urp) ? urp : kv.Key;
            if (kv.Value.tex.IsNull || !LitMaps.Contains(prop)) continue;
            if (prop != kv.Key && md.Textures.TryGetValue(prop, out var own) && !own.tex.IsNull) continue;   // URP's own slot wins
            int ti = ReadTexture(set, f, kv.Value.tex, mipSkip, d, texIndex);
            if (ti >= 0) info.Textures.Add((prop, ti, kv.Value.sx, kv.Value.sy, kv.Value.ox, kv.Value.oy));
        }
        matIndex[key] = d.Materials.Count;
        d.Materials.Add(info);
        return d.Materials.Count - 1;
    }

    // Block-compressed formats: bytes per 4x4 block; others per pixel; -1 = unknown (no mips skipped then).
    static int MipBytes(int format, int w, int h)
    {
        int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
        return format switch
        {
            10 or 26 => bw * bh * 8,                 // DXT1, BC4
            12 or 24 or 25 or 27 => bw * bh * 16,    // DXT5, BC6H, BC7, BC5
            4 or 5 or 14 => w * h * 4,
            3 => w * h * 3,
            1 or 63 => w * h,
            _ => -1,
        };
    }

    static int ReadTexture(AssetSet set, AssetFile from, PPtr p, int mipSkip, PersonData d, Dictionary<string, int> texIndex)
    {
        var f = set.Resolve(from, p);
        if (f == null) return -1;
        string key = f.FilePath + "#" + p.Id;
        if (texIndex.TryGetValue(key, out int have)) return have;
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
        var bytes = t.StreamSize > 0 ? f.ReadStream(t.StreamPath, t.StreamOffset + skip, (uint)(total - skip)) : t.Image.AsSpan((int)skip).ToArray();
        d.Bytes += bytes.Length;
        texIndex[key] = d.Textures.Count;
        d.Textures.Add(new TexBytes { Name = t.Name, Width = w, Height = h, Format = t.Format, Mips = mips - drop, Linear = t.ColorSpace == 0, Data = bytes });
        return d.Textures.Count - 1;
    }
}
