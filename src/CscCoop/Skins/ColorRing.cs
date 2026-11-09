using UnityEngine;

namespace CscCoop.Skins;

// A colour picker drawn with IMGUI (the skins window): a ring of hues round a square of saturation (left to right)
// and brightness (bottom to top) for the ring's hue. Click or drag on either. Textures made here, the square's
// again only when the hue changes.
public sealed class ColorRing
{
    const int RingPx = 192, SquarePx = 48;
    const float Inner = 0.76f;   // the ring's inner radius, of its outer
    Texture2D ring, square, dot;
    float squareHue = -1;
    public float H, S, V;
    int dragging;   // 0 none, 1 ring, 2 square
    public bool Dragging => dragging != 0;

    public Color32 Color
    {
        get => UnityEngine.Color.HSVToRGB(H, S, V);
        set
        {
            UnityEngine.Color.RGBToHSV(value, out var h, out var s, out var v);
            if (s > 0.01f && v > 0.01f) H = h;   // grey or black keeps the hue it had
            if (v > 0.01f) S = s;
            V = v;
        }
    }

    // Returns 1 while the colour changes, 2 when the mouse lets go after a change, else 0.
    public int Draw(Rect r)
    {
        Make();
        float size = Mathf.Min(r.width, r.height);
        var box = new Rect(r.x + (r.width - size) / 2, r.y + (r.height - size) / 2, size, size);
        GUI.DrawTexture(box, ring);
        var sq = SquareRect(box);
        if (!Mathf.Approximately(squareHue, H)) MakeSquare();
        GUI.DrawTexture(sq, square);
        // the markers
        float a = H * 2 * Mathf.PI, mid = size / 2 * (1 + Inner) / 2;
        var c = box.center;
        Marker(new Vector2(c.x + Mathf.Cos(a) * mid, c.y - Mathf.Sin(a) * mid));
        Marker(new Vector2(sq.x + S * sq.width, sq.y + (1 - V) * sq.height));

        var e = Event.current;
        int result = 0;
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            var d = e.mousePosition - c; float rr = d.magnitude / (size / 2);
            if (sq.Contains(e.mousePosition)) dragging = 2;
            else if (rr >= Inner * 0.9f && rr <= 1.05f) dragging = 1;
            if (dragging != 0) { result = Pick(e.mousePosition, c, sq); e.Use(); }
        }
        else if (e.type == EventType.MouseDrag && dragging != 0) { result = Pick(e.mousePosition, c, sq); e.Use(); }
        else if ((e.type == EventType.MouseUp || e.rawType == EventType.MouseUp) && dragging != 0) { dragging = 0; result = 2; e.Use(); }
        return result;
    }

    int Pick(Vector2 m, Vector2 c, Rect sq)
    {
        if (dragging == 1)
        {
            float a = Mathf.Atan2(-(m.y - c.y), m.x - c.x) / (2 * Mathf.PI);
            H = a < 0 ? a + 1 : a;
        }
        else
        {
            S = Mathf.Clamp01((m.x - sq.x) / sq.width);
            V = Mathf.Clamp01(1 - (m.y - sq.y) / sq.height);
        }
        return 1;
    }

    static Rect SquareRect(Rect box)
    {
        float side = box.width * Inner * 0.66f;   // inside the ring, a little gap
        return new Rect(box.center.x - side / 2, box.center.y - side / 2, side, side);
    }

    void Marker(Vector2 p)
    {
        GUI.DrawTexture(new Rect(p.x - 7, p.y - 7, 14, 14), dot);
    }

    void Make()
    {
        if (ring != null) return;
        ring = new Texture2D(RingPx, RingPx, TextureFormat.RGBA32, false) { name = "CoopColorRing", hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[RingPx * RingPx];
        for (int y = 0; y < RingPx; y++)
            for (int x = 0; x < RingPx; x++)
            {
                // texture rows go up; the ring is drawn as seen (hue 0 to the right, counterclockwise)
                float dx = (x + 0.5f) / RingPx * 2 - 1, dy = (y + 0.5f) / RingPx * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01((1 - d) * RingPx / 2) * Mathf.Clamp01((d - Inner) * RingPx / 2);
                float h = Mathf.Atan2(dy, dx) / (2 * Mathf.PI); if (h < 0) h += 1;
                var col = UnityEngine.Color.HSVToRGB(h, 1, 1); col.a = alpha;
                px[y * RingPx + x] = col;
            }
        ring.SetPixels32(px); ring.Apply();
        square = new Texture2D(SquarePx, SquarePx, TextureFormat.RGBA32, false) { name = "CoopColorSquare", hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Clamp };
        const int D = 32;
        dot = new Texture2D(D, D, TextureFormat.RGBA32, false) { name = "CoopColorDot", hideFlags = HideFlags.DontUnloadUnusedAsset };
        var dp = new Color32[D * D];
        for (int y = 0; y < D; y++)
            for (int x = 0; x < D; x++)
            {
                float dx = (x + 0.5f) / D * 2 - 1, dy = (y + 0.5f) / D * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
                // a white circle with a dark edge, see-through in the middle
                float a = Mathf.Clamp01((1 - d) * D / 2) * Mathf.Clamp01((d - 0.55f) * D / 2);
                var c = d > 0.8f ? new Color(0.1f, 0.1f, 0.1f, a) : new Color(1, 1, 1, a);
                dp[y * D + x] = c;
            }
        dot.SetPixels32(dp); dot.Apply();
    }

    void MakeSquare()
    {
        squareHue = H;
        var px = new Color32[SquarePx * SquarePx];
        for (int y = 0; y < SquarePx; y++)
            for (int x = 0; x < SquarePx; x++)
                px[y * SquarePx + x] = UnityEngine.Color.HSVToRGB(H, x / (SquarePx - 1f), y / (SquarePx - 1f));
        square.SetPixels32(px); square.Apply();
    }
}
