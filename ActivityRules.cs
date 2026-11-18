using System;
using System.IO;
using System.Text.Json;

namespace Vespera;

internal sealed record ActivityRules(int TypingHoldMs, int ScrollHoldMs, int SmokeAfterMs, int WindowWidth,
    int DanceMinEvents = 12, int DanceMinTypingMs = 1200, bool TypingInAllApps = true)
{
    public static ActivityRules Load(string artwork)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var rules = JsonSerializer.Deserialize<ActivityRules>(File.ReadAllText(Path.Combine(artwork, "activity.json")), options)
            ?? throw new InvalidDataException("Не удалось прочитать artwork/activity.json.");
        if (rules.TypingHoldMs <= 0 || rules.ScrollHoldMs <= 0 || rules.SmokeAfterMs <= 0 || rules.WindowWidth <= 0 ||
            rules.DanceMinEvents < 2 || rules.DanceMinTypingMs <= 0)
            throw new InvalidDataException("Значения в activity.json должны быть положительными.");
        return rules;
    }

    public string Choose(long now, long? key, long? wheel, uint idle, bool typingAllowed)
    {
        if (wheel is long lastWheel && now - lastWheel < ScrollHoldMs) return "look";
        if (typingAllowed && key is long lastKey && now - lastKey < TypingHoldMs) return "work";
        return idle >= SmokeAfterMs ? "smoke" : "idle";
    }

    public void CheckPriority()
    {
        const long at = 2000;
        if (Choose(at, at, at, 0, true) != "look" ||
            Choose(at, at, null, 0, true) != "work" ||
            Choose(at, at, null, 0, false) != "idle" ||
            Choose(at, at - TypingHoldMs, null, (uint)SmokeAfterMs, true) != "smoke")
            throw new InvalidDataException("События активности не переключают сцены правильно.");
    }
}
