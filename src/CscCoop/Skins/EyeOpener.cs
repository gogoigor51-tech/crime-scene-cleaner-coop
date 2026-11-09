using System;
using System.Collections.Generic;
using UnityEngine;

namespace CscCoop.Skins;

// Opens the eyes of the game's people (experiment, owner's request). They are bodies: almost every face mesh has its
// eyelids modelled shut and no blend shapes to open them. The eyeballs are there under the lids (CC_Base_Eye), and
// the skeleton says where they are (CC_Base_L_Eye / R_Eye). So on the body mesh, before it's made, the vertices of
// the eyelid shell in front of each eyeball (between 0.85 and 1.8 eyeball radii from its centre) are slid round the
// eyeball like a real lid: the upper lid up, the lower lid down, most in the middle of the eye and less toward its
// corners, fading out toward the brow and cheek. Normals turn with them. Everything in the mesh's own space.
public static class EyeOpener
{
    public static bool Enabled = Env.Get("CSCCOOP_MUTANT") != "eyes-closed";
    public static float UpperOpen = 15, LowerOpen = -16, Slit = -4;   // degrees from straight ahead, seen from the side
    public static int LastMoved, LastCut;

    // CSCCOOP_EYES="upper,lower,slit" (tests): other angles.
    static EyeOpener()
    {
        var e = Env.Get("CSCCOOP_EYES").Split(',');
        if (e.Length == 3 && float.TryParse(e[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var u)
            && float.TryParse(e[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var l)
            && float.TryParse(e[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sl)) { UpperOpen = u; LowerOpen = l; Slit = sl; }
    }

    static readonly string[] LeftEye = { "CC_Base_L_Eye", "eye_l" }, RightEye = { "CC_Base_R_Eye", "eye_r" };
    static readonly string[] Head = { "CC_Base_Head", "head" }, Hip = { "CC_Base_Hip", "pelvis" };

    public static void Apply(PersonData d)
    {
        LastMoved = 0; LastCut = 0; LastSmoothed = 0;
        if (!Enabled) return;
        var done = new HashSet<int>();
        foreach (var r in d.Renderers)
        {
            var a = d.Meshes[r.Mesh];
            if (done.Contains(r.Mesh) || a.Pos == null || a.Name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) < 0) continue;
            int l = Find(d, r, LeftEye), rt = Find(d, r, RightEye), h = Find(d, r, Head), p = Find(d, r, Hip);
            if (l < 0 || rt < 0 || h < 0 || p < 0) continue;
            Vector3 L = BonePos(a, l), R = BonePos(a, rt), H = BonePos(a, h), P = BonePos(a, p);
            var right = R - L;
            float ipd = right.magnitude;
            if (ipd < 1e-5f) continue;
            right /= ipd;
            // up: the head's own axis nearest to hip -> head. Some people bow their head in the bind pose (SC Mother,
            // Senator); with the body's up the lids opened round a tilted line and the middle of the upper one stayed
            // down as a bump.
            var up = HeadUp(a, h, H - P); up = (up - right * Vector3.Dot(up, right)).normalized;
            LastTilt = Vector3.Angle(up, H - P - right * Vector3.Dot(H - P, right));
            var fwd = Vector3.Cross(right, up);
            if (Vector3.Dot(fwd, (L + R) * 0.5f - H) < 0) fwd = -fwd;   // toward the face
            float radius = ipd * 0.2f;                                  // an eyeball is about a fifth of the eye distance
            if (Env.Get("CSCCOOP_EYES_DUMP") == "1") Dump(d, a, L, right, up, fwd, ipd * 0.2f);
            var side = new sbyte[a.Vertices];   // +1 upper lid moved, -1 lower lid moved
            var piece = Pieces(a); pieceOf = piece;
            int moved = OpenEye(d, a, piece, L, right, up, fwd, radius, side);
            var eyeL = (c: L, slit: LastSlit, front: LastFront * radius);
            moved += OpenEye(d, a, piece, R, right, up, fwd, radius, side);
            var eyeR = (c: R, slit: LastSlit, front: LastFront * radius);
            // The two lids share the vertices where they meet, so sliding them apart only stretched the skin over the
            // eye. The triangles that join an upper-lid vertex to a lower-lid one are the stretched ones: without them
            // there is an opening, and the eyeball behind it shows.
            int cut = 0;
            edge = new bool[a.Vertices];
            if (Env.Get("CSCCOOP_MUTANT") != "eyes-stretched")
                for (int sm = 0; sm < a.SubMeshes.Length; sm++)
                {
                    var tri = a.SubMeshes[sm]; var keep = new List<int>(tri.Length);
                    for (int t = 0; t + 2 < tri.Length; t += 3)
                    {
                        int s0 = side[tri[t]], s1 = side[tri[t + 1]], s2 = side[tri[t + 2]];
                        bool hi = s0 > 0 || s1 > 0 || s2 > 0, lo = s0 < 0 || s1 < 0 || s2 < 0;
                        if (hi && lo) { cut++; edge[tri[t]] = edge[tri[t + 1]] = edge[tri[t + 2]] = true; continue; }
                        keep.Add(tri[t]); keep.Add(tri[t + 1]); keep.Add(tri[t + 2]);
                    }
                    a.SubMeshes[sm] = keep.ToArray();
                }
            if (cut > 0 && Env.Get("CSCCOOP_MUTANT") != "eyes-sharp")
                foreach (var e in new[] { eyeL, eyeR }) SmoothEdge(a, side, e.c, e.slit, right, up, fwd, radius);
            // The eyeballs: placed here, where the skin is (the head bone's own space, from its bind pose), not from
            // the copy's bones at run time: on some people (Art BlackFemale01) those put the eyeballs out of the face.
            // As big as fits behind where the shut lids were, at most a fifth of the eye distance.
            if (r.Bones[h] >= 0)
                foreach (var e in new[] { eyeL, eyeR })
                {
                    // and no bigger than the skin left round the opening: at the outer corners the skin comes in
                    // closer than the eyeball, which poked out there (seen from the side)
                    float inside = Inside(a, e.c, right, up, fwd, radius);
                    float big = Mathf.Clamp(Mathf.Min(e.front - radius * 0.15f, inside * 0.97f), radius * 0.7f, radius);
                    if (Env.Get("CSCCOOP_EYES_DUMP") == "1") Plugin.L.LogInfo(System.FormattableString.Invariant($"EYEFIT '{d.Person?.Display}' front {e.front / radius:F2}r skin inside {inside / radius:F2}r -> eyeball {big / radius:F2}r"));
                    if (Env.Get("CSCCOOP_MUTANT") == "eyes-bone-placed") break;
                    var bind = BindPose(a, h);
                    d.Eyes.Add(new EyeFit
                    {
                        Head = r.Bones[h], Pos = bind.MultiplyPoint3x4(e.c),
                        Rot = Quaternion.LookRotation(bind.MultiplyVector(fwd), bind.MultiplyVector(up)),
                        Radius = bind.MultiplyVector(right * big).magnitude, Socket = bind.MultiplyVector(right * radius).magnitude,
                    });
                }
            LastCut += cut;
            LastMoved += moved;
            done.Add(r.Mesh);
        }
    }

    // The nearest the skin (a kept triangle's vertex, in front of the eye, not the lashes) comes to the eye's centre.
    static float Inside(MeshArrays a, Vector3 c, Vector3 right, Vector3 up, Vector3 fwd, float r)
    {
        float best = float.MaxValue;
        var counts = new Dictionary<int, int>();
        foreach (var g in pieceOf) counts[g] = counts.TryGetValue(g, out var n) ? n + 1 : 1;
        foreach (var tri in a.SubMeshes)
            foreach (var v in tri)
            {
                if (counts[pieceOf[v]] < 200) continue;
                Local(a, v, c, right, up, fwd, out var x, out _, out var dist, out var z);
                if (z > r * 0.2f && Mathf.Abs(x) < r * 1.6f) best = Mathf.Min(best, dist);
            }
        return best;
    }
    static int[] pieceOf;

    static bool[] edge;   // vertices of the triangles cut out: the lids' new edges

    // The cut follows the mesh's triangles, so the lids' edges came out ragged: sharp teeth (owner's test). Each edge
    // vertex is put on a smooth almond (seen from the front): the upper and lower edges meet at the corners, as far
    // open in the middle as the lids were opened there. Then the lid round it is smoothed (its angle round the eye
    // averaged with its neighbours', a few times), so no fold or spike is left behind the edge. Vertices at the same
    // place (texture seams) move together.
    static void SmoothEdge(MeshArrays a, sbyte[] side, Vector3 c, float slit, Vector3 right, Vector3 up, Vector3 fwd, float r)
    {
        var mine = new List<int>();
        float xc = 0;
        for (int v = 0; v < a.Vertices; v++)
        {
            if (side[v] == 0) continue;
            Local(a, v, c, right, up, fwd, out var x, out _, out var dist, out _);
            if (dist > r * 2.5f) continue;   // the other eye's
            mine.Add(v);
            if (edge[v]) xc = Mathf.Max(xc, Mathf.Abs(x));
        }
        if (mine.Count == 0 || xc < r * 0.3f) return;
        // how far open each lid is in the middle: the median of its edge there
        var hiMid = new List<float>(); var loMid = new List<float>();
        foreach (var v in mine)
        {
            if (!edge[v]) continue;
            Local(a, v, c, right, up, fwd, out var x, out var phi, out _, out _);
            if (Mathf.Abs(x) < xc * 0.35f) (side[v] > 0 ? hiMid : loMid).Add(phi);
        }
        if (hiMid.Count == 0 || loMid.Count == 0) return;
        hiMid.Sort(); loMid.Sort();
        float hi = hiMid[hiMid.Count / 2], lo = loMid[loMid.Count / 2];
        float Curve(float x, int s)
        {
            float f = Mathf.Pow(Mathf.Clamp01(1 - x * x / (xc * xc)), 0.55f);
            return slit + ((s > 0 ? hi : lo) - slit) * f;
        }
        // same place, same vertex
        var key = new Dictionary<Vector3Int, List<int>>();
        Vector3Int K(int v) => new Vector3Int(Mathf.RoundToInt(a.Pos[v * 3] * 1e5f), Mathf.RoundToInt(a.Pos[v * 3 + 1] * 1e5f), Mathf.RoundToInt(a.Pos[v * 3 + 2] * 1e5f));
        foreach (var v in mine) { var k = K(v); if (!key.TryGetValue(k, out var l)) key[k] = l = new List<int>(); l.Add(v); }
        var target = new Dictionary<int, float>();
        foreach (var v in mine)
        {
            Local(a, v, c, right, up, fwd, out var x, out var phi, out _, out _);
            target[v] = edge[v] ? Curve(x, side[v]) : phi;
        }
        // neighbours among this lid's moved vertices, by place
        var isMine = new HashSet<int>(mine);
        var nb = new Dictionary<Vector3Int, HashSet<Vector3Int>>();
        var held = new HashSet<Vector3Int>();
        foreach (var tri in a.SubMeshes)
            for (int t = 0; t + 2 < tri.Length; t += 3)
                for (int i = 0; i < 3; i++)
                {
                    int p = tri[t + i], q = tri[t + (i + 1) % 3];
                    // where the moved lid meets skin that stayed: kept as it is, or a seam would open there
                    if (isMine.Contains(p) != isMine.Contains(q)) { held.Add(K(isMine.Contains(p) ? p : q)); continue; }
                    if (!isMine.Contains(p) || side[p] != side[q]) continue;
                    Vector3Int kp = K(p), kq = K(q);
                    if (kp == kq) continue;
                    if (!nb.TryGetValue(kp, out var sp)) nb[kp] = sp = new HashSet<Vector3Int>(); sp.Add(kq);
                    if (!nb.TryGetValue(kq, out var sq)) nb[kq] = sq = new HashSet<Vector3Int>(); sq.Add(kp);
                }
        var phiAt = new Dictionary<Vector3Int, float>();
        var fixedAt = new HashSet<Vector3Int>();
        foreach (var kv in key)
        {
            float sum = 0; bool fix = false;
            foreach (var v in kv.Value) { sum += target[v]; fix |= edge[v]; }
            phiAt[kv.Key] = sum / kv.Value.Count;
            if (fix || held.Contains(kv.Key)) fixedAt.Add(kv.Key);
        }
        for (int it = 0; it < 4; it++)
        {
            var next = new Dictionary<Vector3Int, float>(phiAt);
            foreach (var kv in nb)
            {
                if (fixedAt.Contains(kv.Key) || kv.Value.Count == 0) continue;
                float avg = 0; foreach (var n in kv.Value) avg += phiAt[n];
                avg /= kv.Value.Count;
                next[kv.Key] = Mathf.Lerp(phiAt[kv.Key], avg, 0.5f);
            }
            phiAt = next;
        }
        foreach (var kv in key)
            foreach (var v in kv.Value)
            {
                Local(a, v, c, right, up, fwd, out _, out var phi, out _, out _);
                float delta = phiAt[kv.Key] - phi;
                if (Mathf.Abs(delta) > 0.01f) Turn(a, v, c, Quaternion.AngleAxis(-delta, right));
            }
        LastSmoothed += mine.Count;
    }
    public static int LastSmoothed;

    // Tests: the connected pieces of mesh around the left eye, with where they sit (phi: degrees up from straight ahead).
    static void Dump(PersonData d, MeshArrays a, Vector3 c, Vector3 right, Vector3 up, Vector3 fwd, float r)
    {
        var parent = new int[a.Vertices]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        bool In(int v, out float phi, out float dist, out float x)
        {
            var p = new Vector3(a.Pos[v * 3], a.Pos[v * 3 + 1], a.Pos[v * 3 + 2]) - c;
            x = Vector3.Dot(p, right); float y = Vector3.Dot(p, up), z = Vector3.Dot(p, fwd); dist = p.magnitude;
            phi = Mathf.Atan2(y, z) * Mathf.Rad2Deg;
            return z > -r * 0.2f && dist < r * 2.6f && Mathf.Abs(x) < r * 1.6f;
        }
        for (int sm = 0; sm < a.SubMeshes.Length; sm++)
        {
            var t = a.SubMeshes[sm];
            for (int i = 0; i + 2 < t.Length; i += 3) { parent[Root(t[i])] = Root(t[i + 1]); parent[Root(t[i + 1])] = Root(t[i + 2]); }
        }
        var subOf = new int[a.Vertices];
        for (int sm = 0; sm < a.SubMeshes.Length; sm++) foreach (var v in a.SubMeshes[sm]) subOf[v] = sm;
        var groups = new Dictionary<int, List<int>>();
        for (int v = 0; v < a.Vertices; v++) if (In(v, out _, out _, out _)) { int g = Root(v); if (!groups.TryGetValue(g, out var l)) groups[g] = l = new List<int>(); l.Add(v); }
        foreach (var kv in groups)
        {
            float mp = 0, md = 0, minD = 9, maxD = 0, rootPhi = 0, tipPhi = 0, minP = 999, maxP = -999, mx = 0;
            foreach (var v in kv.Value)
            {
                In(v, out var phi, out var dist, out var x);
                mp += phi; md += dist; mx += x; minP = Mathf.Min(minP, phi); maxP = Mathf.Max(maxP, phi);
                if (dist < minD) { minD = dist; rootPhi = phi; }
                if (dist > maxD) { maxD = dist; tipPhi = phi; }
            }
            int n = kv.Value.Count, total = 0; float far = 0, fx = 0, fz = 9;
            for (int v = 0; v < a.Vertices; v++) if (Root(v) == kv.Key)
            {
                total++; var q = new Vector3(a.Pos[v * 3], a.Pos[v * 3 + 1], a.Pos[v * 3 + 2]) - c;
                far = Mathf.Max(far, q.magnitude / r); fx = Mathf.Max(fx, Mathf.Abs(Vector3.Dot(q, right)) / r); fz = Mathf.Min(fz, Vector3.Dot(q, fwd) / r);
            }
            Plugin.L.LogInfo(System.FormattableString.Invariant($"EYEDUMP '{d.Person?.Display}' piece total {total} far {far:F2}r |x| {fx:F2}r z {fz:F2}r, in box {n} (sub {subOf[kv.Value[0]]}): phi mean {mp / n:F1} min {minP:F1} max {maxP:F1}, x mean {mx / n / r:F2}r, dist {minD / r:F2}-{maxD / r:F2}r (root phi {rootPhi:F1}, tip phi {tipPhi:F1})"));
        }
    }

    // Which connected piece of the mesh each vertex is in (vertices joined by triangles).
    static int[] Pieces(MeshArrays a)
    {
        var parent = new int[a.Vertices]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        foreach (var t in a.SubMeshes)
            for (int i = 0; i + 2 < t.Length; i += 3) { parent[Root(t[i])] = Root(t[i + 1]); parent[Root(t[i + 1])] = Root(t[i + 2]); }
        for (int i = 0; i < parent.Length; i++) parent[i] = Root(i);
        return parent;
    }

    sealed class Lash { public List<int> Verts = new(); public float RootPhi, RootX, MinDist = float.MaxValue; }

    // One eye. The eyelashes are loose pieces of the body mesh (cards, each twice), the upper row rooted on the upper
    // lid's edge, the lower on the lower's. Where the lids meet lies between the two rows' roots: fixed at -4 degrees
    // it was too high on most people (the lashes' roots were at -5 to -35), so the upper lashes and part of the upper
    // lid went down with the lower lid (owner's test: two rows of lashes under the eye). The lashes turn whole, with
    // the lid edge they hang on.
    static int OpenEye(PersonData d, MeshArrays a, int[] piece, Vector3 c, Vector3 right, Vector3 up, Vector3 fwd, float r, sbyte[] side)
    {
        var count = new Dictionary<int, int>();
        foreach (var g in piece) count[g] = count.TryGetValue(g, out var n) ? n + 1 : 1;
        var lashes = new Dictionary<int, Lash>();
        var outside = new HashSet<int>();
        for (int v = 0; v < a.Vertices; v++)
        {
            Local(a, v, c, right, up, fwd, out var x, out var phi, out var dist, out var z);
            // long upper lashes reach 2.6 radii out (SC Mother): with a tighter box they counted as outside and stayed
            bool near = z > -r * 0.2f && dist < r * 3.5f && Mathf.Abs(x) < r * 2.3f;   // and 1.94 radii to the side
            int g = piece[v];
            if (!near) { if (lashes.ContainsKey(g) || count[g] < 200) outside.Add(g); continue; }
            if (count[g] >= 200) continue;
            if (!lashes.TryGetValue(g, out var l)) lashes[g] = l = new Lash();
            l.Verts.Add(v);
            if (dist < l.MinDist) { l.MinDist = dist; l.RootPhi = phi; l.RootX = x; }
        }
        var rows = new List<Lash>();
        foreach (var kv in lashes)
        {
            if (outside.Contains(kv.Key) || kv.Value.Verts.Count < 20) continue;
            float lo = 999, hi = -999, mx = 0;
            foreach (var v in kv.Value.Verts) { Local(a, v, c, right, up, fwd, out var x, out var phi, out _, out _); lo = Mathf.Min(lo, phi); hi = Mathf.Max(hi, phi); mx += x; }
            mx /= kv.Value.Verts.Count;
            if (hi - lo < 50 && Mathf.Abs(mx) < r * 0.6f && kv.Value.MinDist > r * 0.9f)
            {
                // A strip runs along the lid: its root is the middle of its edge nearest the eye, not the one vertex
                // nearest (at a corner, Art AsianFemale02: its weight came out small and the lashes hardly moved).
                var l = kv.Value; var band = Vector3.zero; int bn = 0; float far = 0;
                foreach (var v in l.Verts) far = Mathf.Max(far, (P(a, v) - c).magnitude);
                foreach (var v in l.Verts) if ((P(a, v) - c).magnitude <= l.MinDist + (far - l.MinDist) * 0.25f) { band += P(a, v); bn++; }
                if (bn > 0 && Env.Get("CSCCOOP_MUTANT") != "eyes-lash-corner")
                {
                    var b = band / bn - c;
                    l.RootX = Vector3.Dot(b, right);
                    l.RootPhi = Mathf.Atan2(Vector3.Dot(b, up), Vector3.Dot(b, fwd)) * Mathf.Rad2Deg;
                }
                rows.Add(l);
            }
        }
        // two rows: split the roots at their widest gap
        float slit = Slit;
        if (rows.Count >= 2 && Env.Get("CSCCOOP_MUTANT") != "eyes-fixed-slit")
        {
            // every card is there twice (both faces) and a row can be several cards: a gap under 4 degrees is within
            // one row, so with one row found the fixed slit stays
            rows.Sort((p, q) => p.RootPhi.CompareTo(q.RootPhi));
            int cut = 1; float gap = -1;
            for (int i = 1; i < rows.Count; i++) if (rows[i].RootPhi - rows[i - 1].RootPhi > gap) { gap = rows[i].RootPhi - rows[i - 1].RootPhi; cut = i; }
            if (gap >= 4) slit = (rows[cut].RootPhi + rows[cut - 1].RootPhi) * 0.5f;
        }
        LastSlit = slit;
        // where the shut lids are straight in front of the eye bone, along the slit: the eyeball must stay behind it
        float front = float.MaxValue;
        for (int v = 0; v < a.Vertices; v++)
        {
            if (count[piece[v]] < 200) continue;   // lashes and small pieces: not the skin
            Local(a, v, c, right, up, fwd, out var x, out var phi, out var dist, out var z);
            if (z > 0 && Mathf.Abs(x) < r * 0.35f && Mathf.Abs(phi - slit) < 12) front = Mathf.Min(front, dist);
        }
        LastFront = front / r;
        if (Env.Get("CSCCOOP_EYES_DUMP") == "1") Plugin.L.LogInfo(System.FormattableString.Invariant($"EYEFRONT '{d.Person?.Display}' shut lids {front / r:F2}r in front of the eye bone (r {r:F4})"));
        var isLash = new HashSet<int>();
        foreach (var l in rows) foreach (var v in l.Verts) isLash.Add(v);
        int moved = 0;
        // the lashes: each card turned whole by what its root's lid edge does
        foreach (var l in rows)
        {
            float w = 1 - Smooth(r * 0.75f, r * 1.3f, Mathf.Abs(l.RootX));
            float delta = (Target(l.RootPhi, slit) - l.RootPhi) * w;
            var rot = Quaternion.AngleAxis(-delta, right);
            foreach (var v in l.Verts) { Turn(a, v, c, rot); moved++; }
            // On a shut eye the upper lashes hang down over the lid; carried up with it they stood like a curtain in
            // front of the eye, and how far down they hang differs from person to person. So each card is also turned
            // about its own root until it points as an open eye's lashes do (seen from the side): the upper ones
            // forward and up, the lower ones forward and down.
            if (w < 0.05f) continue;
            // a card is a strip along the lid: its direction is from its root edge (nearest the eye) to its tip edge
            var root = RootPos(a, l, c);
            var tip = Direction(a, l, c);
            float now = Mathf.Atan2(Vector3.Dot(tip, up), Vector3.Dot(tip, fwd)) * Mathf.Rad2Deg;
            float want = l.RootPhi >= slit ? UpperLash : LowerLash;
            float turn = Mathf.DeltaAngle(now, want) * w;
            if (Env.Get("CSCCOOP_MUTANT") == "eyes-lash-carried") turn = 0;
            var swing = Quaternion.AngleAxis(-turn, right);
            foreach (var v in l.Verts) Turn(a, v, root, swing);
            if (Env.Get("CSCCOOP_EYES_DUMP") == "1")
            {
                var t2 = Direction(a, l, c);
                Plugin.L.LogInfo(System.FormattableString.Invariant($"EYELASH '{d.Person?.Display}' {l.Verts.Count}v root phi {l.RootPhi:F1} x {l.RootX / r:F2}r dist {l.MinDist / r:F2}r {(l.RootPhi >= slit ? "upper" : "lower")} w {w:F2} delta {delta:F1} dir {now:F1} -> {Mathf.Atan2(Vector3.Dot(t2, up), Vector3.Dot(t2, fwd)) * Mathf.Rad2Deg:F1} len {tip.magnitude / r:F2}r"));
            }
        }
        // the lids
        for (int v = 0; v < a.Vertices; v++)
        {
            if (isLash.Contains(v)) continue;
            Local(a, v, c, right, up, fwd, out var x, out var phi, out var dist, out var z);
            // The upper lid of older people is thick: its fold lies out to 2 radii and more. Moved less than the
            // lid's edge, the fold hung over the opened eye (owner: bumps in the middle of SC Mother's upper lids).
            bool upperLid = phi >= slit && Env.Get("CSCCOOP_MUTANT") != "eyes-thin-lid";
            float near0 = upperLid ? 1.75f : 1.35f, far0 = upperLid ? 2.4f : 1.8f;
            if (z <= 0 || dist < r * 0.85f || dist > r * far0 || Mathf.Abs(x) > r * 1.3f || phi > Top || phi < Bottom) continue;
            float w = (1 - Smooth(r * 0.75f, r * 1.3f, Mathf.Abs(x))) * (1 - Smooth(r * near0, r * far0, dist));
            if (w <= 0.001f) continue;
            float delta = (Target(phi, slit) - phi) * w;
            if (Mathf.Abs(delta) < 0.01f) continue;
            Turn(a, v, c, Quaternion.AngleAxis(-delta, right));
            side[v] = (sbyte)(phi >= slit ? 1 : -1);
            moved++;
        }
        return moved;
    }

    const float Top = 75, Bottom = -65;
    public static float LastSlit, LastTilt, LastFront;

    // Where a lid point at phi goes: [slit, Top] -> [slit + upper opening, Top], [Bottom, slit] -> [Bottom, slit + lower].
    static float Target(float phi, float slit)
    {
        // the opened lids: the upper never lower than UpperOpen (lids that met low on the eye stayed droopy), the lower
        // a little under where they met
        float up = Mathf.Max(slit + 16, UpperOpen), down = Mathf.Min(slit - 6, LowerOpen);
        return phi >= slit ? Top - (Top - phi) * (Top - up) / (Top - slit) : Bottom + (phi - Bottom) * (down - Bottom) / (slit - Bottom);
    }

    // Which way the lashes of the opened eye point, degrees up from straight ahead, seen from the side.
    public static float UpperLash = 40, LowerLash = -45;

    static Vector3 RootPos(MeshArrays a, Lash l, Vector3 c)
    {
        int best = l.Verts[0]; float bd = float.MaxValue;
        foreach (var v in l.Verts)
        {
            var p = new Vector3(a.Pos[v * 3], a.Pos[v * 3 + 1], a.Pos[v * 3 + 2]);
            float d = (p - c).magnitude;
            if (d < bd) { bd = d; best = v; }
        }
        return new Vector3(a.Pos[best * 3], a.Pos[best * 3 + 1], a.Pos[best * 3 + 2]);
    }

    // From the mean of a card's vertices nearest the eye to the mean of its farthest.
    static Vector3 Direction(MeshArrays a, Lash l, Vector3 c)
    {
        float lo = float.MaxValue, hi = 0;
        foreach (var v in l.Verts) { float d = (P(a, v) - c).magnitude; lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); }
        float band = (hi - lo) * 0.25f;
        Vector3 rs = Vector3.zero, ts = Vector3.zero; int rn = 0, tn = 0;
        foreach (var v in l.Verts)
        {
            var p = P(a, v); float d = (p - c).magnitude;
            if (d <= lo + band) { rs += p; rn++; }
            if (d >= hi - band) { ts += p; tn++; }
        }
        return rn == 0 || tn == 0 ? Vector3.zero : ts / tn - rs / rn;
    }

    static Vector3 P(MeshArrays a, int v) => new Vector3(a.Pos[v * 3], a.Pos[v * 3 + 1], a.Pos[v * 3 + 2]);

    static void Local(MeshArrays a, int v, Vector3 c, Vector3 right, Vector3 up, Vector3 fwd, out float x, out float phi, out float dist, out float z)
    {
        var p = new Vector3(a.Pos[v * 3], a.Pos[v * 3 + 1], a.Pos[v * 3 + 2]) - c;
        x = Vector3.Dot(p, right); float y = Vector3.Dot(p, up); z = Vector3.Dot(p, fwd);
        dist = p.magnitude; phi = Mathf.Atan2(y, z) * Mathf.Rad2Deg;
    }

    static void Turn(MeshArrays a, int v, Vector3 c, Quaternion rot)
    {
        var p = rot * (new Vector3(a.Pos[v * 3], a.Pos[v * 3 + 1], a.Pos[v * 3 + 2]) - c) + c;
        a.Pos[v * 3] = p.x; a.Pos[v * 3 + 1] = p.y; a.Pos[v * 3 + 2] = p.z;
        if (a.Normal == null) return;
        var n = rot * new Vector3(a.Normal[v * 3], a.Normal[v * 3 + 1], a.Normal[v * 3 + 2]);
        a.Normal[v * 3] = n.x; a.Normal[v * 3 + 1] = n.y; a.Normal[v * 3 + 2] = n.z;
    }

    static int Find(PersonData d, RendererInfo r, string[] names)
    {
        for (int i = 0; i < r.Bones.Length; i++)
        {
            int n = r.Bones[i];
            if (n < 0) continue;
            foreach (var name in names) if (d.Nodes[n].Name == name) return i;
        }
        return -1;
    }

    // The bone's position in the mesh's space: the inverse of its bind pose (stored row by row).
    static Vector3 BonePos(MeshArrays a, int i)
    {
        Vector4 c = BoneToMesh(a, i).GetColumn(3);
        return new Vector3(c.x, c.y, c.z);
    }

    static Matrix4x4 BoneToMesh(MeshArrays a, int i) => BindPose(a, i).inverse;

    static Matrix4x4 BindPose(MeshArrays a, int i)
    {
        var f = a.BindPoses[i]; var m = new Matrix4x4();
        for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) m[row + col * 4] = f[row * 4 + col];
        return m;
    }

    // Of the head bone's three axes, the one nearest to the body's up, turned to point up.
    public static Vector3 HeadUp(MeshArrays a, int head, Vector3 bodyUp)
    {
        var m = BoneToMesh(a, head);
        return BestAxis(new Vector3[] { m.GetColumn(0), m.GetColumn(1), m.GetColumn(2) }, bodyUp);
    }

    public static Vector3 BestAxis(Vector3[] axes, Vector3 bodyUp)
    {
        if (Env.Get("CSCCOOP_MUTANT") == "eyes-body-up") return bodyUp.normalized;
        Vector3 best = bodyUp.normalized; float bd = 0;
        foreach (var ax in axes)
        {
            var n = ax.normalized; float dot = Vector3.Dot(n, bodyUp.normalized);
            if (Mathf.Abs(dot) > bd) { bd = Mathf.Abs(dot); best = dot < 0 ? -n : n; }
        }
        return bd > 0.5f ? best : bodyUp.normalized;   // no axis near up (odd rig): the body's up
    }

    static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }

}
