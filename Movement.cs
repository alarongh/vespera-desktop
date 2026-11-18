using System;
using System.IO;
using System.Windows;

namespace Vespera;

internal sealed class DragPlayback
{
    private Vector offset;
    private Point lastPointer;
    private long? lastMotion;
    private string direction = "step-right";
    public bool Active { get; private set; }

    public void Begin(Point pointer, Point windowOrigin)
    {
        offset = pointer - windowOrigin;
        lastPointer = pointer;
        lastMotion = null;
        Active = true;
    }

    public Point? Move(Point pointer, long now)
    {
        Vector change = pointer - lastPointer;
        if (change.LengthSquared < 1) return null;
        if (Math.Abs(change.X) >= 2) direction = change.X < 0 ? "step-left" : "step-right";
        lastPointer = pointer;
        lastMotion = now;
        return pointer - offset;
    }

    public string SceneAt(long now) => lastMotion is long motion && now - motion < 150 ? direction : "idle";

    public void End() => Active = false;

    public static void Check()
    {
        var drag = new DragPlayback();
        drag.Begin(new Point(200, 200), new Point(170, 160));
        if (drag.Move(new Point(220, 210), 100) != new Point(190, 170) || drag.SceneAt(100) != "step-right" ||
            drag.Move(new Point(205, 219), 160) != new Point(175, 179) || drag.SceneAt(160) != "step-left" ||
            drag.SceneAt(310) != "idle")
            throw new InvalidDataException("Направление бега при перетаскивании выбирается неверно.");
        drag.End();
    }
}

internal sealed class TypingDance
{
    private long? first;
    private long last;
    private int count;

    public void Record(long now, int pause)
    {
        if (first is null || now - last >= pause)
        {
            first = now;
            count = 0;
        }
        last = now;
        count++;
    }

    public bool TryStart(long now, ActivityRules rules)
    {
        if (first is not long begin || now - last < rules.TypingHoldMs) return false;
        bool ready = count >= rules.DanceMinEvents && last - begin >= rules.DanceMinTypingMs &&
            now - last < rules.TypingHoldMs + 1000;
        Reset();
        return ready;
    }

    public void Reset() => first = null;

    public static void Check(ActivityRules rules)
    {
        var dance = new TypingDance();
        long gap = Math.Max(1, Math.Min(rules.TypingHoldMs / 2, 150));
        int count = Math.Max(rules.DanceMinEvents, (int)Math.Ceiling(rules.DanceMinTypingMs / (double)gap) + 1);
        for (int i = 0; i < count; i++) dance.Record(i * gap, rules.TypingHoldMs);
        long end = (count - 1) * gap + rules.TypingHoldMs;
        if (dance.TryStart(end - 1, rules) || !dance.TryStart(end, rules) || dance.TryStart(end + 1, rules))
            throw new InvalidDataException("Танец должен запускаться один раз после паузы в печати.");
    }
}
