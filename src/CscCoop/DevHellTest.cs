using System;
using System.Linq;
using System.Text;
using CoopCore;
using UnityEngine;

namespace CscCoop;

// Suites for the owner's test of 0.4.3-test on Developers' Hell (tools/run-pair.ps1 -Test <suite> [-Mission 11]):
//  - stackreward (-Mission 11): the host leaves the Super Stacker with a winning score, the way the minigame does
//    (StackMiniGamePresenter.HighscoreOnLeave, which EventOnScoreReach listens to). What its reward event moves (the
//    passage behind the shelf) must end the same on the client.
//  - mails (-Mission 11): the host deletes two mails on the office computers (InspectableMail.DeleteMail, what the mail
//    UI's delete ends in). The client's mails and the "delete the mails" count must match.
//  - vanrace (first job): both machines run the van's own sequence for the same body (TrunkController.SacaleAndDestroyItem,
//    what the trunk's trigger starts once the item is in it; the other player's copy lands in the trunk too), the
//    client half a second later. The van prop and effect must be shown once per machine, the body counted once.
public partial class Bot
{
    // ---- stackreward ----

    EventOnScoreReach srReach;

    static string Snapshot(EventOnScoreReach r)
    {
        var sb = new StringBuilder();
        var ev = r?.OnScoreReach;
        if (ev == null) return "-";
        for (int k = 0; k < ev.GetPersistentEventCount(); k++)
        {
            var o = ev.GetPersistentTarget(k);
            var go = o == null ? null : o.TryCast<GameObject>() ?? o.TryCast<Component>()?.gameObject;
            if (go == null) { sb.Append($" [{k}:none]"); continue; }
            foreach (var t in go.GetComponentsInChildren<Transform>(true).Take(12))
                sb.Append(FormattableString.Invariant($" [{t.name}:{(t.gameObject.activeInHierarchy ? 1 : 0)}:{t.position.x:F2},{t.position.y:F2},{t.position.z:F2}:{t.eulerAngles.y:F0}]"));
        }
        return sb.ToString();
    }

    void StackRewardTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        switch (step)
        {
            case 0 when t > 3:
                srReach = UnityEngine.Object.FindObjectOfType<EventOnScoreReach>(true);
                if (srReach != null)
                {
                    var ev = srReach.OnScoreReach;
                    for (int k = 0; ev != null && k < ev.GetPersistentEventCount(); k++)
                        Plugin.L.LogInfo($"STACKREWARD event {k}: {(ev.GetPersistentTarget(k) != null ? ev.GetPersistentTarget(k).name + " (" + ev.GetPersistentTarget(k).GetIl2CppType().Name + ")" : "null")}.{ev.GetPersistentMethodName(k)}");
                }
                Plugin.L.LogInfo($"STACKREWARD reach {(srReach != null)} required {(srReach != null ? srReach.scoreRequired : 0)} game {(srReach?.stackGame != null)}");
                Plugin.L.LogInfo($"STACKREWARD before:{Snapshot(srReach)}");
                step = 1; break;
            case 1 when t > 5:
                if (host && srReach?.stackGame != null)
                {
                    int score = srReach.scoreRequired + 100;
                    var leave = srReach.stackGame.HighscoreOnLeave;
                    Plugin.L.LogInfo($"STACKREWARD host leaves the game with {score} (listeners {(leave != null)})");
                    leave?.Invoke(score);
                }
                step = 2; break;
            case 2 when t > 20:
                Plugin.L.LogInfo($"STACKREWARD after:{Snapshot(srReach)}");
                Plugin.L.LogInfo("STACKREWARD dump done");
                step = 3; break;
        }
    }

    // ---- mails ----

    static string MailState()
    {
        var sb = new StringBuilder();
        foreach (var m in UnityEngine.Object.FindObjectsOfType<InspectableMail>(true).OrderBy(m => GameWorld.IndexedPathOf(m.transform)))
            sb.Append($" [{m.name}:{(m.IsEvidence ? 1 : 0)}:{(m.gameObject.activeInHierarchy ? 1 : 0)}]");
        foreach (var q in UnityEngine.Object.FindObjectsOfType<DeleteMailQuest>(true))
            sb.Append($" quest {q.quest} {q.mailCountCurrent}/{q.mailCountMax} deleted {q.wasDeleted};");
        return sb.ToString();
    }

    void MailsTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        switch (step)
        {
            case 0 when t > 3:
                Plugin.L.LogInfo($"MAILS before:{MailState()}");
                step = 1; break;
            case 1 when t > 5:
                if (host)
                {
                    // The mails a quest counts (DeleteMailQuest.im), as the player deletes them.
                    var targets = UnityEngine.Object.FindObjectsOfType<DeleteMailQuest>(true).Select(q => q.im).Where(m => m != null && m.IsEvidence).Take(2).ToArray();
                    foreach (var m in targets) { m.DeleteMail(); Plugin.L.LogInfo($"MAILS host deleted '{m.name}' ({Actions.IndexOfAny(m)})"); }
                    if (targets.Length == 0) Plugin.L.LogInfo("MAILS host found no mail to delete");
                }
                step = 2; break;
            case 2 when t > 12:
                Plugin.L.LogInfo($"MAILS after:{MailState()}");
                Plugin.L.LogInfo("MAILS dump done");
                step = 3; break;
        }
    }

    // ---- vanrace ----

    int vrItem = -1;
    float vrClientAt = -1;

    void VanStart(GameWorld w, string who)
    {
        var trunk = UnityEngine.Object.FindObjectOfType<TrunkController>();
        var g = vrItem >= 0 ? w.Garbage[vrItem] : null;
        if (trunk == null || g == null) { Plugin.L.LogInfo($"VANRACE {who}: no trunk or item"); return; }
        // TryAddItemToTruck starts this only for an item inside the trunk's trigger (its list); the bots start the
        // sequence it starts: the item swells and shrinks, then TryShowProp, the effect and CollectAndDestroy.
        trunk.StartCoroutine(trunk.SacaleAndDestroyItem(g));
        Plugin.L.LogInfo(FormattableString.Invariant($"VANRACE {who} started the van sequence for {g.name} at {Time.unscaledTime:F2}"));
    }

    void VanRaceTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        switch (step)
        {
            case 0 when t > 3:
                for (int i = 0; i < w.Garbage.Count && vrItem < 0; i++)
                    if (w.Garbage[i] != null && w.Garbage[i].bodyController != null && !w.Garbage[i].isCollected) vrItem = i;
                Plugin.L.LogInfo($"VANRACE item {vrItem} '{(vrItem >= 0 ? w.Garbage[vrItem].name : "-")}'");
                step = 1; break;
            case 1 when t > 5:
                if (host) VanStart(w, "host"); else vrClientAt = t + 0.5f;
                step = 2; break;
            case 2 when t > 15:
                var g = vrItem >= 0 ? w.Garbage[vrItem] : null;
                var bq = g == null ? null : g.GetComponentInChildren<BodyQuest>(true) ?? g.bodyController?.GetComponentInChildren<BodyQuest>(true);
                Plugin.L.LogInfo($"VANRACE after: collected {(g != null && g.isCollected)} props shown {Bodies.PropsShown} skipped {Bodies.PropsSkipped} collected by the other {Bodies.Collected} body counted {(bq != null && bq.wasCollected)}");
                Plugin.L.LogInfo("VANRACE dump done");
                step = 3; break;
        }
        if (!host && vrClientAt > 0 && t >= vrClientAt) { vrClientAt = -1; VanStart(w, "client"); }
    }
}
