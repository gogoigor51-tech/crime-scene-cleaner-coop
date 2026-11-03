using System;
using System.Linq;
using System.Text;
using CoopCore;
using UnityEngine;

namespace CscCoop;

// bagmerge suite (tools/run-pair.ps1 -Test bagmerge -Mission 11): the host makes a trash bag, the client makes one and
// drops it onto the host's while the host pushes its own toward it (GarbageBagConnector.OnCollisionEnter merges them: the faster grows, the other shrinks
// away; needs the bag perk, so both bots unlock all perks in the test copies). Owner's test: merging bags of two
// players made duplicates for a while. Every 0.5 s both log how many bags they have; at the end there must be one
// bag on both, of the merged size, at the same spot, and never more bags than were made.
public partial class Bot
{
    GameObject bmMine; Vector3 bmSpot; int bmMax; float bmNext;

    static GameObject MakeBag(Vector3 p)
    {
        GameObject prefab = null;
        foreach (var gc in Resources.FindObjectsOfTypeAll<GarbageCollector>()) if (gc.garbagePrefab != null) { prefab = gc.garbagePrefab; break; }
        return prefab != null ? UnityEngine.Object.Instantiate(prefab, p, Quaternion.identity) : null;
    }

    static string Bags()
    {
        var sb = new StringBuilder();
        foreach (var b in UnityEngine.Object.FindObjectsOfType<GarbageBagConnector>().Where(b => b.gameObject.activeInHierarchy).OrderBy(b => b.transform.position.x))
            sb.Append(FormattableString.Invariant($"{P3(b.transform.position)}~{b.CurrentSize}~{(Dyn.IsPuppet(b.gameObject) ? "other" : "mine")};"));
        return sb.ToString();
    }

    void BagMergeTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        if (step >= 2 && step < 4 && t >= bmNext)
        {
            bmNext = t + 0.5f;
            int n = UnityEngine.Object.FindObjectsOfType<GarbageBagConnector>().Count(b => b.gameObject.activeInHierarchy);
            if (n > bmMax) { bmMax = n; Plugin.L.LogInfo($"BAGMERGE t {t:F1} bags {n}: {Bags()}"); }
        }
        switch (step)
        {
            case 0 when t > 3:
                try { PerksController.Instance.UnlockAllPerks(); } catch (Exception e) { Plugin.L.LogInfo("BAGMERGE perks: " + e.Message); }
                // the same spot on both
                bmSpot = new Vector3(-33.5f, 1.5f, 20.8f);   // office floor away from the van (its trunk takes bags)
                if (Physics.Raycast(bmSpot + Vector3.up * 2, Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore)) bmSpot = hit.point + Vector3.up * 0.3f;
                Plugin.L.LogInfo($"BAGMERGE spot {P3(bmSpot)} perk 53 unlocked {PerksController.Instance.IsPerkUnlocked((PerkID)53)}");
                step = 1; break;
            case 1 when t > 5:
                if (host) bmMine = MakeBag(bmSpot);
                step = 10; break;
            case 10 when t > 9:
                // both bags moving when they meet: the host's slides toward the spot, the client's falls onto it
                if (!host)
                {
                    bmMine = MakeBag(bmSpot + Vector3.up * 1.2f);
                    var rb = bmMine != null ? bmMine.GetComponent<Rigidbody>() : null;
                    if (rb != null) rb.velocity = Vector3.down * 4f;
                }
                if (host && bmMine != null)
                {
                    bmMine.transform.position = bmSpot + new Vector3(-0.6f, 0.1f, 0f);
                    var rb = bmMine.GetComponent<Rigidbody>();
                    if (rb != null) rb.velocity = new Vector3(3f, 1f, 0f);
                }
                Plugin.L.LogInfo($"BAGMERGE {(host ? "host" : "client")} bags before the drop: {Bags()}");
                step = 2; break;
            case 2 when t > 22:
                Plugin.L.LogInfo($"BAGMERGE final max {bmMax} bags {Bags()}");
                Plugin.L.LogInfo("BAGMERGE dump done");
                step = 4; break;
        }
    }
}
