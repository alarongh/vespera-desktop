using System;
using System.IO;

namespace Vespera;

internal sealed class EnterKey
{
    private int held;

    public bool Update(bool keypad, bool down)
    {
        int mask = keypad ? 2 : 1;
        bool wasHeld = (held & mask) != 0;
        held = down ? held | mask : held & ~mask;
        return down && !wasHeld;
    }

    public static void Check()
    {
        var key = new EnterKey();
        if (!key.Update(false, true) || key.Update(false, true) ||
            !key.Update(true, true) || key.Update(true, false) ||
            key.Update(false, false) || !key.Update(false, true))
            throw new InvalidDataException("Enter должен срабатывать один раз за нажатие.");
    }
}

internal sealed class EnterCaption
{
    private readonly string[] replies;
    private readonly Func<int> pick;
    private long endsAt;
    public string Text { get; private set; } = "";

    public EnterCaption(string[] replies) : this(replies, () => Random.Shared.Next(replies.Length)) { }

    private EnterCaption(string[] replies, Func<int> pick)
    {
        if (replies.Length == 0) throw new ArgumentException("Нужна хотя бы одна реплика.", nameof(replies));
        this.replies = replies;
        this.pick = pick;
    }

    public void Show(long now)
    {
        Text = replies[pick()];
        endsAt = now + 1450;
    }

    public double Opacity(long now) => Math.Clamp((endsAt - now) / 210.0, 0, 1);
    public void Dismiss() => endsAt = 0;

    public static void Check()
    {
        int[] choices = [0, 0, 1];
        int index = 0;
        var caption = new EnterCaption(["Ладно, зачёт.", "Ещё раз. Я смотрю."], () => choices[index++]);
        caption.Show(10);
        if (caption.Text != "Ладно, зачёт." || caption.Opacity(10) != 1 ||
            caption.Opacity(1400) <= 0 || caption.Opacity(1500) != 0)
            throw new InvalidDataException("Первая реплика Enter не работает.");
        caption.Show(1600);
        if (caption.Text != "Ладно, зачёт.") throw new InvalidDataException("Случайная реплика может повторяться.");
        caption.Show(2000);
        if (caption.Text != "Ещё раз. Я смотрю.") throw new InvalidDataException("Вторая реплика Enter не работает.");
        caption.Dismiss();
        if (caption.Opacity(2001) != 0) throw new InvalidDataException("Реплика должна скрываться вместе с Весперой.");
    }
}
