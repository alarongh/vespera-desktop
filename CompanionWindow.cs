using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using SystemIcons = System.Drawing.SystemIcons;

namespace Vespera;

internal sealed class CompanionWindow : Window
{
    private readonly AnimationPack pack;
    private readonly ActivityRules rules;
    private readonly Image picture = new() { Stretch = Stretch.Uniform };
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DragPlayback drag = new();
    private readonly TypingDance typingDance = new();
    private NativeInput? input;
    private long? lastKey;
    private long? lastWheel;
    private long danceUntil;
    private long ignoreKeyboardUntil;
    private bool moveUnlocked;
    private bool clickThrough = true;
    private bool matchAllApps;
    private bool demo;
    private string current = "";
    private long started;
    private long demoStarted;
    private string? manual;
    private long manualUntil;

    public CompanionWindow(AnimationPack pack, ActivityRules rules, bool startDemo)
    {
        this.pack = pack;
        this.rules = rules;
        matchAllApps = rules.TypingInAllApps;
        demo = startDemo;
        Title = "Веспера";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        Content = picture;
        Width = rules.WindowWidth;
        Height = Width * 1.08;
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - Height - 8;

        var menu = new Forms.ContextMenuStrip();
        Add(menu, "Автоматически", () => { demo = false; manual = null; });
        Add(menu, "Демонстрация", () => { demoStarted = clock.ElapsedMilliseconds; demo = true; manual = null; });
        var preview = new Forms.ToolStripMenuItem("Показать сцену");
        foreach (var (key, label) in pack.Labels)
            Add(preview, label, () => Preview(key));
        Add(preview, "Взгляд за курсором", () => Preview("look"));
        menu.Items.Add(preview);
        var allApps = new Forms.ToolStripMenuItem("Реагировать на печать во всех приложениях")
        {
            CheckOnClick = true,
            Checked = matchAllApps
        };
        allApps.CheckedChanged += (_, _) =>
        {
            matchAllApps = allApps.Checked;
            lastKey = null;
            typingDance.Reset();
            danceUntil = 0;
        };
        menu.Items.Add(allApps);
        var move = new Forms.ToolStripMenuItem("Перетаскивать без Alt") { CheckOnClick = true };
        move.CheckedChanged += (_, _) => { moveUnlocked = move.Checked; RefreshClickThrough(); };
        menu.Items.Add(move);
        var sizes = new Forms.ToolStripMenuItem("Размер");
        foreach (int size in new[] { 200, 320, 450 })
            Add(sizes, $"{size}", () => ResizeSprite(size));
        menu.Items.Add(sizes);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(menu, "Закрыть Весперу", Close);

        tray = new Forms.NotifyIcon { Icon = SystemIcons.Application, Text = "Веспера", ContextMenuStrip = menu };
        tray.Visible = true;
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => Tick();
        SourceInitialized += (_, _) =>
        {
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)
                ?? throw new InvalidOperationException("Не удалось открыть окно для событий ввода.");
            input = new NativeInput(source);
            input.KeyboardActivity += () =>
            {
                long now = clock.ElapsedMilliseconds;
                if (drag.Active || now < ignoreKeyboardUntil || NativeInput.AltHeld ||
                    (!matchAllApps && !input.IsCodexForeground())) return;
                lastKey = now;
                typingDance.Record(now, rules.TypingHoldMs);
                danceUntil = 0;
            };
            input.WheelActivity += () =>
            {
                lastWheel = clock.ElapsedMilliseconds;
                typingDance.Reset();
                danceUntil = 0;
            };
            input.SetClickThrough(true);
            RefreshClickThrough();
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if ((!moveUnlocked && !NativeInput.AltHeld) || drag.Active ||
                !NativeInput.TryGetPointer(out Point pointer) || !CaptureMouse()) return;
            drag.Begin(pointer, PointToScreen(new Point(0, 0)));
            lastKey = null;
            lastWheel = null;
            typingDance.Reset();
            danceUntil = 0;
            e.Handled = true;
        };
        MouseLeftButtonUp += (_, _) => FinishDrag();
        LostMouseCapture += (_, _) => FinishDrag();
        Loaded += (_, _) => { Tick(); timer.Start(); };
        Closed += (_, _) =>
        {
            timer.Stop();
            input?.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
        };
    }

    private static void Add(Forms.ContextMenuStrip menu, string caption, Action action)
        => menu.Items.Add(caption, null, (_, _) => action());

    private static void Add(Forms.ToolStripMenuItem menu, string caption, Action action)
        => menu.DropDownItems.Add(caption, null, (_, _) => action());

    private void Preview(string scene)
    {
        demo = false;
        manual = scene;
        manualUntil = clock.ElapsedMilliseconds + (scene == "idol-dance" ? pack.Scenes[scene].Length : 6500);
        current = "";
    }

    private void ResizeSprite(int width)
    {
        Rect area = SystemParameters.WorkArea;
        Width = width;
        Height = width * 1.08;
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private void RefreshClickThrough()
    {
        bool transparent = !(moveUnlocked || drag.Active || NativeInput.AltHeld);
        if (transparent == clickThrough) return;
        clickThrough = transparent;
        input?.SetClickThrough(transparent);
    }

    private void FinishDrag()
    {
        if (!drag.Active) return;
        drag.End();
        ignoreKeyboardUntil = clock.ElapsedMilliseconds + 350;
        if (IsMouseCaptured) ReleaseMouseCapture();
        RefreshClickThrough();
    }

    private string SelectScene(long now)
    {
        if (drag.Active)
        {
            if (Mouse.LeftButton == MouseButtonState.Released) FinishDrag();
            else
            {
                if (NativeInput.TryGetPointer(out Point pointer) && drag.Move(pointer, now) is Point origin)
                    input?.MoveWindow(origin);
                return drag.SceneAt(now);
            }
        }
        if (manual is not null && now < manualUntil) return manual;
        manual = null;
        if (demo) return DemoScene((now - demoStarted) % 19000);
        bool typingAllowed = matchAllApps || input?.IsCodexForeground() == true;
        if (!typingAllowed)
        {
            typingDance.Reset();
            danceUntil = 0;
        }
        string automatic = rules.Choose(now, lastKey, lastWheel, input?.IdleMilliseconds ?? 0, typingAllowed);
        if (automatic is "work" or "look") return automatic;
        if (typingAllowed && typingDance.TryStart(now, rules))
            danceUntil = now + pack.Scenes["idol-dance"].Length;
        return now < danceUntil ? "idol-dance" : automatic;
    }

    private void Tick()
    {
        long now = clock.ElapsedMilliseconds;
        RefreshClickThrough();
        string scene = SelectScene(now);
        if (scene != current)
        {
            current = scene;
            started = now;
            tray.Text = scene switch
            {
                "work" => "Веспера · плеть",
                "smoke" => "Веспера · перекур",
                "look" => "Веспера · взгляд за курсором",
                "idol-dance" => "Веспера · танец",
                "step-left" or "step-right" => "Веспера · бежит",
                _ => "Веспера"
            };
        }
        BitmapSource image = scene == "look" ? Look(now - started) : pack.Scenes[scene].At(now - started);
        if (!ReferenceEquals(image, picture.Source)) picture.Source = image;
    }

    private BitmapSource Look(long elapsed)
    {
        if (demo)
        {
            double angle = elapsed / 4000.0 * 2 * Math.PI;
            return pack.Look(Math.Sin(angle), -Math.Cos(angle));
        }
        if (!IsLoaded || !NativeInput.TryGetPointer(out System.Windows.Point pointer)) return pack.Scenes["idle"].At(0);
        System.Windows.Point center = PointToScreen(new System.Windows.Point(ActualWidth / 2, ActualHeight / 2));
        double dx = pointer.X - center.X;
        double dy = pointer.Y - center.Y;
        return dx * dx + dy * dy < 36 ? pack.Scenes["idle"].At(0) : pack.Look(dx, dy);
    }

    private static string DemoScene(long milliseconds) => milliseconds switch
    {
        < 1800 => "idle",
        < 4700 => "work",
        < 9300 => "idol-dance",
        < 15000 => "smoke",
        _ => "look"
    };
}
