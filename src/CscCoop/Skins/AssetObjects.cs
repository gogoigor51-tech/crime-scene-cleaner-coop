using System;
using System.Collections.Generic;
using System.IO;

namespace CscCoop.Skins;

// The object layouts the skins read, as Unity 2021.3 writes them in a release build (no type trees). Each Read* reads
// one object from the start of its bytes; fields not needed are skipped in place, since the next ones follow them.
public static class ClassId
{
    public const int GameObject = 1, Transform = 4, Material = 21, Texture2D = 28, Mesh = 43, Animator = 95, SkinnedMeshRenderer = 137;
}

public sealed class GameObjectData
{
    public string Name = ""; public bool Active; public List<PPtr> Components = new();
    public static GameObjectData Read(Reader r)
    {
        var g = new GameObjectData();
        int n = r.I32();
        for (int i = 0; i < n; i++) g.Components.Add(r.Ptr());
        r.I32();                        // layer
        g.Name = r.Str();
        r.U16();                        // tag
        g.Active = r.Bool();
        return g;
    }
}

public sealed class TransformData
{
    public PPtr GameObject, Father; public float[] Rot = new float[4], Pos = new float[3], Scale = new float[3];
    public List<PPtr> Children = new();
    public static TransformData Read(Reader r)
    {
        var t = new TransformData { GameObject = r.Ptr(), Rot = r.Floats(4), Pos = r.Floats(3), Scale = r.Floats(3) };
        int n = r.I32();
        for (int i = 0; i < n; i++) t.Children.Add(r.Ptr());
        t.Father = r.Ptr();
        return t;
    }
}

public sealed class SkinnedRendererData
{
    public PPtr GameObject, Mesh, RootBone; public bool Enabled; public List<PPtr> Materials = new(), Bones = new();
    public static SkinnedRendererData Read(Reader r)
    {
        var s = new SkinnedRendererData { GameObject = r.Ptr() };
        s.Enabled = r.Bool();
        r.Skip(9);                      // shadows, probes, motion vectors, ray tracing (bytes)
        r.Align();
        r.U32(); r.I32();               // rendering layer mask, renderer priority
        r.U16(); r.U16();               // lightmap indices
        r.Skip(32);                     // lightmap tiling offsets
        int m = r.I32();
        for (int i = 0; i < m; i++) s.Materials.Add(r.Ptr());
        r.U16(); r.U16();               // static batch info
        r.Ptr(); r.Ptr(); r.Ptr();      // static batch root, probe anchor, light probe volume override
        r.I32(); r.I16(); r.I16();      // sorting layer id, layer, order
        r.I32();                        // quality
        r.Bool(); r.Bool(); r.Align();  // update when offscreen, skinned motion vectors
        s.Mesh = r.Ptr();
        int b = r.I32();
        for (int i = 0; i < b; i++) s.Bones.Add(r.Ptr());
        int w = r.I32(); r.Skip(w * 4); // blend shape weights
        s.RootBone = r.Ptr();
        return s;
    }
}

public sealed class SubMeshData { public uint FirstByte, IndexCount, BaseVertex, FirstVertex, VertexCount; public int Topology; }
public sealed class ChannelData { public byte Stream, Offset, Format, Dimension; }

public sealed class MeshData
{
    public string Name = "";
    public List<SubMeshData> SubMeshes = new();
    public List<float[]> BindPoses = new();          // 16 floats each, column-major (Unity's Matrix4x4f)
    public uint[] BoneNameHashes = Array.Empty<uint>();
    public int IndexFormat;                          // 0 = 16 bit, 1 = 32 bit
    public byte[] IndexBuffer = Array.Empty<byte>();
    public uint VertexCount;
    public List<ChannelData> Channels = new();
    public byte[] VertexBytes = Array.Empty<byte>();
    public long StreamOffset; public uint StreamSize; public string StreamPath = "";
    public byte Compression;

    public static MeshData Read(Reader r)
    {
        var m = new MeshData { Name = r.Str() };
        int subs = r.I32();
        for (int i = 0; i < subs; i++)
        {
            var s = new SubMeshData { FirstByte = r.U32(), IndexCount = r.U32(), Topology = r.I32(), BaseVertex = r.U32(), FirstVertex = r.U32(), VertexCount = r.U32() };
            r.Skip(24);                 // local AABB
            m.SubMeshes.Add(s);
        }
        // blend shapes: vertices (40 bytes), shapes (12), channels (name, hash, frame index, frame count), full weights
        int bv = r.I32(); r.Skip(bv * 40);
        int bs = r.I32(); r.Skip(bs * 12);
        int bc = r.I32(); for (int i = 0; i < bc; i++) { r.Str(); r.U32(); r.I32(); r.I32(); }
        int fw = r.I32(); r.Skip(fw * 4);
        int bp = r.I32();
        for (int i = 0; i < bp; i++) m.BindPoses.Add(r.Floats(16));
        int bh = r.I32();
        m.BoneNameHashes = new uint[bh];
        for (int i = 0; i < bh; i++) m.BoneNameHashes[i] = r.U32();
        r.U32();                        // root bone name hash
        int ba = r.I32(); r.Skip(ba * 24); // bones AABB
        int vw = r.I32(); r.Skip(vw * 4);  // variable bone count weights
        m.Compression = r.U8();
        r.Bool(); r.Bool(); r.Bool(); r.Align();
        m.IndexFormat = r.I32();
        m.IndexBuffer = r.Bytes();
        m.VertexCount = r.U32();
        int ch = r.I32();
        for (int i = 0; i < ch; i++) m.Channels.Add(new ChannelData { Stream = r.U8(), Offset = r.U8(), Format = r.U8(), Dimension = (byte)(r.U8() & 0xF) });
        m.VertexBytes = r.Bytes();
        // compressed mesh: 10 packed vectors and the UV info
        SkipFloatVector(r); SkipFloatVector(r); SkipFloatVector(r); SkipFloatVector(r);   // vertices, uv, normals, tangents
        SkipIntVector(r); SkipIntVector(r); SkipIntVector(r);                              // weights, normal signs, tangent signs
        SkipFloatVector(r);                                                                // float colors
        SkipIntVector(r); SkipIntVector(r);                                                // bone indices, triangles
        r.U32();                        // uv info
        r.Skip(24);                     // local AABB
        r.I32();                        // mesh usage flags
        r.Bytes(); r.Bytes();           // baked convex / triangle collision meshes
        r.F32(); r.F32();               // mesh metrics
        m.StreamOffset = (long)r.U64(); m.StreamSize = r.U32(); m.StreamPath = r.Str();
        return m;
    }

    static void SkipFloatVector(Reader r) { r.U32(); r.F32(); r.F32(); r.Bytes(); r.U8(); r.Align(); }
    static void SkipIntVector(Reader r) { r.U32(); r.Bytes(); r.U8(); r.Align(); }
}

public sealed class MaterialData
{
    public string Name = ""; public PPtr Shader; public string Keywords = ""; public int RenderQueue;
    public Dictionary<string, (PPtr tex, float sx, float sy, float ox, float oy)> Textures = new();
    public Dictionary<string, int> Ints = new();
    public Dictionary<string, float> Floats = new();
    public Dictionary<string, float[]> Colors = new();

    public static MaterialData Read(Reader r)
    {
        var m = new MaterialData { Name = r.Str(), Shader = r.Ptr() };
        // 2021.2 and later: the keywords as two lists, valid (the shader has them) and invalid
        int valid = r.I32(); var kw = new List<string>(); for (int i = 0; i < valid; i++) kw.Add(r.Str());
        int invalid = r.I32(); for (int i = 0; i < invalid; i++) r.Str();
        m.Keywords = string.Join(" ", kw);
        r.U32();                        // lightmap flags
        r.Bool(); r.Bool(); r.Align();  // instancing variants, double sided GI
        m.RenderQueue = r.I32();
        int tags = r.I32(); for (int i = 0; i < tags; i++) { r.Str(); r.Str(); }
        int passes = r.I32(); for (int i = 0; i < passes; i++) r.Str();
        int tex = r.I32();
        for (int i = 0; i < tex; i++) { var k = r.Str(); var p = r.Ptr(); m.Textures[k] = (p, r.F32(), r.F32(), r.F32(), r.F32()); }
        int ints = r.I32(); for (int i = 0; i < ints; i++) { var k = r.Str(); m.Ints[k] = r.I32(); }
        int fl = r.I32(); for (int i = 0; i < fl; i++) { var k = r.Str(); m.Floats[k] = r.F32(); }
        int col = r.I32(); for (int i = 0; i < col; i++) { var k = r.Str(); m.Colors[k] = r.Floats(4); }
        return m;
    }
}

public sealed class TextureData
{
    public string Name = ""; public int Width, Height, Format, MipCount, ColorSpace;
    public byte[] Image = Array.Empty<byte>();
    public long StreamOffset; public uint StreamSize; public string StreamPath = "";

    public static TextureData Read(Reader r)
    {
        var t = new TextureData { Name = r.Str() };
        r.I32();                        // forced fallback format
        r.Bool(); r.Bool(); r.Align();  // downscale fallback, alpha optional
        t.Width = r.I32(); t.Height = r.I32();
        r.I32();                        // complete image size
        r.I32();                        // mips stripped
        t.Format = r.I32(); t.MipCount = r.I32();
        r.Bool(); r.Bool(); r.Bool(); r.Bool(); r.Align();   // readable, preprocessed, ignore master limit, streaming mips
        r.I32();                        // streaming mips priority
        r.I32(); r.I32();               // image count, dimension
        r.Skip(24);                     // texture settings
        r.I32();                        // lightmap format
        t.ColorSpace = r.I32();
        r.Bytes();                      // platform blob
        t.Image = r.Bytes();
        t.StreamOffset = (long)r.U64(); t.StreamSize = r.U32(); t.StreamPath = r.Str();
        return t;
    }
}

// The files of one job, opened when first needed; resolves a PPtr seen in one file to the file it points into.
public sealed class AssetSet
{
    readonly string dataDir;
    readonly Dictionary<string, AssetFile> files = new(StringComparer.OrdinalIgnoreCase);
    public AssetSet(string dataDir) { this.dataDir = dataDir; }

    public AssetFile Get(string name)
    {
        name = Path.GetFileName(name);
        lock (files)
        {
            if (!files.TryGetValue(name, out var f)) files[name] = f = new AssetFile(Path.Combine(dataDir, name));
            return f;
        }
    }

    // Lets go of the open files (a person read, the catalog scanned).
    public void Close() { lock (files) { foreach (var f in files.Values) f.Close(); files.Clear(); } }

    // The file a PPtr points into: 0 = the same file, n = its n-th external. Null for the engine's built-in files.
    public AssetFile Resolve(AssetFile from, PPtr p)
    {
        if (p.File == 0) return from;
        var ext = from.Externals[p.File - 1];
        var name = Path.GetFileName(ext);
        if (name.StartsWith("unity default resources") || name.StartsWith("unity_builtin_extra")) return null;
        if (!File.Exists(Path.Combine(dataDir, name))) return null;
        return Get(name);
    }
}
