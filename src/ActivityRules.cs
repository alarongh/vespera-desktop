using System;
using System.IO;
using System.Text.Json;

namespace Vespera;

internal sealed record ActivityRules(int TypingHoldMs, int ScrollHoldMs, int SmokeAfterMs, int WindowWidth,
    int DanceMinEvents = 12, int DanceMinTypingMs = 1200, bool TypingInAllApps = true,
    int CursorLookHoldMs = 2700, double HeadAnchorX = 0.50, double HeadAnchorY = 0.15, int SwimsuitTypingHoldMs = 1120, int SwimsuitCursorLookHoldMs = 1150, double AnimationSpeed = 1.0)
{
    public static ActivityRules Load(string artwork)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var rules = JsonSerializer.Deserialize<ActivityRules>(File.ReadAllText(Path.Combine(artwork, "activity.json")), options)
            ?? throw new InvalidDataException("Не удалось прочитать artwork/activity.json.");
        if (rules.TypingHoldMs <= 0 || rules.ScrollHoldMs <= 0 || rules.SmokeAfterMs <= 0 || rules.WindowWidth <= 0 ||
            rules.DanceMinEvents < 2 || rules.DanceMinTypingMs <= 0 || rules.CursorLookHoldMs <= 0 || rules.SwimsuitTypingHoldMs <= 0 || rules.SwimsuitCursorLookHoldMs <= 0 ||
            (!double.IsFinite(rules.AnimationSpeed) || rules.AnimationSpeed < .6 || rules.AnimationSpeed > 1.6) ||
            rules.HeadAnchorX <= 0 || rules.HeadAnchorX >= 1 || rules.HeadAnchorY <= 0 || rules.HeadAnchorY >= 1)
            throw new InvalidDataException("Значения в activity.json должны быть положительными.");
        return rules;
    }

    public string Choose(long now, long? key, long? wheel, uint idle, bool typingAllowed, bool allowSmoking = true, bool preferNewestActivity = false)
    {
        bool writing = typingAllowed && key is long lastKey && now - lastKey < TypingHoldMs;
        if (wheel is long lastWheel && now - lastWheel < ScrollHoldMs &&
            (!preferNewestActivity || !writing || lastWheel >= key)) return "look";
        if (writing) return "work";
        return allowSmoking && idle >= SmokeAfterMs ? "smoke" : "idle";
    }

    public string? PointerScene(long elapsed, long idleAnimationMs)
    {
        if (elapsed < CursorLookHoldMs) return "look";
        return elapsed < CursorLookHoldMs + idleAnimationMs ? "idle" : null;
    }

    public void CheckPriority()
    {
        const long at = 2000;
        if (Choose(at, at, at, 0, true) != "look" ||
            Choose(at, at, null, 0, true) != "work" ||
            Choose(at, at, null, 0, false) != "idle" ||
            Choose(at, at - TypingHoldMs, null, (uint)SmokeAfterMs, true) != "smoke" ||
            Choose(at, null, null, uint.MaxValue, true, allowSmoking: false) != "idle" ||
            Choose(at, at, at - 1, 0, true, preferNewestActivity: true) != "work" ||
            PointerScene(CursorLookHoldMs - 1, 2000) != "look" ||
            PointerScene(CursorLookHoldMs, 2000) != "idle" ||
            PointerScene(CursorLookHoldMs + 2000, 2000) is not null)
            throw new InvalidDataException("События активности не переключают сцены правильно.");
    }
}
