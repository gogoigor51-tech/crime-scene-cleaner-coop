using System;
using System.Linq;
using CscCoop.Skins;

// Lists the skinned meshes in a scene file: their game object, mesh (with where its data is) and materials' textures.
var missingSeen = new System.Collections.Generic.HashSet<string>();
var dir = args[0]; var level = args[1]; var filter = args.Length > 2 ? args[2] : "";
if (level == "shapes")
{
    // Which people's meshes keep blend shapes (names with Eye/Blink), and the bones their renderers miss.
    foreach (var p in PersonReader.Catalog(dir))
    {
        var ss = new AssetSet(dir);
        var pf = ss.Get(p.File);
        var names = new System.Collections.Generic.List<string>();
        int shapes = 0;
        var stack = new System.Collections.Generic.Stack<long>(); stack.Push(p.Root);
        while (stack.Count > 0)
        {
            var g = GameObjectData.Read(pf.Read(stack.Pop()));
            foreach (var c in g.Components)
            {
                if (c.File != 0 || !pf.Objects.TryGetValue(c.Id, out var o)) continue;
                if (o.ClassId == ClassId.Transform) { foreach (var ch in TransformData.Read(pf.Read(c.Id)).Children) if (ch.File == 0 && pf.Objects.ContainsKey(ch.Id)) stack.Push(TransformData.Read(pf.Read(ch.Id)).GameObject.Id); }
                if (o.ClassId != ClassId.SkinnedMeshRenderer) continue;
                var sr = SkinnedRendererData.Read(pf.Read(c.Id));
                foreach (var b in sr.Bones)
                    if (b.File == 0 && pf.Objects.ContainsKey(b.Id)) { var bt = TransformData.Read(pf.Read(b.Id)); var bg = GameObjectData.Read(pf.Read(bt.GameObject.Id)); if (!missingSeen.Contains(bg.Name) && p.Display == filter) { } }
                    else if (p.Display == filter) Console.WriteLine($"   bone outside: {b}");
                var mf = ss.Resolve(pf, sr.Mesh); if (mf == null) continue;
                var md = MeshData.Read(mf.Read(sr.Mesh.Id));
                shapes += md.BlendNames.Count;
                names.AddRange(md.BlendNames.Where(n => n.Contains("Eye") || n.Contains("Blink")));
            }
        }
        if (shapes > 0) Console.WriteLine($"{p.Display}: {shapes} shapes; eye: {string.Join(", ", names.Take(8))}");
        ss.Close();
    }
    return;
}
if (level == "nodes")
{
    // Node names of one person (filter = display name), with which are skinned, and the body mesh's blend shape counts.
    var people = PersonReader.Catalog(dir);
    var p = people.Find(x => x.Display == filter);
    var ns = new AssetSet(dir);
    var d = PersonReader.Read(ns, p, 3);
    var used = new System.Collections.Generic.HashSet<int>();
    foreach (var r in d.Renderers) foreach (var b in r.Bones) if (b >= 0) used.Add(b);
    Console.WriteLine(string.Join(" ", d.Nodes.Select((n, i) => (used.Contains(i) ? "*" : "") + n.Name).Take(90)));
    foreach (var r in d.Renderers) Console.WriteLine($"  renderer on '{d.Nodes[r.Node].Name}' mesh '{d.Meshes[r.Mesh].Name}' bones {r.Bones.Length} missing {r.Bones.Count(b => b < 0)} root '{(r.RootBone >= 0 ? d.Nodes[r.RootBone].Name : "-")}'");
    return;
}
if (level == "readall")
{
    // Every person read as the skins window reads them: what fails and why; blend shapes on the body and eyes.
    var people = PersonReader.Catalog(dir);
    int ok = 0;
    foreach (var p in people)
    {
        var rs = new AssetSet(dir);
        try
        {
            var d = PersonReader.Read(rs, p, 3);
            if (d == null) { Console.WriteLine($"FAIL {p.Display} ({p.Key}): no renderers read"); continue; }
            var used = new System.Collections.Generic.HashSet<int>();
            foreach (var r in d.Renderers) foreach (var b in r.Bones) if (b >= 0) used.Add(b);
            int hips = d.Nodes.FindIndex(n => n.Name == "CC_Base_Hip");
            bool hipSkinned = d.Nodes.Select((n, i) => (n, i)).Any(x => x.n.Name == "CC_Base_Hip" && used.Contains(x.i));
            int missingBones = d.Renderers.Sum(r => r.Bones.Count(b => b < 0));
            int missingMats = d.Renderers.Sum(r => r.Materials.Count(m => m < 0));
            var why = hips < 0 ? "no CC_Base_Hip" : !hipSkinned ? "hip not skinned" : "";
            if (why != "" || missingBones > 0 || missingMats > 0) Console.WriteLine($"ODD  {p.Display} ({p.Key}): {why} missing bones {missingBones}, missing materials {missingMats}, renderers {d.Renderers.Count}, nodes {d.Nodes.Count}");
            else ok++;
            if (filter != "" && p.Display.Contains(filter))
            {
                var pf = rs.Get(p.File);
                foreach (var id in pf.OfClass(ClassId.SkinnedMeshRenderer))
                {
                    var sr = SkinnedRendererData.Read(pf.Read(id));
                    var gobj = GameObjectData.Read(pf.Read(sr.GameObject.Id));
                    if (!d.Nodes.Any(n => n.Name == gobj.Name)) continue;
                    var mf = rs.Resolve(pf, sr.Mesh); if (mf == null) continue;
                    var md = MeshData.Read(mf.Read(sr.Mesh.Id));
                    var nz = md.BlendNames.Select((n, i) => (n, w: i < sr.BlendWeights.Length ? sr.BlendWeights[i] : 0)).Where(x => x.w != 0 || x.n.Contains("Eye") || x.n.Contains("Blink")).Take(12);
                    Console.WriteLine($"   {gobj.Name}: {md.BlendNames.Count} shapes, weights {sr.BlendWeights.Length}: {string.Join(", ", nz.Select(x => $"{x.n}={x.w:F0}"))}");
                }
            }
        }
        catch (Exception e) { Console.WriteLine($"FAIL {p.Display} ({p.Key}): {e.GetType().Name} {e.Message}"); }
        finally { rs.Close(); }
    }
    Console.WriteLine($"{ok} of {people.Count} read cleanly");
    return;
}
if (level == "catalog")
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var people = PersonReader.Catalog(dir, Console.WriteLine);
    Console.WriteLine($"{people.Count} people in {sw.ElapsedMilliseconds} ms");
    foreach (var p in people) Console.WriteLine($"  {p.File}#{p.Root}  '{p.Name}' -> '{p.Display}'  meshes {p.Meshes}");
    if (filter != "")
    {
        var pick = people.Find(x => x.Display.Contains(filter));
        sw.Restart();
        var d = PersonReader.Read(new AssetSet(dir), pick, 2);
        Console.WriteLine($"read '{pick.Display}' at mip skip 2: {d.Meshes.Count} meshes, {d.Textures.Count} textures, {d.Bytes / 1024} KB in {sw.ElapsedMilliseconds} ms");
    }
    return;
}
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
