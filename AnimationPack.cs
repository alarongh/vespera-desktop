using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Vespera;

internal sealed record Frame(BitmapSource Image, int Milliseconds);

internal sealed class Scene
{
    private readonly Frame[] frames;
    private readonly long[] ends;
    public int LoopFrom { get; }
    public long IntroLength => LoopFrom == 0 ? 0 : ends[LoopFrom - 1];
    public long Length => ends[^1];
    public int Count => frames.Length;

    public Scene(Frame[] frames, int loopFrom)
    {
        if (frames.Length == 0 || loopFrom < 0 || loopFrom >= frames.Length ||
            frames.Any(frame => frame.Milliseconds <= 0))
            throw new InvalidDataException("Проверь длительности и точку повтора в pack.json.");
        this.frames = frames;
        LoopFrom = loopFrom;
        ends = new long[frames.Length];
        for (int i = 0; i < frames.Length; i++)
            ends[i] = (i == 0 ? 0 : ends[i - 1]) + frames[i].Milliseconds;
    }

    public BitmapSource At(long elapsed)
    {
        if (elapsed >= Length) elapsed = IntroLength + (elapsed - IntroLength) % (Length - IntroLength);
        int frame = Array.BinarySearch(ends, elapsed + 1);
        return frames[frame < 0 ? ~frame : frame].Image;
    }
}

internal sealed class AnimationPack
{
    private sealed record FrameSpec(string File, int Ms);
    private sealed record SceneSpec(string Label, int LoopFrom, FrameSpec[] Frames);
    private sealed record PackSpec(Dictionary<string, SceneSpec> Scenes, string[] LookFrames);

    private readonly BitmapSource[] looks;
    public IReadOnlyDictionary<string, Scene> Scenes { get; }
    public IReadOnlyDictionary<string, string> Labels { get; }

    private AnimationPack(Dictionary<string, Scene> scenes, Dictionary<string, string> labels, BitmapSource[] looks)
    {
        Scenes = scenes;
        Labels = labels;
        this.looks = looks;
    }

    public static AnimationPack Load(string root)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        PackSpec data = JsonSerializer.Deserialize<PackSpec>(File.ReadAllText(Path.Combine(root, "pack.json")), options)
            ?? throw new InvalidDataException("Не удалось прочитать artwork/pack.json.");
        BitmapSource LoadImage(string file)
        {
            string path = Path.GetFullPath(Path.Combine(root, file));
            using var input = File.OpenRead(path);
            BitmapDecoder decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource image = decoder.Frames[0];
            image.Freeze();
            return image;
        }

        var scenes = data.Scenes.ToDictionary(pair => pair.Key, pair =>
            new Scene(pair.Value.Frames.Select(frame => new Frame(LoadImage(frame.File), frame.Ms)).ToArray(), pair.Value.LoopFrom));
        foreach (string key in new[] { "idle", "work", "smoke", "step-left", "step-right", "idol-dance" })
            if (!scenes.ContainsKey(key)) throw new InvalidDataException($"В pack.json нужна сцена {key}.");
        if (data.LookFrames.Length == 0) throw new InvalidDataException("В pack.json нужны направления взгляда.");
        return new AnimationPack(scenes, data.Scenes.ToDictionary(pair => pair.Key, pair => pair.Value.Label),
            data.LookFrames.Select(LoadImage).ToArray());
    }

    public BitmapSource Look(double dx, double dy)
    {
        double heading = (Math.Atan2(dx, -dy) / (2 * Math.PI) + 1) % 1;
        int index = (int)Math.Round(heading * looks.Length, MidpointRounding.AwayFromZero) % looks.Length;
        return looks[index];
    }

    public void CheckPlayback()
    {
        foreach (var pair in Scenes)
        {
            Scene scene = pair.Value;
            if (scene.Count < 2 || scene.At(0).PixelHeight < 350 ||
                !ReferenceEquals(scene.At(scene.Length), scene.At(scene.IntroLength)))
                throw new InvalidDataException($"Сбой воспроизведения сцены {pair.Key}.");
        }
        if (ReferenceEquals(Look(1, 0), Look(-1, 0)))
            throw new InvalidDataException("Повороты налево и направо должны отличаться.");
    }
}
