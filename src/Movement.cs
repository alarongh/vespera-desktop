using System;
using System.IO;
using System.Windows;

namespace Vespera;

internal sealed class DragPlayback
{
    private Vector offset;
    private Point lastPointer;
    private Point directionOrigin;
    private long? lastMotion;
    private static readonly string[] Directions =
        ["step-up", "step-up-right", "step-right", "step-down-right",
         "step-down", "step-down-left", "step-left", "step-up-left"];
    private int? facing;
    public bool Active { get; private set; }

    public void Begin(Point pointer, Point windowOrigin)
    {
        offset = pointer - windowOrigin;
        lastPointer = directionOrigin = pointer;
        lastMotion = null;
        facing = null;
        Active = true;
    }

    public Point? Move(Point pointer, long now)
    {
        Vector change = pointer - lastPointer;
        if (change.LengthSquared < 1) return null;
        Vector directionChange = pointer - directionOrigin;
        if (directionChange.LengthSquared >= 25)
        {
            if (lastMotion is long previous && now - previous >= 150) facing = null;
            facing = DirectionIndex(directionChange, facing);
            directionOrigin = pointer;
        }
        lastPointer = pointer;
        lastMotion = now;
        return pointer - offset;
    }

    private static int DirectionIndex(Vector movement, int? current = null)
    {
        double degrees = (Math.Atan2(movement.X, -movement.Y) * 180 / Math.PI + 360) % 360;
        if (current is int direction && Math.Abs((degrees - direction * 45 + 540) % 360 - 180) <= 28)
            return direction;
        return (int)Math.Round(degrees / 45, MidpointRounding.AwayFromZero) % Directions.Length;
    }

    public static string DirectionScene(Vector movement) => Directions[DirectionIndex(movement)];

    public string SceneAt(long now) => lastMotion is long motion && now - motion < 150 && facing is int direction
        ? Directions[direction] : "idle";

    public void End() => Active = false;

    public static void Check()
    {
        var drag = new DragPlayback();
        drag.Begin(new Point(200, 200), new Point(170, 160));
        if (drag.Move(new Point(220, 207), 100) != new Point(190, 167) || drag.SceneAt(100) != "step-right" ||
            drag.Move(new Point(205, 219), 160) != new Point(175, 179) || drag.SceneAt(160) != "step-down-left" ||
            DirectionScene(new Vector(0, -10)) != "step-up" ||
            DirectionScene(new Vector(0, 10)) != "step-down" ||
            DirectionScene(new Vector(10, -10)) != "step-up-right" ||
            DirectionScene(new Vector(-10, -10)) != "step-up-left" ||
            DirectionScene(new Vector(10, 10)) != "step-down-right" ||
            DirectionScene(new Vector(-10, 0)) != "step-left" ||
            DirectionIndex(new Vector(10, 5), 2) != 2 ||
            DirectionIndex(new Vector(10, 8), 2) != 3 ||
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
