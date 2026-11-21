using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Vespera;

internal sealed record Frame(string File, int Milliseconds);
internal sealed record ExitRoute(int After, string Scene);
internal sealed record GestureSpan(int From, int Until);

internal sealed class Scene
{
    private readonly Frame[] frames;
    private readonly long[] ends;
    private readonly long[] exitPoints;
    private readonly (long At, string Scene)[] exitRoutes;
    private readonly (long From, long Until)[] gestures;
    public bool HasExitRoutes => exitRoutes.Length > 0;
    public int LoopFrom { get; }
    public long OutroFrom { get; }
    public long IntroLength => LoopFrom == 0 ? 0 : ends[LoopFrom - 1];
    public long Length => ends[^1];
    public int Count => frames.Length;

    public Scene(Frame[] frames, int loopFrom, int[]? exitAfter, int outroFrom = 0, ExitRoute[]? routes = null, GestureSpan[]? finishGestures = null)
    {
        if (frames.Length == 0 || loopFrom < 0 || loopFrom >= frames.Length || outroFrom < 0 || outroFrom >= frames.Length ||
            frames.Any(frame => frame.Milliseconds <= 0))
            throw new InvalidDataException("Проверь длительности и точку повтора в pack.json.");
        this.frames = frames;
        LoopFrom = loopFrom;
        ends = new long[frames.Length];
        for (int i = 0; i < frames.Length; i++)
            ends[i] = (i == 0 ? 0 : ends[i - 1]) + frames[i].Milliseconds;
        OutroFrom = outroFrom == 0 ? 0 : ends[outroFrom - 1];
        int[] cuts = exitAfter ?? [];
        if (cuts.Any(index => index <= loopFrom || index > frames.Length))
            throw new InvalidDataException("Проверь точки завершения сцены в pack.json.");
        exitPoints = cuts.Select(index => ends[index - 1]).Distinct().Order().ToArray();
        if (routes?.Any(route => route.After < Math.Max(1, loopFrom) || route.After > frames.Length) == true ||
            (routes is { Length: > 0 } && routes.Select(route => route.After).Distinct().Count() != routes.Length))
            throw new InvalidDataException("Проверь точки выхода из отдыха в pack.json.");
        exitRoutes = (routes ?? []).OrderBy(route => route.After)
            .Select(route => (ends[route.After - 1], route.Scene)).ToArray();
        if (finishGestures?.Any(span => span.From < loopFrom || span.Until > frames.Length ||
               span.From >= span.Until) == true)
            throw new InvalidDataException("Проверь границы жестов в pack.json.");
        gestures = (finishGestures ?? []).Select(span =>
            (span.From == 0 ? 0 : ends[span.From - 1], ends[span.Until - 1])).ToArray();
    }

    public string At(long elapsed)
    {
        if (elapsed >= Length) elapsed = IntroLength + (elapsed - IntroLength) % (Length - IntroLength);
        int frame = Array.BinarySearch(ends, elapsed + 1);
        return frames[frame < 0 ? ~frame : frame].File;
    }

    public long EndOfCycle(long elapsed)
    {
        if (elapsed <= Length) return Length;
        long loopLength = Length - IntroLength;
        long position = (elapsed - Length) % loopLength;
        return position == 0 ? elapsed : elapsed + loopLength - position;
    }

    public (long At, string Scene) NextExitRoute(long elapsed)
    {
        long end = EndOfCycle(elapsed);
        long position = Length - (end - elapsed);
        foreach (var route in exitRoutes)
            if (route.At >= position) return (elapsed + route.At - position, route.Scene);
        var first = exitRoutes[0];
        return (end + first.At - IntroLength, first.Scene);
    }

    private long LoopPosition(long elapsed) => elapsed >= Length
        ? IntroLength + (elapsed - IntroLength) % (Length - IntroLength)
        : elapsed;

    public bool IsFinishingGesture(long elapsed)
    {
        long position = LoopPosition(elapsed);
        foreach (var (from, until) in gestures)
            if (position >= from && position < until) return true;
        return false;
    }

    public string LatestExitRoute(long elapsed)
    {
        long position = LoopPosition(elapsed);
        for (int i = exitRoutes.Length - 1; i >= 0; i--)
            if (exitRoutes[i].At <= position) return exitRoutes[i].Scene;
        return exitRoutes[0].Scene;
    }

    public long NextLoopAnchor(long elapsed, string scene)
    {
        long position = LoopPosition(elapsed);
        long? first = null;
        foreach (var route in exitRoutes)
        {
            if (route.Scene != scene) continue;
            first ??= route.At;
            if (route.At >= position) return route.At;
        }
        return first ?? IntroLength;
    }

    public long NextExit(long elapsed)
    {
        long end = EndOfCycle(elapsed);
        long position = Length - (end - elapsed);
        int index = Array.BinarySearch(exitPoints, position);
        if (index < 0) index = ~index;
        return index < exitPoints.Length ? elapsed + exitPoints[index] - position : end;
    }
}

internal sealed class AnimationPack
{
    private sealed record FrameSpec(string File, int Ms);
    private sealed record SceneSpec(string Label, int LoopFrom, FrameSpec[] Frames, int[]? ExitAfter, int OutroFrom, ExitRoute[]? ExitRoutes, GestureSpan[]? FinishGestures);
    private sealed record PackSpec(Dictionary<string, SceneSpec> Scenes, string[] LookFrames, string[]? LookBlinkFrames, bool InPlaceStageGaze, string? SwimsuitFramePrefix);

    private readonly string[] looks;
    private readonly string[]? blinkLooks;
    public bool InPlaceStageGaze { get; }
    public IReadOnlyList<string> Files { get; }
    public IReadOnlyDictionary<string, Scene> Scenes { get; }
    public IReadOnlyDictionary<string, string> Labels { get; }

    private AnimationPack(Dictionary<string, Scene> scenes, Dictionary<string, string> labels, string[] looks, string[]? blinkLooks, string[] files, bool inPlaceStageGaze)
    {
        Scenes = scenes;
        Labels = labels;
        this.looks = looks;
        this.blinkLooks = blinkLooks;
        Files = files;
        InPlaceStageGaze = inPlaceStageGaze;
    }

    public static AnimationPack Load(string root, double rate = 1.0, bool swimsuitOnly = false)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        PackSpec data = JsonSerializer.Deserialize<PackSpec>(File.ReadAllText(Path.Combine(root, "pack.json")), options)
            ?? throw new InvalidDataException("Не удалось прочитать artwork/pack.json.");
        CheckWardrobe(data);
        var files = data.Scenes.Values.SelectMany(scene => scene.Frames.Select(frame => frame.File))
            .Concat(data.LookFrames).Concat(data.LookBlinkFrames ?? []).Distinct().Order(StringComparer.Ordinal).ToArray();
        foreach (string file in files)
            if (Path.IsPathRooted(file) || file.Contains('\\') || file.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException($"Некорректное имя кадра: {file}.");

        var scenes = data.Scenes.ToDictionary(pair => pair.Key, pair =>
            new Scene(Retime(pair.Value.Frames, rate),
                pair.Value.LoopFrom, pair.Value.ExitAfter, pair.Value.OutroFrom, pair.Value.ExitRoutes, pair.Value.FinishGestures));
        foreach (var (name, scene) in data.Scenes)
        {
            foreach (var route in scene.ExitRoutes ?? [])
            {
                if (!PropPlayback.IsOutro(route.Scene) || !scenes.TryGetValue(route.Scene, out var target))
                    throw new InvalidDataException($"Не найдена сцена выхода {route.Scene}.");
                int frame = PropPlayback.IsB2Work(name) ? route.After - 1
                    : route.After == scene.Frames.Length ? scene.LoopFrom : route.After;
                if (scene.Frames[frame].File != target.At(0))
                    throw new InvalidDataException($"Кадр выхода {route.Scene} не совпадает с позой.");
            }
        }
        string[] required = swimsuitOnly
            ? ["smoke", "smoke-out", "b2-work", "b2-work-out", "b2-dress", "b2-settle",
               "b2-rise-side", "b2-rise-cross", "b2-rise-knee", "b2-rise-phone"]
            : ["idle", "work", "smoke", "step-left", "step-right", "step-up", "step-down",
               "step-up-left", "step-up-right", "step-down-left", "step-down-right", "work-out", "smoke-out", .. DanceRotation.Scenes];
        foreach (string key in required)
            if (!scenes.ContainsKey(key)) throw new InvalidDataException($"В pack.json нужна сцена {key}.");
        if (data.LookFrames.Length == 0 ||
            (data.LookBlinkFrames is not null && data.LookBlinkFrames.Length != data.LookFrames.Length))
            throw new InvalidDataException("Набор направлений взгляда и морганий неполон.");
        return new AnimationPack(scenes, data.Scenes.ToDictionary(pair => pair.Key, pair => pair.Value.Label),
            data.LookFrames, data.LookBlinkFrames, files, data.InPlaceStageGaze);
    }

    private static Frame[] Retime(FrameSpec[] frames, double rate)
    {
        long source = 0;
        long output = 0;
        var result = new Frame[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            FrameSpec frame = frames[i];
            if (frame.Ms <= 0) throw new InvalidDataException("Длительность кадра должна быть больше нуля.");
            source += frame.Ms;
            long next = Math.Max(output + 1, (long)Math.Round(source / rate));
            result[i] = new(frame.File, checked((int)(next - output)));
            output = next;
        }
        return result;
    }

    private static void CheckWardrobe(PackSpec pack)
    {
        if (pack.SwimsuitFramePrefix is not { Length: > 0 } prefix) return;
        if (!pack.Scenes.TryGetValue("smoke", out SceneSpec? rest) || !prefix.EndsWith("/", StringComparison.Ordinal))
            throw new InvalidDataException("В pack.json не указан комплект кадров B2.");
        bool IsB2(FrameSpec frame) => frame.File.StartsWith(prefix, StringComparison.Ordinal);
        if (rest.Frames.Skip(rest.LoopFrom).Any(frame => !IsB2(frame)) ||
            rest.Frames.SkipWhile(frame => !IsB2(frame)).Any(frame => !IsB2(frame)))
            throw new InvalidDataException("В сцену B2 попал кадр из другого костюма.");

        foreach (var (name, scene) in pack.Scenes)
        {
            bool wardrobeExit = name == "b2-dress" || PropPlayback.IsRestOutro(name);
            if (wardrobeExit)
            {
                if (scene.Frames.Length == 0 || !IsB2(scene.Frames[0]) ||
                    scene.Frames.SkipWhile(IsB2).Any(IsB2))
                    throw new InvalidDataException($"Во время переодевания снова появился прежний кадр в {name}.");
            }
            else if (name.StartsWith("b2-", StringComparison.Ordinal) && scene.Frames.Any(frame => !IsB2(frame)))
                throw new InvalidDataException($"В сцене {name} смешаны кадры двух костюмов.");
        }
    }

    public string Look(double dx, double dy, bool blink = false)
    {
        double heading = (Math.Atan2(dx, -dy) / (2 * Math.PI) + 1) % 1;
        int index = (int)Math.Round(heading * looks.Length, MidpointRounding.AwayFromZero) % looks.Length;
        return blink && blinkLooks is not null ? blinkLooks[index] : looks[index];
    }

    public void CheckPlayback(bool swimsuitOnly = false)
    {
        foreach (var pair in Scenes)
        {
            Scene scene = pair.Value;
            if (scene.Count < 2 ||
                scene.At(scene.Length) != scene.At(scene.IntroLength) ||
                scene.EndOfCycle(scene.Length - 1) != scene.Length ||
                scene.EndOfCycle(scene.Length) != scene.Length ||
                scene.EndOfCycle(scene.Length + 1) != scene.Length * 2 - scene.IntroLength)
                throw new InvalidDataException($"Сбой воспроизведения сцены {pair.Key}.");
        }
        Frame[] example = [new("", 100), new("", 100), new("", 100), new("", 100)];
        var cuttable = new Scene(example, 1, [2, 3]);
        if (cuttable.NextExit(80) != 200 || cuttable.NextExit(200) != 200 ||
            cuttable.NextExit(301) != 400 || cuttable.NextExit(401) != 500 ||
            cuttable.NextExit(599) != 600 || cuttable.NextExit(700) != 700)
            throw new InvalidDataException("Сцена должна завершаться в ближайшей точке выхода.");
        if (!swimsuitOnly && Look(1, 0) == Look(-1, 0))
            throw new InvalidDataException("Повороты налево и направо должны отличаться.");
    }
}
