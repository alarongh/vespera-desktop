using System;
using System.IO;

namespace Vespera;

[Flags]
internal enum ModifierKey
{
    Other = 0,
    LeftControl = 1, RightControl = 2, LeftAlt = 4, RightAlt = 8,
    Shift = 16, LeftWindows = 32, RightWindows = 64
}

internal sealed class VisibilityShortcut
{
    private const ModifierKey Control = ModifierKey.LeftControl | ModifierKey.RightControl;
    private const ModifierKey Blocking = ModifierKey.RightAlt | ModifierKey.Shift |
        ModifierKey.LeftWindows | ModifierKey.RightWindows;
    private ModifierKey held;
    private bool armed;
    public bool ControlAltHeld => ChordDown && (held & Blocking) == 0;

    private bool ChordDown => (held & Control) != 0 && (held & ModifierKey.LeftAlt) != 0;

    public bool Update(ModifierKey key, bool down)
    {
        bool wasDown = ChordDown;
        held = down ? held | key : held & ~key;
        if (down && (key == ModifierKey.Other || (key & Blocking) != 0))
            armed = false;
        if (ChordDown && !wasDown)
            armed = (held & Blocking) == 0;
        if (ChordDown || !wasDown) return false;
        bool completed = armed;
        armed = false;
        return completed;
    }

    public static void Check()
    {
        const ModifierKey ctrl = ModifierKey.LeftControl;
        const ModifierKey alt = ModifierKey.LeftAlt;
        var shortcut = new VisibilityShortcut();
        if (shortcut.Update(alt, true) || shortcut.Update(ctrl, true) || shortcut.Update(ctrl, true) ||
            !shortcut.Update(alt, false) || shortcut.Update(ctrl, false))
            throw new InvalidDataException("Alt+Ctrl должен срабатывать один раз при отпускании.");

        shortcut.Update(ctrl, true);
        shortcut.Update(alt, true);
        shortcut.Update(ModifierKey.Other, true);
        if (shortcut.Update(ctrl, false) || shortcut.Update(alt, false))
            throw new InvalidDataException("Сочетание с третьей клавишей не должно скрывать Весперу.");

        shortcut.Update(ctrl, true);
        shortcut.Update(ModifierKey.RightAlt, true);
        if (shortcut.Update(ctrl, false) || shortcut.Update(ModifierKey.RightAlt, false))
            throw new InvalidDataException("AltGr не должен скрывать Весперу.");

        shortcut.Update(ModifierKey.RightControl, true);
        shortcut.Update(alt, true);
        if (!shortcut.Update(ModifierKey.RightControl, false))
            throw new InvalidDataException("Сочетание должно работать в любом порядке.");
    }
}
