using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CscCoop;

// Another player shown as one of the game's people (the bodies in the jobs) instead of the mod's own figure. A body is
// copied without what makes it a body: the copy keeps the skeleton and the skinned meshes only (no ragdoll, colliders,
// scripts, quest or blood). Its bones are put in their bind pose (SkinnedMeshRenderer bindposes), then every frame
// they follow the PlayerFigure's posed bones: the body's Animator is a humanoid, so GetBoneTransform names the hips,
// spine, arms and legs whatever the rig calls them (the game's people are Character Creator rigs, CC_Base_*).
// The torso and head copy the figure's rotation; arms and legs follow the direction of the figure's limbs, since the
// bodies bind in a T pose and the figure in an A pose.
public sealed class SkinModel
{
    public readonly GameObject Root;
    public readonly string Source;
    public readonly int Meshes, Mapped;

    // Same order as PlayerFigure.Bones: hips, spine, chest, neck, head, upperArm L/R, foreArm L/R, hand L/R,
    // thigh L/R, shin L/R, foot L/R.
    static readonly HumanBodyBones[] Map =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
    };
    // For limbs: the bone whose direction is followed (child in Map), -1 = copy the rotation.
    static readonly int[] Child = { -1, -1, -1, -1, -1, 7, 8, 9, 10, -1, -1, 13, 14, 15, 16, -1, -1 };

    readonly Transform[] bone = new Transform[Map.Length];
    readonly Quaternion[] bindRot = new Quaternion[Map.Length];   // figure space (the copy's root at the origin)
    readonly Quaternion[] bindLocal = new Quaternion[Map.Length];
    readonly Vector3[] bindDir = new Vector3[Map.Length];
    readonly Vector3 hipsBind;

    static GameObject shelf;

    SkinModel(GameObject root, string source, int meshes, int mapped, Vector3 hips)
    { Root = root; Source = source; Meshes = meshes; Mapped = mapped; hipsBind = hips; }

    // The body's model from one of the scene's bodies; null if it has no humanoid Animator.
    public static SkinModel From(GameObject body, Transform parent, out string why)
    {
        why = "";
        var anim = body != null ? body.GetComponentInChildren<Animator>(true) : null;
        if (anim == null || anim.avatar == null || !anim.isHuman) { why = "no humanoid animator"; return null; }
        // Where each humanoid bone sits under the animator, to find the same bone in the copy.
        var paths = new string[Map.Length];
        int mapped = 0;
        for (int k = 0; k < Map.Length; k++)
        {
            var t = anim.GetBoneTransform(Map[k]);
            if (t == null) continue;
            paths[k] = RelPath(anim.transform, t);
            mapped++;
        }
        if (paths[0] == null) { why = "no hips"; return null; }

        var copy = Object.Instantiate(anim.gameObject, Shelf, false);
        copy.name = "CoopSkin " + anim.gameObject.name;
        Strip(copy);
        var bones = new Transform[Map.Length];
        for (int k = 0; k < Map.Length; k++) bones[k] = paths[k] == null ? null : (paths[k] == "" ? copy.transform : copy.transform.Find(paths[k]));
        return Bind(copy, bones, anim.gameObject.name, mapped, parent);
    }

    // An inactive parent: a copy made under it doesn't run Awake/OnEnable of the body's scripts.
    public static Transform Shelf
    {
        get
        {
            if (shelf == null) { shelf = new GameObject("CoopSkinShelf"); shelf.SetActive(false); Object.DontDestroyOnLoad(shelf); }
            return shelf.transform;
        }
    }

    // A person built from the game's files (SkinFiles.Build): bones found by name.
    public static SkinModel FromBuilt(GameObject built, Transform[] humanoid, string source, Transform parent)
    {
        var m = FromBuiltInner(built, humanoid, source, parent);
        m.built = true;
        return m;
    }
    bool built;   // made from the files: its meshes, materials and textures are ours to destroy

    static SkinModel FromBuiltInner(GameObject built, Transform[] humanoid, string source, Transform parent)
    {
        int mapped = 0;
        foreach (var t in humanoid) if (t != null) mapped++;
        built.transform.SetParent(Shelf, false);
        built.SetActive(true);   // active under the inactive shelf until placed
        return Bind(built, humanoid, source, mapped, parent);
    }

    static SkinModel Bind(GameObject copy, Transform[] bones, string source, int mapped, Transform parent)
    {
        // Bind pose: each skinned bone where its mesh was modelled around it.
        copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        copy.transform.localScale = Vector3.one;
        int meshes = 0;
        var placed = new HashSet<IntPtr>();
        foreach (var smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;
            meshes++;
            smr.updateWhenOffscreen = true;
            var bp = mesh.bindposes; var bs = smr.bones;
            var order = new List<int>();
            for (int i = 0; i < bs.Length && i < bp.Length; i++) if (bs[i] != null && placed.Add(bs[i].Pointer)) order.Add(i);
            order.Sort((a, b) => Depth(bs[a]).CompareTo(Depth(bs[b])));   // parents first
            var meshToWorld = smr.transform.localToWorldMatrix;
            foreach (int i in order)
            {
                var m = meshToWorld * bp[i].inverse;
                bs[i].SetPositionAndRotation(m.GetColumn(3), m.rotation);
            }
        }
        var model = new SkinModel(copy, source, meshes, mapped, bones[0].position);
        for (int k = 0; k < Map.Length; k++)
        {
            var t = model.bone[k] = bones[k];
            if (t == null) continue;
            model.bindRot[k] = t.rotation; model.bindLocal[k] = t.localRotation;
            int c = Child[k];
            if (c >= 0 && bones[c] != null) model.bindDir[k] = (bones[c].position - t.position).normalized;
        }
        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.identity;
        return model;
    }

    // Copy of a body: only transforms and renderers stay (joints before rigidbodies: a joint needs its rigidbody).
    static void Strip(GameObject go)
    {
        foreach (var j in go.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
        foreach (var c in go.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue;
            var t = c.GetIl2CppType().Name;
            if (t == "Transform" || t == "SkinnedMeshRenderer" || t == "MeshRenderer" || t == "MeshFilter") continue;
            if (t == "Animator") { var a = c.TryCast<Animator>(); if (a != null) a.enabled = false; continue; }
            if (t == "Rigidbody" || t.EndsWith("Collider") || t == "CharacterJoint") { Object.DestroyImmediate(c); continue; }
            var b = c.TryCast<Behaviour>();
            if (b != null) b.enabled = false;
            Object.DestroyImmediate(c);
        }
    }

    // Poses the copy from the figure's bones (figure root = the copy's parent, feet at its origin).
    public void Apply(Transform figureRoot, Transform[] fig, Vector3 figHipsBind)
    {
        var rootRot = figureRoot.rotation;
        var inv = Quaternion.Inverse(rootRot);
        if (bone[0] != null)
        {
            var off = fig[0].localPosition - figHipsBind;   // the figure's hip bob, in figure space
            bone[0].position = figureRoot.TransformPoint(new Vector3(hipsBind.x, hipsBind.y, hipsBind.z) + off);
        }
        for (int k = 0; k < Map.Length; k++)
        {
            var t = bone[k];
            if (t == null || fig[k] == null) continue;
            int c = Child[k];
            if (c >= 0 && bindDir[k] != Vector3.zero && fig[c] != null)
            {
                var want = (fig[c].position - fig[k].position).normalized;
                var from = rootRot * bindDir[k];
                // the shortest turn from the bind direction (the limb's twist stays the bind's)
                t.rotation = Quaternion.FromToRotation(from, want) * rootRot * bindRot[k];
            }
            else if (k == 9 || k == 10 || k == 15 || k == 16)
                t.localRotation = bindLocal[k];   // hands and feet straight on the forearm / shin
            else
                t.rotation = fig[k].rotation * inv * rootRot * bindRot[k];   // torso, neck, head: the figure's turn
        }
    }

    public void Destroy() { if (Root == null) return; if (built) Skins.SkinFiles.Destroy(Root); else Object.Destroy(Root); }

    // Tests: where the left arm points (upper arm to forearm), in the figure's space.
    public string ArmInfo(Transform figureRoot)
    {
        if (bone[5] == null || bone[7] == null) return "no arm";
        var d = figureRoot.InverseTransformDirection(bone[7].position - bone[5].position).normalized;
        return FormattableString.Invariant($"skin L arm {d.x:F2},{d.y:F2},{d.z:F2} bind {bindDir[5].x:F2},{bindDir[5].y:F2},{bindDir[5].z:F2} upperarm '{bone[5].name}' active {bone[5].gameObject.activeInHierarchy}");
    }

    static string RelPath(Transform root, Transform t)
    {
        var parts = new List<string>();
        for (var x = t; x != null && x != root; x = x.parent) parts.Insert(0, x.name);
        return string.Join("/", parts);
    }

    static int Depth(Transform t) { int d = 0; for (var x = t; x != null; x = x.parent) d++; return d; }
}
