using System;
using System.Linq;
using CscCoop.Skins;

// Lists the skinned meshes in a scene file: their game object, mesh (with where its data is) and materials' textures.
var dir = args[0]; var level = args[1]; var filter = args.Length > 2 ? args[2] : "";
if (level == "props")
{
    // Texture property names (and keyword sets) used by the people's materials across every scene file.
    var counts = new System.Collections.Generic.Dictionary<string, int>();
    int people = 0;
    for (int n = 0; n < 60; n++)
    {
        var lv = "level" + n;
        if (!System.IO.File.Exists(System.IO.Path.Combine(dir, lv))) continue;
        var ps = new AssetSet(dir);
        try
        {
            var pf = ps.Get(lv);
            foreach (var id in pf.OfClass(ClassId.SkinnedMeshRenderer))
            {
                var sr = SkinnedRendererData.Read(pf.Read(id));
                if (sr.Bones.Count < 60) continue;
                people++;
                foreach (var mp in sr.Materials)
                {
                    var mf2 = ps.Resolve(pf, mp); if (mf2 == null) continue;
                    var mat = MaterialData.Read(mf2.Read(mp.Id));
                    var key = string.Join(",", mat.Textures.Where(kv => !kv.Value.tex.IsNull).Select(kv => kv.Key).OrderBy(x => x));
                    counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            }
        }
        catch (Exception e) { Console.WriteLine($"{lv}: {e.Message}"); }
    }
    Console.WriteLine($"{people} skinned meshes on people");
    foreach (var kv in counts.OrderByDescending(x => x.Value)) Console.WriteLine($"{kv.Value,5}  {kv.Key}");
    return;
}
var set = new AssetSet(dir);
var f = set.Get(level);
Console.WriteLine($"{level}: {f.Version}, {f.Objects.Count} objects, externals: {string.Join(", ", f.Externals)}");
int shown = 0;
foreach (var id in f.OfClass(ClassId.SkinnedMeshRenderer))
{
    var s = SkinnedRendererData.Read(f.Read(id));
    var goFile = set.Resolve(f, s.GameObject);
    var go = GameObjectData.Read(goFile.Read(s.GameObject.Id));
    if (filter != "" && !go.Name.Contains(filter)) continue;
    var mf = set.Resolve(f, s.Mesh);
    if (mf == null) continue;
    var m = MeshData.Read(mf.Read(s.Mesh.Id));
    Console.WriteLine($"SMR '{go.Name}' bones {s.Bones.Count} mats {s.Materials.Count} -> mesh '{m.Name}' in {System.IO.Path.GetFileName(mf.FilePath)}: {m.VertexCount} verts, {m.SubMeshes.Count} submeshes, idx fmt {m.IndexFormat} ({m.IndexBuffer.Length} B), bindposes {m.BindPoses.Count}, channels [{string.Join(" ", m.Channels.Select((c, i) => c.Dimension == 0 ? "" : $"{i}:s{c.Stream}o{c.Offset}f{c.Format}d{c.Dimension}").Where(x => x != ""))}], vertex bytes {m.VertexBytes.Length}, stream {m.StreamPath}@{m.StreamOffset}+{m.StreamSize}, compression {m.Compression}");
    foreach (var mp in s.Materials)
    {
        var matFile = set.Resolve(f, mp);
        if (matFile == null) { Console.WriteLine("   material (built-in)"); continue; }
        var mat = MaterialData.Read(matFile.Read(mp.Id));
        var texs = mat.Textures.Where(kv => !kv.Value.tex.IsNull).Select(kv =>
        {
            var tf = set.Resolve(matFile, kv.Value.tex);
            if (tf == null) return kv.Key + "=builtin";
            var t = TextureData.Read(tf.Read(kv.Value.tex.Id));
            return $"{kv.Key}={t.Name} {t.Width}x{t.Height} fmt{t.Format} mips{t.MipCount} {(t.StreamSize > 0 ? "stream " + t.StreamSize : "inline " + t.Image.Length)}";
        });
        Console.WriteLine($"   material '{mat.Name}' kw '{mat.Keywords}' queue {mat.RenderQueue} floats {mat.Floats.Count} colors {mat.Colors.Count}: {string.Join("; ", texs)}");
    }
    if (++shown >= 8) break;
}
