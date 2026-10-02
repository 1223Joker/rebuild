using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using Rebuild.Sim.MapGen;

namespace Rebuild.Tools;

/// <summary>Minimal RGB PNG writer (https://www.w3.org/TR/png/) and the map preview palette.</summary>
public static class Png
{
    public static byte[] Encode(int width, int height, byte[] rgb)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0); // filter: none
                z.Write(rgb, y * width * 3, width * 3);
            }
        }
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type RGB
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        s.Write(buffer);
        var typeAndData = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) typeAndData[i] = (byte)type[i];
        data.CopyTo(typeAndData, 4);
        s.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32.HashToUInt32(typeAndData));
        s.Write(buffer);
    }

    private static readonly int[] TeamColors = { 0xE6194B, 0x4363D8, 0xFFE119, 0x3CB44B, 0xF58231, 0x911EB4, 0x42D4F4, 0xF032E6 };

    /// <summary>Top-down preview: terrain colours shaded by height, objects, resources, starts and lairs.</summary>
    public static byte[] RenderMap(MapData map, int scale)
    {
        int s = map.Edge, w = s * scale;
        var rgb = new byte[w * w * 3];
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                int i = y * s + x;
                int color = TileColor(map, i);
                for (int py = 0; py < scale; py++)
                    for (int px = 0; px < scale; px++)
                        Put(rgb, w, x * scale + px, y * scale + py, color);
            }
        }
        for (int k = 0; k < map.Starts.Length; k++)
        {
            var st = map.Starts[k];
            int color = TeamColors[st.Team % TeamColors.Length];
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    bool border = System.Math.Abs(dx) == 3 || System.Math.Abs(dy) == 3;
                    for (int py = 0; py < scale; py++)
                        for (int px = 0; px < scale; px++)
                            Put(rgb, w, (st.X + dx) * scale + px, (st.Y + dy) * scale + py, border ? 0xFFFFFF : color);
                }
        }
        return Encode(w, w, rgb);
    }

    private static int TileColor(MapData map, int i)
    {
        int h = map.Height[i];
        switch ((MapObject)map.Object[i])
        {
            case MapObject.Tree: return Shade(0x2E6B2E, h);
            case MapObject.Stone: return 0xBDBDBD;
            case MapObject.Game: return 0x8B5A2B;
            case MapObject.Lair: return 0xFF0000;
        }
        switch ((Resource)map.Resource[i])
        {
            case Resource.Coal: return 0x222222;
            case Resource.Iron: return 0xB7410E;
            case Resource.Gold: return 0xFFD700;
            case Resource.Fish: return 0x3A7BD5;
        }
        return (Terrain)map.Terrain[i] switch
        {
            Terrain.Water => 0x1F4E8C,
            Terrain.Fertile => Shade(0xA8C64E, h),
            Terrain.Mountain => Shade(0x7A6A58, h),
            _ => (map.Flags[i] & (byte)TileFlags.Buildable) != 0 ? Shade(0x6FA84A, h) : Shade(0x5E8C3E, h),
        };
    }

    private static int Shade(int color, int height)
    {
        int f = 80 + System.Math.Min(height, 120) * 3 / 2; // 80..260 percent-ish
        int r = System.Math.Min(255, ((color >> 16) & 255) * f / 140);
        int g = System.Math.Min(255, ((color >> 8) & 255) * f / 140);
        int b = System.Math.Min(255, (color & 255) * f / 140);
        return (r << 16) | (g << 8) | b;
    }

    private static void Put(byte[] rgb, int w, int x, int y, int color)
    {
        if (x < 0 || y < 0 || x >= w || y >= w) return;
        int o = (y * w + x) * 3;
        rgb[o] = (byte)(color >> 16);
        rgb[o + 1] = (byte)(color >> 8);
        rgb[o + 2] = (byte)color;
    }
}
