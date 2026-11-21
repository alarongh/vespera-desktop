using System;
using System.IO;
using System.Linq;

namespace Vespera;

internal sealed class PropPlayback(AnimationPack pack)
{
    public const string B2Work = "b2-work";
    private static readonly string[] B2WorkScenes = [B2Work, "b2-work-arc", "b2-work-sweep"];
    private (string Scene, long Started)? workStarted;
    private string? previousB2Work;
    private (long At, string Scene) workExit;
    private long? restStarted;
    private (long At, string Scene) restExit;

    public static bool IsB2Work(string scene) => Array.IndexOf(B2WorkScenes, scene) >= 0;

    public string NextB2Work(string? current = null)
    {
        string[] choices = B2WorkScenes.Where(scene => pack.Scenes.ContainsKey(scene) && scene != (current ?? previousB2Work)).ToArray();
        return previousB2Work = choices.Length == 0 ? B2Work : choices[Random.Shared.Next(choices.Length)];
    }


    public static string? OutroFor(string scene) => scene switch
    {
        "work" => "work-out",
        _ when IsB2Work(scene) => "b2-work-out",
        "smoke" => "smoke-out",
        StageRestPlayback.Smoke => StageRestPlayback.Outro,
        _ => null
    };

    public static bool IsRestOutro(string scene) => scene == "smoke-out" ||
        scene.StartsWith("rest-out-", StringComparison.Ordinal);

    public static bool IsB2WorkOutro(string scene) => scene is "b2-work-out" or "b2-work-out-first";
    public static bool IsOutro(string scene) => scene == "work-out" || IsB2WorkOutro(scene) || IsRestOutro(scene);

    public string Select(string current, long started, long now, string wanted)
    {
        long elapsed = now - started;
        if ((current == "work" || IsB2Work(current)) && wanted != current)
        {
            if (workStarted != (current, started))
            {
                workStarted = (current, started);
                Scene scene = pack.Scenes[current];
                workExit = scene.HasExitRoutes ? scene.NextExitRoute(elapsed)
                    : (scene.NextExit(elapsed), OutroFor(current)!);
            }
            return elapsed < workExit.At ? current : workExit.Scene;
        }

        workStarted = null;
        Scene rest = pack.Scenes["smoke"];
        if (current == "smoke" && wanted != "smoke")
        {
            if (rest.HasExitRoutes)
            {
                if (elapsed < rest.OutroFrom) return wanted;
                if (restStarted != started)
                {
                    restStarted = started;
                    restExit = rest.NextExitRoute(elapsed);
                }
                return elapsed < restExit.At ? current : restExit.Scene;
            }
            if (elapsed >= rest.OutroFrom) return "smoke-out";
        }
        restStarted = null;
        if (IsOutro(current) && elapsed < pack.Scenes[current].Length)
            return current;
        return wanted;
    }

    public static void Check(AnimationPack pack, bool swimsuitOnly = false)
    {
        var playback = new PropPlayback(pack);
        Scene smoke = pack.Scenes["smoke"];
        long lit = smoke.OutroFrom;
        long extinguishEnd = pack.Scenes["smoke-out"].Length;
        bool validEntry = smoke.HasExitRoutes
            ? lit > 0 && lit < smoke.IntroLength &&
                playback.Select("smoke", 0, lit - 1, "look") == "look" &&
                playback.Select("smoke", 0, lit, "look") == "smoke" &&
                playback.Select("smoke", 0, smoke.IntroLength, "look") == "smoke-out"
            : lit > 0 && lit < smoke.IntroLength &&
                playback.Select("smoke", 0, lit - 1, "look") == "look" &&
                playback.Select("smoke", 0, lit, "look") == "smoke-out";
        if (!validEntry ||
            playback.Select("smoke-out", lit, lit + extinguishEnd - 1, "work") != "smoke-out" ||
            playback.Select("smoke-out", lit, lit + extinguishEnd, "work") != "work")
            throw new InvalidDataException("Проверь воспроизведение начала и конца перекура.");

        if (!swimsuitOnly)
        {
            Scene work = pack.Scenes["work"];
            long workEnd = work.NextExit(work.IntroLength + 1);
            if (work.IntroLength <= 0 ||
                playback.Select("work", 0, work.IntroLength + 1, "idle") != "work" ||
                playback.Select("work", 0, workEnd, "idol-dance") != "work-out" ||
                playback.Select("work-out", workEnd, workEnd + pack.Scenes["work-out"].Length, "idol-dance") != "idol-dance")
                throw new InvalidDataException("Плеть должна убираться после окончания текущего взмаха.");

        }

        if (!pack.Scenes.TryGetValue(B2Work, out Scene? baseline)) return;
        string[] workScenes = B2WorkScenes.Where(pack.Scenes.ContainsKey).ToArray();
        foreach (string name in workScenes)
        {
            const long start = 500;
            var work = pack.Scenes[name];
            if (work.Length != baseline.Length || work.IntroLength != baseline.IntroLength ||
                work.At(0) != baseline.At(0) || work.At(work.IntroLength) != baseline.At(baseline.IntroLength))
                throw new InvalidDataException($"Вариант {name} сдвигает темп или начальную позу плети.");
            long[] requests = [work.IntroLength + 1, work.Length - 1, work.Length + 1];
            foreach (long request in requests)
            {
                var response = work.HasExitRoutes ? work.NextExitRoute(request)
                    : (At: work.NextExit(request), Scene: "b2-work-out");
                long finish = start + response.At;
                var closing = new PropPlayback(pack);
                if (closing.Select(name, start, finish - 1, "b2-settle") != name ||
                    closing.Select(name, start, finish, "b2-settle") != response.Scene ||
                    closing.Select(response.Scene, finish, finish + pack.Scenes[response.Scene].Length,
                        "b2-settle") != "b2-settle")
                    throw new InvalidDataException("После взмаха B2 должен плавно вернуться к отдыху.");
            }
        }

        if (workScenes.Length > 1)
        {
            string prior = playback.NextB2Work();
            for (int i = 0; i < 4; i++)
            {
                string next = playback.NextB2Work();
                if (!pack.Scenes.ContainsKey(next) || next == prior)
                    throw new InvalidDataException("Два одинаковых варианта плети B2 подряд.");
                prior = next;
            }
        }
    }
}

internal sealed class DanceRotation(AnimationPack pack)
{
    public static readonly string[] Scenes = ["idol-dance", "idol-dance-shuffle", "idol-dance-turn"];
    private const string StageDance = "idol-dance-stage";
    public readonly string[] Available = pack.Scenes.ContainsKey(StageDance) ? [.. Scenes, StageDance] : Scenes;
    private int index;
    public string Next => Available[index];

    public string Take()
    {
        string scene = Next;
        index = (index + 1) % Available.Length;
        return scene;
    }

    public static void Check(AnimationPack pack)
    {
        var rotation = new DanceRotation(pack);
        foreach (string scene in rotation.Available)
            if (rotation.Take() != scene)
                throw new InvalidDataException("Танцы должны чередоваться.");
        if (rotation.Next != Scenes[0])
            throw new InvalidDataException("Чередование танцев должно замыкаться.");
    }
}
