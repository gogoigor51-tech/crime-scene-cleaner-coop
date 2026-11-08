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
    public static float UpperOpen = 26, LowerOpen = -16, Slit = -4;   // degrees from straight ahead, seen from the side
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
        LastMoved = 0; LastCut = 0;
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
            var up = H - P; up = (up - right * Vector3.Dot(up, right)).normalized;
            var fwd = Vector3.Cross(right, up);
            if (Vector3.Dot(fwd, (L + R) * 0.5f - H) < 0) fwd = -fwd;   // toward the face
            float radius = ipd * 0.2f;                                  // an eyeball is about a fifth of the eye distance
            var side = new sbyte[a.Vertices];   // +1 upper lid moved, -1 lower lid moved
            int moved = Open(a, L, right, up, fwd, radius, side) + Open(a, R, right, up, fwd, radius, side);
            // The two lids share the vertices where they meet, so sliding them apart only stretched the skin over the
            // eye. The triangles that join an upper-lid vertex to a lower-lid one are the stretched ones: without them
            // there is an opening, and the eyeball behind it shows.
            int cut = 0;
            if (Env.Get("CSCCOOP_MUTANT") != "eyes-stretched")
                for (int sm = 0; sm < a.SubMeshes.Length; sm++)
                {
                    var tri = a.SubMeshes[sm]; var keep = new List<int>(tri.Length);
                    for (int t = 0; t + 2 < tri.Length; t += 3)
                    {
                        int s0 = side[tri[t]], s1 = side[tri[t + 1]], s2 = side[tri[t + 2]];
                        bool hi = s0 > 0 || s1 > 0 || s2 > 0, lo = s0 < 0 || s1 < 0 || s2 < 0;
                        if (hi && lo) { cut++; continue; }
                        keep.Add(tri[t]); keep.Add(tri[t + 1]); keep.Add(tri[t + 2]);
                    }
                    a.SubMeshes[sm] = keep.ToArray();
                }
            LastCut += cut;
            LastMoved += moved;
            done.Add(r.Mesh);
        }
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
        var f = a.BindPoses[i]; var m = new Matrix4x4();
        for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) m[row + col * 4] = f[row * 4 + col];
        Vector4 c = m.inverse.GetColumn(3);
        return new Vector3(c.x, c.y, c.z);
    }

    static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }

    static int Open(MeshArrays a, Vector3 c, Vector3 right, Vector3 up, Vector3 fwd, float r, sbyte[] side)
    {
        int moved = 0;
        const float Top = 75, Bottom = -65;
        for (int v = 0; v < a.Vertices; v++)
        {
            var p = new Vector3(a.Pos[v * 3], a.Pos[v * 3 + 1], a.Pos[v * 3 + 2]) - c;
            float x = Vector3.Dot(p, right), y = Vector3.Dot(p, up), z = Vector3.Dot(p, fwd);
            float dist = p.magnitude;
            if (z <= 0 || dist < r * 0.85f || dist > r * 1.8f || Mathf.Abs(x) > r * 1.3f) continue;
            float phi = Mathf.Atan2(y, z) * Mathf.Rad2Deg;
            if (phi > Top || phi < Bottom) continue;
            // how far this vertex takes part: the middle of the eye most, the corners and the outer shell less
            float w = (1 - Smooth(r * 0.75f, r * 1.3f, Mathf.Abs(x))) * (1 - Smooth(r * 1.35f, r * 1.8f, dist));
            if (w <= 0.001f) continue;
            float target;
            if (phi >= Slit) target = Top - (Top - phi) * (Top - UpperOpen) / (Top - Slit);   // [Slit, Top] -> [UpperOpen, Top]
            else target = Bottom + (phi - Bottom) * (LowerOpen - Bottom) / (Slit - Bottom);   // [Bottom, Slit] -> [Bottom, LowerOpen]
            float delta = (target - phi) * w;
            if (Mathf.Abs(delta) < 0.01f) continue;
            var rot = Quaternion.AngleAxis(-delta, right);   // positive delta: up, toward +up from +fwd
            var np = c + rot * p;
            a.Pos[v * 3] = np.x; a.Pos[v * 3 + 1] = np.y; a.Pos[v * 3 + 2] = np.z;
            if (a.Normal != null)
            {
                var n = rot * new Vector3(a.Normal[v * 3], a.Normal[v * 3 + 1], a.Normal[v * 3 + 2]);
                a.Normal[v * 3] = n.x; a.Normal[v * 3 + 1] = n.y; a.Normal[v * 3 + 2] = n.z;
            }
            side[v] = (sbyte)(phi >= Slit ? 1 : -1);
            moved++;
        }
        return moved;
    }
}
