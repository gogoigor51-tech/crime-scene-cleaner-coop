using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CscCoop.Skins;

// A reader for the game's own asset files (Unity 2021.3 serialized files, format 22: levelN, sharedassetsN.assets and
// their .resS streams), so a person from another job can be built without loading that job. Release builds keep no
// type trees, so the few object layouts the skins need are written out in AssetObjects; this class reads the file
// table only and hands out one object's bytes at a time. No Unity types: tools/assetdump tests it outside the game.
public sealed class AssetFile
{
    public readonly string FilePath;
    public readonly string Version;
    public readonly List<string> Externals = new();      // fileID 1.. -> path (as the game stores it)
    public readonly Dictionary<long, Obj> Objects = new();
    readonly long dataOffset;

    public readonly struct Obj
    {
        public readonly long Offset; public readonly uint Size; public readonly int ClassId;
        public Obj(long o, uint s, int c) { Offset = o; Size = s; ClassId = c; }
    }

    public AssetFile(string path)
    {
        FilePath = path;
        using var fs = File.OpenRead(path);
        var head = new byte[48];
        fs.Read(head, 0, head.Length);
        uint version = BE32(head, 8);
        if (version < 22) throw new InvalidDataException($"{path}: format {version}, expected 22");
        if (head[16] != 0) throw new InvalidDataException($"{path}: big-endian data");
        uint metaSize = BE32(head, 20);
        dataOffset = (long)BE64(head, 32);
        var meta = new byte[metaSize];
        fs.Read(meta, 0, meta.Length);
        var r = new Reader(meta);
        Version = r.CString();
        r.I32();                       // target platform
        bool typeTree = r.Bool();
        int typeCount = r.I32();
        var classOfType = new int[typeCount];
        for (int i = 0; i < typeCount; i++)
        {
            int cls = r.I32();
            r.Bool();                  // stripped
            r.I16();                   // script type index
            if (cls == 114) r.Skip(16); // script id
            r.Skip(16);                // old type hash
            if (typeTree) throw new InvalidDataException($"{path}: has type trees (not expected in this game)");
            classOfType[i] = cls;
        }
        int objCount = r.I32();
        for (int i = 0; i < objCount; i++)
        {
            r.Align();
            long pathId = r.I64();
            long start = r.I64();
            uint size = r.U32();
            int type = r.I32();
            Objects[pathId] = new Obj(dataOffset + start, size, type >= 0 && type < typeCount ? classOfType[type] : -1);
        }
        int scripts = r.I32();
        for (int i = 0; i < scripts; i++) { r.I32(); r.Align(); r.I64(); }
        int ext = r.I32();
        for (int i = 0; i < ext; i++)
        {
            r.CString();               // empty
            r.Skip(16);                // guid
            r.I32();                   // type
            Externals.Add(r.CString());
        }
    }

    // One object's bytes, read from the file when asked.
    public Reader Read(long pathId)
    {
        if (!Objects.TryGetValue(pathId, out var o)) throw new KeyNotFoundException($"{FilePath}: no object {pathId}");
        var buf = new byte[o.Size];
        lock (this)
        {
            stream ??= new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            stream.Seek(o.Offset, SeekOrigin.Begin);
            ReadFully(stream, buf);
        }
        return new Reader(buf);
    }

    FileStream stream;   // kept open while the file is in use (one object at a time, many small reads)
    public void Close() { lock (this) { stream?.Dispose(); stream = null; } }

    public IEnumerable<long> OfClass(int classId)
    {
        foreach (var kv in Objects) if (kv.Value.ClassId == classId) yield return kv.Key;
    }

    // Bytes from a stream file next to this one (a mesh's or texture's StreamingInfo: path "sharedassets3.assets.resS").
    public byte[] ReadStream(string streamPath, long offset, uint size)
    {
        string p = Path.Combine(Path.GetDirectoryName(FilePath) ?? "", Path.GetFileName(streamPath));
        var buf = new byte[size];
        using var fs = File.OpenRead(p);
        fs.Seek(offset, SeekOrigin.Begin);
        ReadFully(fs, buf);
        return buf;
    }

    static void ReadFully(Stream s, byte[] buf)
    {
        int got = 0;
        while (got < buf.Length) { int n = s.Read(buf, got, buf.Length - got); if (n <= 0) throw new EndOfStreamException(); got += n; }
    }

    static uint BE32(byte[] b, int i) => (uint)(b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3]);
    static ulong BE64(byte[] b, int i) => (ulong)BE32(b, i) << 32 | BE32(b, i + 4);
}

// Little-endian reader with Unity's 4-byte alignment after strings, byte arrays and bools.
public sealed class Reader
{
    readonly byte[] b;
    public int Pos;
    public Reader(byte[] data) { b = data; }
    public int Length => b.Length;
    public void Skip(int n) => Pos += n;
    public void Align() => Pos = (Pos + 3) & ~3;
    public byte U8() => b[Pos++];
    public bool Bool() => b[Pos++] != 0;
    public short I16() { var v = BitConverter.ToInt16(b, Pos); Pos += 2; return v; }
    public ushort U16() { var v = BitConverter.ToUInt16(b, Pos); Pos += 2; return v; }
    public int I32() { var v = BitConverter.ToInt32(b, Pos); Pos += 4; return v; }
    public uint U32() { var v = BitConverter.ToUInt32(b, Pos); Pos += 4; return v; }
    public long I64() { var v = BitConverter.ToInt64(b, Pos); Pos += 8; return v; }
    public ulong U64() { var v = BitConverter.ToUInt64(b, Pos); Pos += 8; return v; }
    public float F32() { var v = BitConverter.ToSingle(b, Pos); Pos += 4; return v; }
    public string CString() { int e = Array.IndexOf(b, (byte)0, Pos); var s = Encoding.UTF8.GetString(b, Pos, e - Pos); Pos = e + 1; return s; }
    public string Str() { int n = I32(); var s = Encoding.UTF8.GetString(b, Pos, n); Pos += n; Align(); return s; }
    public byte[] Bytes() { int n = I32(); var a = new byte[n]; Buffer.BlockCopy(b, Pos, a, 0, n); Pos += n; Align(); return a; }
    public PPtr Ptr() => new(I32(), I64());
    public float[] Floats(int n) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = F32(); return a; }
}

public readonly struct PPtr
{
    public readonly int File; public readonly long Id;
    public PPtr(int f, long id) { File = f; Id = id; }
    public bool IsNull => Id == 0;
    public override string ToString() => $"{File}:{Id}";
}
