using UnityEngine;

namespace Graphs.UI;

/// Procedural 📈-style icon for the top-right toolbar button: axes plus
/// a polyline trending up-right. Painted in pure white so the caller can
/// tint it via `unityBackgroundImageTintColor`.
public static class GraphIcon
{
    private const int Size = 64;

    public static Sprite Create()
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "graphs-icon",
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[Size * Size];
        var ink = new Color32(255, 255, 255, 255);

        // Content sits inside a centered 40×40 region (~63% fill) so the
        // icon's visual weight matches vanilla square-toggle icons when
        // stretched into the button.
        // Texture2D pixel space: y=0 is the BOTTOM.
        DrawLine(px, 15, 14, 15, 50, 1, ink);  // vertical axis
        DrawLine(px, 15, 14, 50, 14, 1, ink);  // horizontal axis

        // Polyline trending up-right with one dip — the 📈 silhouette.
        // Radius-2 nib keeps the line readable at the ~24px display size.
        var pts = new[]
        {
            new Vector2Int(18, 18),
            new Vector2Int(26, 28),
            new Vector2Int(33, 24),
            new Vector2Int(40, 34),
            new Vector2Int(49, 46),
        };
        for (int i = 0; i < pts.Length - 1; i++)
            DrawLine(px, pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, 2, ink);

        // Vertex dots emphasize the joints as discrete data points.
        foreach (var p in pts) StampDot(px, p.x, p.y, 2, ink);

        tex.SetPixels32(px);
        tex.Apply(false);

        var sprite = Sprite.Create(
            tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect);
        sprite.name = "graphs-icon-sprite";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    /// Bresenham line with a circular brush of `radius`. Radius 1 → 3px nib,
    /// radius 2 → 5px nib (with a soft circular footprint).
    private static void DrawLine(Color32[] px, int x0, int y0, int x1, int y1, int radius, Color32 c)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int x = x0, y = y0;
        while (true)
        {
            StampDot(px, x, y, radius, c);
            if (x == x1 && y == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }

    private static void StampDot(Color32[] px, int cx, int cy, int radius, Color32 c)
    {
        int r2 = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dy * dy > r2) continue;
            int x = cx + dx, y = cy + dy;
            if ((uint)x >= Size || (uint)y >= Size) continue;
            px[y * Size + x] = c;
        }
    }
}
