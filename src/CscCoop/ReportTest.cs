using System;
using System.Reflection;
using CoopCore;
using HarmonyLib;
using UnityEngine;

namespace CscCoop;

// report suite (tools/run-pair.ps1 -Test report): the host leaves the job (MissionLeaveController.LeaveMissionWithReport,
// as the truck's "leave" does) and so does the client, at the same moment. Every call of the leave and report methods
// is counted and logged on both machines with its time (owner's test: the report's score animations played twice, about
// a second apart). Each machine must show its report once.
public partial class Bot
{
    static bool reportPatched;
    public static readonly System.Collections.Generic.Dictionary<string, int> ReportCalls = new();

    internal static void PatchReportCounters(Harmony h)
    {
        if (reportPatched) return;
        reportPatched = true;
        var asm = typeof(MissionLeaveController).Assembly;
        var post = new HarmonyMethod(typeof(Bot).GetMethod(nameof(CountCall), BindingFlags.NonPublic | BindingFlags.Static));
        foreach (var (type, method) in new[]
        {
            ("MissionLeaveController", "LeaveMissionWithReport"), ("MissionLeaveController", "DisplayReportAnim"), ("MissionLeaveController", "DisplayReportFade"),
            ("MissionReportDataController", "Display"), ("MissionReportDataController", "DisplayReport"),
            ("MissionReportFinalView", "Setup"), ("MissionReportFinalView", "Open"),
            ("JobDetailsPage", "AnimateShow"), ("MoneySummaryPage", "AnimateShow"), ("SecretsPage", "AnimateShow"),
            ("JobDetailsPage", "ShowPage"), ("MoneySummaryPage", "ShowPage"), ("SecretsPage", "ShowPage"),
            ("MissionFinalReportUI", "Display"), ("MissionFinalReportUI", "StartAnimation"),
            ("JobDetailsPage", "AnimateJobDetailsCoroutine"), ("JobDetailsPage", "SkipAnimation"),
            ("JobDetailsPage", "SetDataInstantly"), ("JobDetailsPage", "AnimateSuccessStatusLabel"),
            ("MissionReportFinalView", "PlayShowSound"), ("MissionReportFinalView", "PlayFinalSound"),
            ("TextIncrementEffect", "UpdateValue"), ("TextIncrementEffect", "Restart"), ("TextIncrementEffect", "SetWithoutAnimation"),
        })
        {
            var t = asm.GetType(type);
            var m = t == null ? null : AccessTools.DeclaredMethod(t, method);
            if (m == null) { Plugin.L.LogWarning($"REPORT no {type}.{method}"); continue; }
            h.Patch(m, postfix: post);
        }
    }

    static bool counting;
    static readonly System.Collections.Generic.Dictionary<Type, MethodInfo> casts = new();
    static void CountCall(object __instance, MethodBase __originalMethod, bool __runOriginal)
    {
        if (!__runOriginal) return;   // skipped by a prefix (a second leave): it didn't run
        // Folded bodies make some of these run for unrelated calls all the time: nothing until the job is left.
        if (!counting && __originalMethod.Name != "LeaveMissionWithReport") return;
        counting = true;
        var dt = __originalMethod.DeclaringType;
        string key = $"{dt.Name}.{__originalMethod.Name}";
        if (__instance is Component c)
        {
            // IL2CPP folds identical bodies: only count calls on the declared type.
            if (!casts.TryGetValue(dt, out var cast)) casts[dt] = cast = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("TryCast").MakeGenericMethod(dt);
            if (cast.Invoke(c, null) == null) return;
        }
        ReportCalls[key] = ReportCalls.TryGetValue(key, out var n) ? n + 1 : 1;
        Plugin.L.LogInfo(FormattableString.Invariant($"REPORT call {key} #{ReportCalls[key]} at {Time.unscaledTime:F2} applying {WorldEvents.Applying}"));
    }

    void ReportTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        switch (step)
        {
            case 0 when t > 4:
                // Both players leave at the same moment (two players at the truck, the lockstep keeps the bots within
                // frames): each machine gets its own leave and the other's Leave event.
                LeaveNow(host ? "host" : "client");
                step = 1; break;
        }
    }

    static void LeaveNow(string who)
    {
        var c = UnityEngine.Object.FindObjectOfType<MissionLeaveController>();
        Plugin.L.LogInfo($"REPORT {who} leaves (controller {(c != null)})");
        c?.LeaveMissionWithReport();
    }

    // Runs outside the job too (the report is shown over the job scene, then the hub loads).
    float reportEndAt = -1, reportOpenAt = -1;
    int reportShots;
    void ReportWatch()
    {
        if (reportEndAt < 0 && ReportCalls.ContainsKey("MissionLeaveController.LeaveMissionWithReport")) reportEndAt = Time.unscaledTime + 45;
        // Screenshots of the report 2, 6 and 15 s after it opened.
        if (ReportCalls.ContainsKey("MissionReportFinalView.Open"))
        {
            if (reportOpenAt < 0) reportOpenAt = Time.unscaledTime;
            string dir = Env.Get("CSCCOOP_SHOT_DIR");
            foreach (var d in new[] { 2f, 6f, 15f })
                if (dir != "" && Time.unscaledTime - reportOpenAt > d && reportShots < Array.IndexOf(new[] { 2f, 6f, 15f }, d) + 1)
                {
                    reportShots++;
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot_report_{(CoopRunner.Instance?.Session?.IsHost == true ? "host" : "client")}_{d:F0}s.png"));
                }
        }
        if (reportEndAt > 0 && Time.unscaledTime > reportEndAt)
        {
            reportEndAt = float.MaxValue;
            foreach (var kv in ReportCalls) Plugin.L.LogInfo($"REPORT total {kv.Key} {kv.Value}");
            Plugin.L.LogInfo("REPORT dump done");
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
