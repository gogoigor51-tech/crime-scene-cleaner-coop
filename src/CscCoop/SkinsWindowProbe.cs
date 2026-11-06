using System.IO;
using CscCoop.Skins;
using UnityEngine;

namespace CscCoop;

// skinswindow (tools/run-probe.ps1 -Test skinswindow): the pause menu's Skins item and window as a player uses them.
// Opens the pause menu (screenshot shot_skins_menu.png), presses Skins, lets the cells in view load
// (shot_skins_window.png), scrolls far down (loading must follow the view, shot_skins_scrolled.png), chooses a person,
// closes the window: loading must stop and the choice be saved.
static class SkinsWindowProbe
{
    static int step;
    static float at;

    static void Shot(string name) { string dir = Env.Get("CSCCOOP_SHOT_DIR"); if (dir != "") ScreenCapture.CaptureScreenshot(Path.Combine(dir, name)); }

    public static void Tick(GameWorld w)
    {
        if (w.Player == null || !w.Player.CanMove) { if (step == 0) at = Time.unscaledTime + 4; }
        float now = Time.unscaledTime;
        var win = SkinsWindow.Instance;
        switch (step)
        {
            case 0 when now >= at && w.Player != null && w.Player.CanMove:
                SkinCatalog.Load();
                var pause = Object.FindObjectOfType<PauseMenuController>(true);
                Plugin.L.LogInfo($"SKINSWIN pause menu {(pause != null)}");
                pause?.Open();
                step = 1; at = now + 2; break;
            case 1 when now >= at:
                Shot("shot_skins_menu.png");
                step = 2; at = now + 1; break;
            case 2 when now >= at:
                Plugin.L.LogInfo($"SKINSWIN pressed the Skins item: {win.PressMenuButton()}, window open {win.IsOpen}");
                step = 3; at = now + 8; break;
            case 3 when now >= at:
                Plugin.L.LogInfo($"SKINSWIN after 8 s: {win.PreviewCount} pictures, {win.FailedCount} failed, in view {win.VisibleRange}, columns {win.Columns}");
                Shot("shot_skins_window.png");
                step = 31; at = now + 1; break;
            case 31 when now >= at:
                win.ScrollToRow(15);
                step = 4; at = now + 8; break;
            case 4 when now >= at:
                Plugin.L.LogInfo($"SKINSWIN scrolled: {win.PreviewCount} pictures, {win.FailedCount} failed, in view {win.VisibleRange}");
                Shot("shot_skins_scrolled.png");
                step = 41; at = now + 1; break;
            case 41 when now >= at:
                var ppl = SkinCatalog.People;
                win.Choose(ppl[System.Math.Min(ppl.Count - 1, 15 * win.Columns + 1)]);
                win.Close();
                step = 5; at = now + 4; break;
            case 5 when now >= at:
                Plugin.L.LogInfo($"SKINSWIN after closing: {win.PreviewCount} pictures (loading stopped), chosen '{Plugin.Skin.Value}'");
                Plugin.L.LogInfo("SKINSWIN done");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                break;
        }
    }
}
