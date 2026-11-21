using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace Vespera;

internal sealed class GazeOverlay
{
    internal sealed class Patch(int x, int y, int width, int height, byte[][] directions)
    {
        public int Direction(Point pointer)
        {
            double dx = pointer.X - (x + width / 2.0);
            double dy = pointer.Y - (y + height / 2.0);
            if (dx * dx + dy * dy < 36) return -1;
            double turn = Math.Atan2(dx, -dy) / (2 * Math.PI) + 1;
            return (int)Math.Round(turn * directions.Length) % directions.Length;
        }

        public void Apply(byte[] canvas, int stride, int direction)
        {
            byte[] patch = directions[direction];
            int line = width * 4;
            for (int row = 0; row < height; row++)
                Buffer.BlockCopy(patch, row * line, canvas, (y + row) * stride + x * 4, line);
        }
    }

    private readonly Dictionary<string, Patch> patches = new(StringComparer.Ordinal);
    public Patch? Find(string file) => patches.GetValueOrDefault(file);

    public static GazeOverlay? Open(string root, int width, int height)
    {
        string path = Path.Combine(root, "gaze.v1");
        if (!File.Exists(path)) return null;
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        string format = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (format == "GZ02")
        {
            byte[] expected = SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "pack.json")));
            if (!CryptographicOperations.FixedTimeEquals(reader.ReadBytes(expected.Length), expected))
                throw new InvalidDataException("Кадры взгляда относятся к другой версии анимаций.");
        }
        else if (format != "GZ01")
            throw new InvalidDataException("Формат анимации взгляда не поддерживается.");
        int count = reader.ReadInt32();
        if (count is < 1 or > 3000) throw new InvalidDataException("Некорректный набор кадров взгляда.");
        var overlay = new GazeOverlay();
        for (int i = 0; i < count; i++)
        {
            string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
            int x = reader.ReadUInt16(), y = reader.ReadUInt16();
            int w = reader.ReadUInt16(), h = reader.ReadUInt16();
            if (w is < 1 or > 130 || h is < 1 or > 60 || x + w > width || y + h > height)
                throw new InvalidDataException("Кадр взгляда выходит за границы спрайта.");
            int length = w * h * 4;
            var directions = new byte[8][];
            for (int j = 0; j < directions.Length; j++)
            {
                directions[j] = reader.ReadBytes(length);
                if (directions[j].Length != length) throw new EndOfStreamException();
            }
            overlay.patches.Add(name, new Patch(x, y, w, h, directions));
        }
        return overlay;
    }
}
