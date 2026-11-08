using System.IO;
using System.Linq;
using CscCoop.Skins;
using UnityEngine;

namespace CscCoop;

// skinswindow (tools/run-probe.ps1 -Test skinswindow): the Skins window as a player uses it, and the owner's six
// findings on 0.4.4-test. Opens the pause menu and presses Skins; the big preview of a chosen person, turned and close
// to the face (shot_skins_face.png: eyes), a cutscene person with an IK rig and one on the Unreal-style rig
// (shot_skins_cts.png, shot_skins_ue.png); a job's asset unloading (Resources.UnloadUnusedAssets) must not leave empty
// cells; every row scrolled through: which people fail; Close must bring the pause menu back.
static class SkinsWindowProbe
{
    static int step, row;
    static float at;

    static void Shot(string name) { string dir = Env.Get("CSCCOOP_SHOT_DIR"); if (dir != "") ScreenCapture.CaptureScreenshot(Path.Combine(dir, name)); }
    static Person By(string display) => SkinCatalog.People.FirstOrDefault(p => p.Display == display);

    public static void Tick(GameWorld w)
    {
        if (step == 0 && (w.Player == null || !w.Player.CanMove)) { at = Time.unscaledTime + 4; return; }
        float now = Time.unscaledTime;
        var win = SkinsWindow.Instance;
        if (now < at) return;
        switch (step)
        {
            case 0:
                SkinCatalog.Load();
                Object.FindObjectOfType<PauseMenuController>(true)?.Open();
                step = 1; at = now + 2; break;
            case 1:
                Plugin.L.LogInfo($"SKINSWIN pressed the Skins item: {win.PressMenuButton()}, window open {win.IsOpen}");
                step = 2; at = now + 8; break;
            case 2:
                Plugin.L.LogInfo($"SKINSWIN after 8 s: {win.PreviewCount} pictures, {win.FailedCount} failed");
                win.Choose(By("DrugDealer"));
                step = 3; at = now + 4; break;
            case 3:
                SkinShowcase.Zoom = 1; SkinShowcase.Yaw = 180;
                step = 4; at = now + 1.5f; break;
            case 4:
                Shot("shot_skins_face.png");
                step = 41; at = now + 1; break;
            case 41:
                SkinShowcase.Zoom = 0; SkinShowcase.Yaw = 150;
                win.Choose(By("Pizza Italian2 Cts"));
                step = 42; at = now + 6; break;
            case 42:
                Shot("shot_skins_cts.png");
                step = 5; at = now + 1; break;
            case 5:
                win.Choose(By("Husband new"));
                step = 51; at = now + 6; break;
            case 51:
                SkinShowcase.Zoom = 1; SkinShowcase.Yaw = 180;
                step = 52; at = now + 1.5f; break;
            case 52:
                Shot("shot_skins_ue.png");
                step = 6; at = now + 1; break;
            case 6:
                Plugin.L.LogInfo($"SKINSWIN before unloading: {win.LivePreviews} of {win.PreviewCount} pictures alive");
                Resources.UnloadUnusedAssets();
                step = 7; at = now + 3; break;
            case 7:
                Plugin.L.LogInfo($"SKINSWIN after unloading: {win.LivePreviews} of {win.PreviewCount} pictures alive");
                step = 8; at = now + 4; break;
            case 8:
                Plugin.L.LogInfo($"SKINSWIN redrawn: {win.LivePreviews} of {win.PreviewCount} alive, {win.Lost} found gone and made again");
                row = 0; step = 9; break;
            case 9:   // every row
                if (win.Busy && now < at + 6) return;
                win.ScrollToRow(row);
                row += 2;
                at = now + 1.2f;
                if (row * win.Columns > SkinCatalog.People.Count + win.Columns * 2) { step = 10; at = now + 4; }
                break;
            case 10:
                Plugin.L.LogInfo($"SKINSWIN all rows: {win.LivePreviews} pictures, {win.FailedCount} failed: {win.FailedNames}");
                win.Choose(null);
                win.Close();
                step = 11; at = now + 2; break;
            case 11:
                var pause = Object.FindObjectOfType<PauseMenuController>(true);
                Plugin.L.LogInfo($"SKINSWIN after Close: window open {win.IsOpen}, pause menu shown {(pause != null && pause.gameObject.activeInHierarchy)}");
                Shot("shot_skins_closed.png");
                Plugin.L.LogInfo("SKINSWIN done");
                step = 12; at = now + 2; break;
            case 12:
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                break;
        }
    }
}
