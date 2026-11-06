using System;
using System.Collections.Generic;
using CscCoop.Skins;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace CscCoop;

// The skins window: "Скины" / "Skins" in the game's pause menu opens a table of every person in the game (SkinCatalog);
// a click chooses one as this player's look (Plugin.Skin, saved in the config; the others see it, SkinSync). Each
// cell shows the person rendered once into a small picture, with a loader until then. People load one at a time in
// the background (SkinLoader), the cells on screen first: after each person the loader asks for the first cell in
// view that has no picture yet. Closing the window stops the loading; pictures already made are kept.
public class SkinsWindow : MonoBehaviour
{
    public SkinsWindow(IntPtr p) : base(p) { }
    public static SkinsWindow Instance;
    void Awake() => Instance = this;

    public bool IsOpen { get; private set; }
    SkinLoader loader;
    readonly Dictionary<string, RenderTexture> previews = new();
    readonly HashSet<string> failed = new();
    readonly HashSet<string> pending = new();   // loaded, waiting for its picture
    Vector2 scroll;
    int firstVisible, lastVisible = -1;
    bool blockedPlayer;
    GameObject menuButton;
    public int Columns { get; private set; } = 6;

    const float CellW = 150, CellH = 210, Pad = 10;

    public static bool Russian
    {
        get
        {
            try { var l = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale; return l == null || l.Identifier.Code.StartsWith("ru"); }
            catch { return true; }
        }
    }
    static string T(string ru, string en) => Russian ? ru : en;

    // ---- the pause menu item ----

    void Update()
    {
        var pause = PauseMenuFind();
        if (pause != null && menuButton == null) AddMenuButton(pause);
        if (IsOpen)
        {
            loader?.Tick();
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }
        SkinPreview.Tick();
    }

    static PauseMenuController PauseMenuFind()
    {
        var p = Object.FindObjectOfType<PauseMenuController>();
        return p != null && p.gameObject.activeInHierarchy ? p : null;
    }

    // A copy of the pause menu's "Continue" button (its list: Continue, Restart mission, Respawn, Exit to the main menu;
    // the "General"/"Settings" ones above are tab headers), right after it, with our text.
    void AddMenuButton(PauseMenuController pause)
    {
        UICustomButton src = null;
        foreach (var b in pause.GetComponentsInChildren<UICustomButton>(false))
        {
            if (b == null || !b.gameObject.activeInHierarchy) continue;
            if (b.name == "Continue") src = b;
            if (!logged) Plugin.L.LogInfo($"COOP pause menu button '{b.name}' text '{TextOf(b.gameObject)}' at {b.transform.GetSiblingIndex()} under '{b.transform.parent?.name}'");
        }
        logged = true;
        if (src == null) return;
        var go = Instantiate(src.gameObject, src.transform.parent);
        go.name = "CoopSkinsButton";
        go.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
        foreach (var lt in go.GetComponentsInChildren<LocalizedText>(true)) Destroy(lt);   // it would put the original text back
        foreach (var t in go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) t.text = T("Скины", "Skins");
        var cb = go.GetComponent<UICustomButton>();
        cb.add_OnClick(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => { pause.Close(); Open(); })));
        menuButton = go;
        Plugin.L.LogInfo($"COOP skins button added to the pause menu after '{src.name}' ({TextOf(src.gameObject)})");
    }
    bool logged;

    static string TextOf(GameObject go) { var t = go.GetComponentInChildren<TMPro.TextMeshProUGUI>(true); return t != null ? t.text : ""; }

    // The bot presses it the way the mouse does.
    public bool PressMenuButton()
    {
        if (menuButton == null || EventSystem.current == null) return false;
        menuButton.GetComponent<UICustomButton>().OnPointerClick(new PointerEventData(EventSystem.current));
        return true;
    }

    // ---- open / close ----

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        SkinCatalog.Load();
        loader = new SkinLoader(3, NextToLoad, OnLoaded);   // previews: textures 1/8 size
        var player = CoopRunner.Instance?.World?.Player;
        if (player != null && player.CanMove) { player.Block(); blockedPlayer = true; }
        Plugin.L.LogInfo("COOP skins window opened");
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        loader?.Stop(); loader = null;
        var player = CoopRunner.Instance?.World?.Player;
        if (blockedPlayer && player != null) player.Unlock();
        blockedPlayer = false;
        Plugin.L.LogInfo($"COOP skins window closed ({previews.Count} pictures made)");
    }

    // The first cell in view without a picture (row by row), else nothing: cells out of view wait for a scroll.
    Person NextToLoad()
    {
        var ppl = SkinCatalog.People;
        if (ppl == null) return null;
        for (int i = Math.Max(0, firstVisible); i <= lastVisible && i < ppl.Count; i++)
        {
            var p = ppl[i];
            if (!previews.ContainsKey(p.Key) && !failed.Contains(p.Key) && !pending.Contains(p.Key)) return p;
        }
        return null;
    }

    void OnLoaded(Person p, GameObject go, Transform[] hum)
    {
        if (go == null) { failed.Add(p.Key); return; }
        pending.Add(p.Key);
        SkinPreview.Render(p, go, hum, rt => { pending.Remove(p.Key); if (rt != null) previews[p.Key] = rt; else failed.Add(p.Key); });
    }

    // ---- drawing ----

    GUIStyle box, label, title, cellLabel;
    Texture2D bg, sel;

    void OnGUI()
    {
        if (!IsOpen) return;
        if (bg == null)
        {
            bg = Solid(new Color(0.06f, 0.06f, 0.07f, 0.94f)); sel = Solid(new Color(0.85f, 0.62f, 0.12f, 1f));
            box = new GUIStyle(GUI.skin.box) { normal = { background = bg } };
            title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.85f, 0.62f, 0.12f) } };
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            cellLabel = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = Color.white } };
        }
        float w = Mathf.Min(Screen.width - 60, 1300), h = Screen.height - 80;
        var area = new Rect((Screen.width - w) / 2, 40, w, h);
        GUI.Box(area, GUIContent.none, box);
        GUILayout.BeginArea(new Rect(area.x + 20, area.y + 14, area.width - 40, area.height - 28));
        GUILayout.BeginHorizontal();
        GUILayout.Label(T("Скины", "Skins"), title);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(T("Закрыть", "Close"), GUILayout.Width(140), GUILayout.Height(36))) Close();
        GUILayout.EndHorizontal();
        var ppl = SkinCatalog.People;
        string chosen = Plugin.Skin.Value;
        GUILayout.Label(ppl == null ? T("Ищу людей в файлах игры…", "Finding people in the game's files…")
            : T($"Выберите, как вас видят другие игроки. Людей: {ppl.Count}.", $"Choose how the other players see you. People: {ppl.Count}."), label);
        if (ppl != null)
        {
            float viewW = area.width - 40, viewH = area.height - 110;
            Columns = Math.Max(1, (int)((viewW - 20) / (CellW + Pad)));
            int count = ppl.Count + 1;   // cell 0: no skin
            int rows = (count + Columns - 1) / Columns;
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Width(viewW), GUILayout.Height(viewH));
            GUILayoutUtility.GetRect(Columns * (CellW + Pad), rows * (CellH + Pad));
            int firstRow = (int)(scroll.y / (CellH + Pad)), lastRow = (int)((scroll.y + viewH) / (CellH + Pad));
            firstVisible = firstRow * Columns - 1; lastVisible = Math.Min(count - 1, (lastRow + 1) * Columns - 1) - 1;
            for (int row = firstRow; row <= lastRow && row < rows; row++)
                for (int col = 0; col < Columns; col++)
                {
                    int cell = row * Columns + col;
                    if (cell >= count) break;
                    var r = new Rect(col * (CellW + Pad), row * (CellH + Pad), CellW, CellH);
                    Person p = cell == 0 ? null : ppl[cell - 1];
                    string key = p?.Key ?? "";
                    bool isSel = key == chosen;
                    if (isSel) GUI.DrawTexture(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), sel);
                    GUI.Box(r, GUIContent.none, box);
                    var pic = new Rect(r.x + 5, r.y + 5, r.width - 10, r.height - 45);
                    if (p == null) GUI.Label(pic, T("\n\n\nБез скина\n(фигура мода)", "\n\n\nNo skin\n(the mod's figure)"), cellLabel);
                    else if (previews.TryGetValue(key, out var rt)) GUI.DrawTexture(pic, rt, ScaleMode.ScaleToFit);
                    else if (failed.Contains(key)) GUI.Label(pic, T("\n\n\nне загрузился", "\n\n\ncouldn't load"), cellLabel);
                    else Spinner(pic);
                    GUI.Label(new Rect(r.x + 4, r.y + r.height - 40, r.width - 8, 38), p == null ? "" : p.Display, cellLabel);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Choose(p);
                }
            GUILayout.EndScrollView();
        }
        GUILayout.EndArea();
    }

    // A loader: a dot running round a circle.
    void Spinner(Rect r)
    {
        var c = r.center;
        float a = Time.unscaledTime * 6;
        for (int i = 0; i < 8; i++)
        {
            float t = a - i * 0.5f;
            float alpha = 1f - i / 8f;
            var p = c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * 18;
            var old = GUI.color; GUI.color = new Color(0.85f, 0.62f, 0.12f, alpha);
            GUI.DrawTexture(new Rect(p.x - 3, p.y - 3, 6, 6), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }

    public void Choose(Person p)
    {
        Plugin.Skin.Value = p?.Key ?? "";
        Plugin.L.LogInfo($"COOP skin chosen: {(p == null ? "none" : $"'{p.Display}' ({p.Key})")}");
        CoopMenu.Toast(p == null ? T("Скин: фигура мода", "Skin: the mod's figure") : T($"Скин: {p.Display}", $"Skin: {p.Display}"));
        SkinSync.OnChosen();
    }

    public int PreviewCount => previews.Count;
    public void ScrollToRow(int row) => scroll.y = row * (CellH + Pad);   // tests
    public string VisibleRange => $"{firstVisible}..{lastVisible}";
    public int FailedCount => failed.Count;

    static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
}

// Renders a person once into a small picture: under its own camera and light far below the level (layer 31 only),
// posed by a PlayerFigure standing for a few frames, then the person is destroyed and only the picture is kept.
public static class SkinPreview
{
    const int Layer = 31;
    static Transform studio;
    static Camera cam;
    static readonly Queue<(Person p, GameObject go, Transform[] hum, Action<RenderTexture> done)> queue = new();
    static PlayerFigure fig;
    static Action<RenderTexture> current;
    static int frames;

    public static void Render(Person p, GameObject go, Transform[] hum, Action<RenderTexture> done) => queue.Enqueue((p, go, hum, done));

    public static void Tick()
    {
        if (fig != null)
        {
            fig.Pose(studio.position, 180, 0, PlayerFigure.Carry.None, CoopCore.PlayerPose.ToolNone, Vector3.zero, Quaternion.identity, 1, false, 0.02f);
            if (++frames < 10) return;
            var rt = new RenderTexture(256, 352, 24) { name = "CoopSkinPreview" };
            cam.targetTexture = rt;
            RenderTexture result = rt;
            try { cam.Render(); }
            catch (Exception e) { Plugin.L.LogWarning($"COOP skin preview: {e.Message}"); rt.Release(); result = null; }
            cam.targetTexture = null;
            if (fig.Skin != null) SkinFiles.Destroy(fig.Skin.Root);
            fig.Destroy(); fig = null;
            current?.Invoke(result); current = null;
            return;
        }
        if (queue.Count == 0) return;
        var (person, built, bones, cb) = queue.Dequeue();
        Setup();
        fig = new PlayerFigure(40, Color.white);
        fig.SetSkin(SkinModel.FromBuilt(built, bones, person.Display, fig.Root.transform));
        foreach (var t in fig.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        current = cb; frames = 0;
    }

    static void Setup()
    {
        if (studio != null) return;
        studio = new GameObject("CoopSkinStudio").transform;
        Object.DontDestroyOnLoad(studio.gameObject);
        studio.position = new Vector3(0, -3000, 0);
        cam = new GameObject("CoopSkinCamera").AddComponent<Camera>();
        cam.transform.SetParent(studio, false);
        cam.transform.localPosition = new Vector3(0, 0.95f, -3.6f);
        cam.transform.localRotation = Quaternion.Euler(0, 0, 0);
        cam.fieldOfView = 34; cam.nearClipPlane = 0.1f; cam.farClipPlane = 10;
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1);
        cam.enabled = false;
        foreach (var (pos, intensity) in new[] { (new Vector3(-1.2f, 2.2f, -2f), 3.5f), (new Vector3(1.5f, 1.4f, -1.5f), 2f), (new Vector3(0, 2f, 1.5f), 1.5f) })
        {
            var l = new GameObject("CoopSkinLight").AddComponent<Light>();
            l.transform.SetParent(studio, false);
            l.transform.localPosition = pos;
            l.type = LightType.Point; l.range = 6; l.intensity = intensity; l.cullingMask = 1 << Layer;
        }
    }
}
