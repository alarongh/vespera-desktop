using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vespera;

internal sealed class SpriteArchive : IDisposable
{
    private const string ArchiveName = "sprites.v1";
    private const uint Magic = 0x31505356;
    private readonly FileStream stream;
    private readonly Dictionary<string, Entry> entries;
    private readonly byte[] compressed;
    private readonly byte[] crop;
    public int Width { get; }
    public int Height { get; }

    private readonly record struct Entry(long Offset, int Length, Int32Rect Bounds);

    private SpriteArchive(FileStream stream, Dictionary<string, Entry> entries, int width, int height, int largest)
    {
        this.stream = stream;
        this.entries = entries;
        Width = width;
        Height = height;
        compressed = new byte[largest];
        crop = new byte[checked(width * height * 4)];
    }

    private static byte[] ManifestHash(string root) =>
        SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "pack.json")));

    public static SpriteArchive? Open(string root, IReadOnlyList<string> files)
    {
        string path = Path.Combine(root, ArchiveName);
        if (!File.Exists(path)) return null;
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Некорректный архив кадров Весперы.");
            if (!reader.ReadBytes(32).AsSpan().SequenceEqual(ManifestHash(root)))
            {
                stream.Dispose();
                return null;
            }
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            long table = reader.ReadInt64();
            if (width < 1 || height < 350 || width > 4096 || height > 4096 ||
                table < stream.Position || table > stream.Length - sizeof(int))
                throw new InvalidDataException("Некорректные размеры архива кадров Весперы.");

            stream.Position = table;
            int count = reader.ReadInt32();
            if (count != files.Count) throw new InvalidDataException("Неполный архив кадров Весперы.");
            var entries = new Dictionary<string, Entry>(count, StringComparer.Ordinal);
            int largest = 0;
            foreach (string file in files)
            {
                var rect = new Int32Rect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                long offset = reader.ReadInt64();
                int length = reader.ReadInt32();
                if (rect.Width < 0 || rect.Height < 0 || rect.X < 0 ||
                    rect.Y < 0 || rect.X > width - rect.Width || rect.Y > height - rect.Height ||
                    offset < 52 || length < 0 || length > width * height * 4 + 1024 ||
                    offset > table - length || ((rect.Width == 0 || rect.Height == 0) != (length == 0)))
                    throw new InvalidDataException($"Некорректная запись кадра {file}.");
                entries.Add(file, new Entry(offset, length, rect));
                largest = Math.Max(largest, length);
            }
            return new SpriteArchive(stream, entries, width, height, largest);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void ReadCanvas(string file, byte[] pixels)
    {
        Array.Clear(pixels);
        Entry entry = entries[file];
        if (entry.Length == 0) return;
        stream.Position = entry.Offset;
        stream.ReadExactly(compressed.AsSpan(0, entry.Length));
        using var block = new MemoryStream(compressed, 0, entry.Length, false);
        using var decoder = new DeflateStream(block, CompressionMode.Decompress);
        Int32Rect rect = entry.Bounds;
        int rowBytes = rect.Width * 4;
        decoder.ReadExactly(crop.AsSpan(0, rowBytes * rect.Height));
        int stride = Width * 4;
        for (int row = 0; row < rect.Height; row++)
            Buffer.BlockCopy(crop, row * rowBytes, pixels, (rect.Y + row) * stride + rect.X * 4, rowBytes);
    }

    public static void Build(string root, IReadOnlyList<string> files)
    {
        string path = Path.Combine(root, ArchiveName);
        string temporary = path + ".building";
        BitmapSource first = LoadPng(root, files[0]);
        int width = first.PixelWidth;
        int height = first.PixelHeight;
        var pixels = new byte[checked(width * height * 4)];
        var entries = new List<Entry>(files.Count);
        try
        {
            using (var output = File.Create(temporary))
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(Magic);
                writer.Write(ManifestHash(root));
                writer.Write(width);
                writer.Write(height);
                long tableLocation = output.Position;
                writer.Write(0L);
                foreach (string file in files)
                {
                    ReadPng(root, file, width, height, pixels);
                    Int32Rect rect = Bounds(pixels, width, height);
                    long start = output.Position;
                    if (rect.Width > 0)
                    {
                        using var encoder = new DeflateStream(output, CompressionLevel.Fastest, true);
                        for (int row = 0; row < rect.Height; row++)
                            encoder.Write(pixels.AsSpan(((rect.Y + row) * width + rect.X) * 4, rect.Width * 4));
                    }
                    entries.Add(new Entry(start, checked((int)(output.Position - start)), rect));
                }

                long table = output.Position;
                writer.Write(entries.Count);
                foreach (Entry entry in entries)
                {
                    writer.Write(entry.Bounds.X);
                    writer.Write(entry.Bounds.Y);
                    writer.Write(entry.Bounds.Width);
                    writer.Write(entry.Bounds.Height);
                    writer.Write(entry.Offset);
                    writer.Write(entry.Length);
                }
                output.Position = tableLocation;
                writer.Write(table);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void Verify(string root, IReadOnlyList<string> files)
    {
        using var archive = Open(root, files) ?? throw new InvalidDataException("Архив кадров не найден или устарел.");
        var fromArchive = new byte[archive.Width * archive.Height * 4];
        var fromPng = new byte[fromArchive.Length];
        foreach (string file in files)
        {
            archive.ReadCanvas(file, fromArchive);
            if (!File.Exists(Path.Combine(root, file))) continue;
            ReadPng(root, file, archive.Width, archive.Height, fromPng);
            if (!fromArchive.AsSpan().SequenceEqual(fromPng))
                throw new InvalidDataException($"Пиксели архива отличаются от PNG: {file}.");
        }
    }

    public static BitmapSource LoadPng(string root, string file)
    {
        using var input = File.OpenRead(Path.Combine(root, file));
        BitmapSource source = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        return source.Format == PixelFormats.Pbgra32
            ? source : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
    }

    public static void ReadPng(string root, string file, int width, int height, byte[] pixels)
    {
        BitmapSource image = LoadPng(root, file);
        if (image.PixelWidth != width || image.PixelHeight != height || Math.Abs(image.DpiX - image.DpiY) > 0.01)
            throw new InvalidDataException($"Кадр {file} имеет другой размер или пропорции холста.");
        image.CopyPixels(pixels, width * 4, 0);
    }

    private static Int32Rect Bounds(byte[] pixels, int width, int height)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            int offset = (y * width * 4) + 3;
            for (int x = 0; x < width; x++, offset += 4)
            {
                if (pixels[offset] == 0) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = y;
            }
        }
        return right < 0 ? Int32Rect.Empty : new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }

    public void Dispose() => stream.Dispose();
}

internal sealed class SpriteRenderer : IDisposable
{
    private readonly string root;
    private readonly SpriteArchive? archive;
    private readonly byte[] pixels;
    private readonly Int32Rect canvas;
    private string? displayed;
    private int displayedDirection = -1;
    private readonly GazeOverlay? gaze;
    public WriteableBitmap Surface { get; }

    public SpriteRenderer(string root, IReadOnlyList<string> files)
    {
        this.root = root;
        archive = SpriteArchive.Open(root, files);
        if (archive is null)
        {
            foreach (string file in files)
                if (!File.Exists(Path.Combine(root, file)))
                    throw new InvalidDataException("Отсутствует архив кадров. Распакуйте полную сборку из GitHub Releases.");
        }
        BitmapSource? first = archive is null ? SpriteArchive.LoadPng(root, files[0]) : null;
        int width = archive?.Width ?? first!.PixelWidth;
        int height = archive?.Height ?? first!.PixelHeight;
        canvas = new Int32Rect(0, 0, width, height);
        pixels = new byte[checked(width * height * 4)];
        gaze = GazeOverlay.Open(root, width, height);
        Surface = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
    }

    public void Present(string file, Point? pointer = null)
    {
        GazeOverlay.Patch? eye = pointer is null ? null : gaze?.Find(file);
        int direction = eye is null ? -1 : eye.Direction(pointer!.Value);
        if (file == displayed && direction == displayedDirection) return;
        if (archive is null)
            SpriteArchive.ReadPng(root, file, canvas.Width, canvas.Height, pixels);
        else
            archive.ReadCanvas(file, pixels);
        if (direction >= 0) eye!.Apply(pixels, canvas.Width * 4, direction);
        Surface.WritePixels(canvas, pixels, canvas.Width * 4, 0);
        displayedDirection = direction;
        displayed = file;
    }

    public void Dispose() => archive?.Dispose();
}
