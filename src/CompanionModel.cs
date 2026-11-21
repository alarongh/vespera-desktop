using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace Vespera;

internal sealed class CompanionModel : IDisposable
{
    private readonly string root;
    private readonly bool swimsuit;
    private readonly string? speedPath;
    public ActivityRules Rules { get; }
    public AnimationPack Pack { get; private set; }
    public PropPlayback Props { get; private set; }
    public SpriteRenderer Renderer { get; }
    public double Speed { get; private set; }
    
    public CompanionModel(string root, bool swimsuit = false, string? speedPath = null)
    {
        this.root = root;
        this.swimsuit = swimsuit;
        this.speedPath = speedPath;
        Rules = ActivityRules.Load(root);
        Speed = ReadSpeed(speedPath, Rules.AnimationSpeed);
        Pack = AnimationPack.Load(root, Speed, swimsuit);
        Props = new PropPlayback(Pack);
        Renderer = new SpriteRenderer(root, Pack.Files);
    }

    private static double ReadSpeed(string? path, double fallback)
    {
        if (path is null) return fallback;
        try
        {
            return double.TryParse(File.ReadAllText(path), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && value is >= .6 and <= 1.6 ? value : fallback;
        }
        catch (IOException) { return fallback; }
        catch (UnauthorizedAccessException) { return fallback; }
    }

    public void SetSpeed(double value)
    {
        if (value is < .6 or > 1.6 || !double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        AnimationPack next = AnimationPack.Load(root, value, swimsuit);
        if (speedPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(speedPath)!);
            string staging = speedPath + ".tmp";
            File.WriteAllText(staging, value.ToString(CultureInfo.InvariantCulture));
            File.Move(staging, speedPath, true);
        }
        Pack = next;
        Props = new PropPlayback(next);
        Speed = value;
    }

    public void Dispose() => Renderer.Dispose();

    public static void CheckHandoffs(string stageRoot, AnimationPack stage, string poolRoot, AnimationPack pool)
    {
        static byte[] Hash(SpriteArchive? archive, string folder, string key)
        {
            BitmapSource? png = archive is null ? SpriteArchive.LoadPng(folder, key) : null;
            int width = archive?.Width ?? png!.PixelWidth;
            int height = archive?.Height ?? png!.PixelHeight;
            var pixels = new byte[checked(width * height * 4)];
            if (archive is not null) archive.ReadCanvas(key, pixels);
            else png!.CopyPixels(pixels, width * 4, 0);
            return SHA256.HashData(pixels);
        }

        using var stageArchive = SpriteArchive.Open(stageRoot, stage.Files);
        using var poolArchive = SpriteArchive.Open(poolRoot, pool.Files);
        byte[] idle = Hash(stageArchive, stageRoot, stage.Scenes["idle"].At(0));
        if (!idle.AsSpan().SequenceEqual(Hash(poolArchive, poolRoot, pool.Scenes["smoke"].At(0))))
            throw new InvalidDataException("Переодевание в B2 должно начинаться с позы ожидания B1.");
        foreach (var (name, scene) in pool.Scenes)
        {
            if ((name == "b2-dress" || PropPlayback.IsRestOutro(name)) &&
                !idle.AsSpan().SequenceEqual(Hash(poolArchive, poolRoot, scene.At(scene.Length - 1))))
                throw new InvalidDataException($"Последний кадр {name} должен совпадать с позой ожидания B1.");
        }
    }
}
