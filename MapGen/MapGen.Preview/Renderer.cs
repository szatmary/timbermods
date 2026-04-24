using MapGen;
using SkiaSharp;

namespace MapGen.Preview;

public static class Renderer
{
    private static readonly SKColor[] BiomeColors =
    {
        new(120, 180,  90, 255),  // Meadow
        new( 40, 100,  60, 255),  // Forest
        new(180, 150, 100, 255),  // Badland
        new(120, 110, 100, 255),  // Rocky
        new( 40,  80, 160, 255),  // Sea
        new(140, 100, 160, 255),  // Crater
        new(240, 210, 140, 255),  // Start
    };

    public static void RenderToPng(MapData map, string outputPath, int scale = 4)
    {
        int w = map.Width * scale;
        int h = map.Height * scale;
        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);

        using var biomePaint = new SKPaint();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var b = map.Biomes[map.MetaIndex(x / 8, y / 8)];
            var baseColor = BiomeColors[(int)b];
            int height = map.TopHeight(x, y);
            float shade = 0.5f + 0.5f * (height / 20f);
            shade = System.Math.Clamp(shade, 0.4f, 1f);
            var c = new SKColor(
                (byte)(baseColor.Red * shade),
                (byte)(baseColor.Green * shade),
                (byte)(baseColor.Blue * shade));
            biomePaint.Color = c;
            canvas.DrawRect(x * scale, y * scale, scale, scale, biomePaint);
        }

        using var waterPaint = new SKPaint { Color = new SKColor(60, 120, 200, 180) };
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (map.WaterDepths[map.ColumnIndex(x, y)] > 0)
                canvas.DrawRect(x * scale, y * scale, scale, scale, waterPaint);
        }

        foreach (var e in map.Entities)
        {
            var color = e.Kind switch
            {
                EntityKind.Tree => new SKColor(20, 80, 30),
                EntityKind.Resource => new SKColor(255, 100, 100),
                EntityKind.Thorn => new SKColor(120, 50, 50),
                EntityKind.Ruin => new SKColor(120, 100, 80),
                EntityKind.Blockage => new SKColor(80, 60, 40),
                EntityKind.Relic => new SKColor(255, 220, 100),
                EntityKind.UnstableCore => new SKColor(255, 60, 60),
                EntityKind.GeothermalVent => new SKColor(255, 150, 50),
                EntityKind.WaterSource => new SKColor(100, 180, 255),
                EntityKind.BadwaterSource => new SKColor(200, 100, 220),
                EntityKind.Slope => new SKColor(200, 200, 200),
                EntityKind.StartMarker => new SKColor(255, 255, 0),
                _ => SKColors.Magenta,
            };
            using var p = new SKPaint { Color = color };
            int r = e.Kind == EntityKind.StartMarker ? scale * 3 : System.Math.Max(1, scale / 2);
            canvas.DrawCircle(e.Coord.X * scale + scale / 2, e.Coord.Y * scale + scale / 2, r, p);
        }

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = System.IO.File.Create(outputPath);
        data.SaveTo(fs);
    }
}
