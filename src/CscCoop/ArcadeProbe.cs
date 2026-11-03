using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CscCoop;

// arcadeprobe (tools/run-probe.ps1 -Test arcadeprobe -Mission 11): the Super Stacker machine (StackMiniGamePresenter,
// a UIPanel) and what opens it, what the scene table does while it is open and after, and the player's flashlight
// (FlashlightController and its Light).
public partial class Bot
{
    int apStep;
    float apAt;
    StackMiniGamePresenter apGame;

    void ArcadeProbe(string scene, float inScene, GameWorld w)
    {
        string only = Env.Get("CSCCOOP_TEST_SCENE");
        if (only != "" && scene != only) return;
        float now = Time.unscaledTime;
        if (apStep == 0)
        {
            if (w.Player == null || !w.Player.CanMove || inScene < 8) return;
            apStep = 1;
            foreach (var g in Object.FindObjectsOfType<StackMiniGamePresenter>(true))
            {
                apGame = g;
                Plugin.L.LogInfo($"ARCADEPROBE presenter '{GameWorld.IndexedPathOf(g.transform)}' at {P3(g.transform.position)} active {g.isActiveAndEnabled} player field '{(g.player != null ? GameWorld.IndexedPathOf(g.player.transform) : "null")}' is our player {(g.player != null && g.player.Pointer == w.Player.gameObject.Pointer)} manager '{(g.manager != null ? GameWorld.IndexedPathOf(g.manager.transform) : "null")}' camera '{(g.gameCamera != null ? GameWorld.IndexedPathOf(g.gameCamera.transform) : "null")}'");
                if (g.manager != null)
                    Plugin.L.LogInfo($"ARCADEPROBE   manager rigidbodies {g.manager.GetComponentsInChildren<Rigidbody>(true).Length}");
            }
            foreach (var it in Object.FindObjectsOfType<Interactable>(true))
            {
                var ev = it.OnInteract;
                if (ev == null) continue;
                for (int k = 0; k < ev.GetPersistentEventCount(); k++)
                {
                    var target = ev.GetPersistentTarget(k);
                    if (target == null) continue;
                    string tn = target.GetIl2CppType().Name;
                    if (!tn.Contains("Stack") && !ev.GetPersistentMethodName(k).Contains("Game")) continue;
                    Plugin.L.LogInfo($"ARCADEPROBE button '{GameWorld.IndexedPathOf(it.transform)}' ({it.GetIl2CppType().Name}) at {P3(it.transform.position)} -> {tn}.{ev.GetPersistentMethodName(k)} actions index {Actions.IndexOfAny(it)}");
                }
            }
            foreach (var f in Object.FindObjectsOfType<FlashlightController>(true))
            {
                var l = f.flashlight;
                Plugin.L.LogInfo($"ARCADEPROBE flashlight '{GameWorld.IndexedPathOf(f.transform)}' on {f.IsOn} light '{(l != null ? GameWorld.IndexedPathOf(l.transform) : "null")}'"
                    + (l != null ? FormattableString.Invariant($" type {l.type} range {l.range} angle {l.spotAngle}/{l.innerSpotAngle} intensity {l.intensity} color {l.color} shadows {l.shadows} cookie {(l.cookie != null ? l.cookie.name : "-")} enabled {l.enabled} active {l.gameObject.activeInHierarchy} local {P3(l.transform.localPosition)} parent '{(l.transform.parent != null ? l.transform.parent.name : "-")}'") : ""));
                if (l != null) foreach (var mb in l.GetComponents<MonoBehaviour>()) Plugin.L.LogInfo($"ARCADEPROBE   light component {mb.GetIl2CppType().Name}");
            }
            Plugin.L.LogInfo($"ARCADEPROBE before: key {w.SceneKey:X8} items {w.ItemCount}");
            if (apGame == null) { Done(); return; }
            apGame.EnterTheGame();
            apAt = now + 3;
            return;
        }
        if (apStep == 1 && now >= apAt)
        {
            Plugin.L.LogInfo($"ARCADEPROBE open: key {w.SceneKey:X8} items {w.ItemCount} player '{(w.Player != null ? w.Player.name : "null")}' active {(apGame.player != null && apGame.player.activeInHierarchy)} panel open {apGame.isActiveAndEnabled}");
            if (apGame.manager != null) { apGame.manager.StartGame(); }
            apStep = 2; apAt = now + 4;
            return;
        }
        if (apStep == 2 && now >= apAt)
        {
            if (apGame.manager != null) { apGame.manager.PlaceCube(); apGame.manager.PlaceCube(); }
            Plugin.L.LogInfo($"ARCADEPROBE playing: key {w.SceneKey:X8} items {w.ItemCount} manager rigidbodies {(apGame.manager != null ? apGame.manager.GetComponentsInChildren<Rigidbody>(true).Length : -1)} scene rigidbodies {Object.FindObjectsOfType<Rigidbody>().Length}");
            apStep = 3; apAt = now + 3;
            return;
        }
        if (apStep == 3 && now >= apAt)
        {
            apGame.Close();
            apStep = 4; apAt = now + 6;
            return;
        }
        if (apStep == 4 && now >= apAt)
        {
            Plugin.L.LogInfo($"ARCADEPROBE closed: key {w.SceneKey:X8} items {w.ItemCount} player active {(w.Player != null && w.Player.gameObject.activeInHierarchy)} can move {(w.Player != null && w.Player.CanMove)}");
            Done();
        }

        static void Done()
        {
            Plugin.L.LogInfo("ARCADEPROBE done");
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
