using System;
using System.IO;

namespace Vespera;

internal sealed class StageRestPlayback(Scene smoke, Scene outro)
{
    public const string Smoke = "stage-smoke";
    public const string Outro = "stage-smoke-out";

    // In a full idle cycle, the break occupies three fifths of the time.
    private readonly long standingTime = (2 * (smoke.Length + outro.Length) + 1) / 3;
    private long? standingSince;
    private string? deferredActivity;
    private bool closeRequested;

    public static StageRestPlayback? From(AnimationPack pack) =>
        pack.Scenes.TryGetValue(Smoke, out var smoke) && pack.Scenes.TryGetValue(Outro, out var outro)
            ? new StageRestPlayback(smoke, outro) : null;

    public void Reset()
    {
        standingSince = null;
        deferredActivity = null;
        closeRequested = false;
    }

    public string Select(string current, long elapsed, long now, string wanted, bool quiet, bool scrolling)
    {
        if (current is Smoke or Outro)
        {
            standingSince = null;
            if (wanted == "work" || (wanted != "idle" && deferredActivity is null))
                deferredActivity = wanted;
            closeRequested |= scrolling || wanted != "idle";
            if (current == Smoke)
                return elapsed >= smoke.Length || (closeRequested && elapsed >= smoke.OutroFrom) ? Outro : Smoke;
            if (elapsed < outro.Length) return Outro;

            string next = wanted != "idle" ? wanted : deferredActivity ?? wanted;
            Reset();
            return next;
        }

        if (current != "idle" || wanted != "idle" || scrolling)
        {
            Reset();
            return wanted;
        }

        standingSince ??= now;
        return quiet && now - standingSince >= standingTime ? Smoke : wanted;
    }

    public static void Check(AnimationPack pack)
    {
        StageRestPlayback? playback = From(pack);
        if (playback is null) return;
        Scene smoke = pack.Scenes[Smoke];
        Scene outro = pack.Scenes[Outro];
        long standing = playback.standingTime;
        long breakTime = smoke.Length + outro.Length;
        if (Math.Abs((double)breakTime / (breakTime + standing) - .60) > .001 ||
            smoke.OutroFrom <= 0 || smoke.OutroFrom >= smoke.Length ||
            playback.Select("idle", 0, 0, "idle", true, false) != "idle" ||
            playback.Select("idle", standing - 1, standing - 1, "idle", true, false) != "idle" ||
            playback.Select("idle", standing, standing, "idle", false, false) != "idle" ||
            playback.Select("idle", standing, standing, "idle", true, false) != Smoke ||
            playback.Select(Smoke, smoke.Length, standing + smoke.Length, "idle", true, false) != Outro ||
            playback.Select(Outro, outro.Length, standing + breakTime, "idle", true, false) != "idle")
            throw new InvalidDataException("Проверь расписание перекура B1.");

        playback.Reset();
        if (playback.Select(Smoke, smoke.OutroFrom - 1, 200, "work", false, false) != Smoke ||
            playback.Select(Smoke, smoke.OutroFrom, 201, "idle", true, false) != Outro ||
            playback.Select(Outro, outro.Length - 1, 202, "idle", true, false) != Outro ||
            playback.Select(Outro, outro.Length, 203, "idle", true, false) != "work")
            throw new InvalidDataException("Перекур B1 должен закончиться перед ответом на печать.");
    }
}
