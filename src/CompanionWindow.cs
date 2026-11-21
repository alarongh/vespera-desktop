using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using SystemIcons = System.Drawing.SystemIcons;

namespace Vespera;

internal sealed class CompanionWindow : Window
{
    private readonly string[] demoSequence;
    private long DemoLength => demoSequence.Sum(scene => stageModel.Pack.Scenes[scene].Length) + 4000;
    private readonly CompanionModel stageModel;
    private readonly CompanionModel? poolModel;
    private CompanionModel model;
    private AnimationPack pack => model.Pack;
    private readonly CharacterProfile profile;
    private SpriteRenderer renderer => model.Renderer;
    private ActivityRules rules => model.Rules;
    private readonly Image picture = new() { Stretch = Stretch.Uniform };
    private readonly Forms.NotifyIcon tray;
    private readonly EnterCaption enterCaption;
    private readonly TextBlock captionText = new()
    {
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.LavenderBlush
    };
    private readonly Border caption = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(237, 32, 24, 34)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(146, 99, 122)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(9, 3, 9, 4),
        Margin = new Thickness(0, 5, 6, 0),
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Right,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };
    private readonly Forms.ToolStripMenuItem visibilityItem;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DragPlayback drag = new();
    private readonly TypingDance typingDance = new();
    private NativeInput? input;
    private long? lastKey;
    private long? lastWheel;
    private Point? pointerPosition;
    private Point? cursorMotionAnchor;
    private Point? stageLookPointer;
    private long? lastCursorMotion;
    private long nextBlink;
    private long blinkUntil;
    private readonly DanceRotation danceRotation;
    private PropPlayback propPlayback => model.Props;
    private StageRestPlayback? stageRest;
    private string? activeDance;
    private bool dancePending;
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
    private readonly bool hasSwimsuitRest;
    internal bool SupportsSwimsuitMode => hasSwimsuitRest;
    internal bool SwimsuitMode { get; private set; }
    internal event Action<bool>? SwimsuitModeChanged;
    internal bool HasFinishedStageEntry => !IsWardrobeActive && !SwimsuitMode;
    private bool UseDrawnStageGaze => !UsingPoolModel && !SwimsuitMode;
    private bool UsingPoolModel => poolModel is not null && ReferenceEquals(model, poolModel);
    internal bool HasFinishedSwimsuitEntry => IsWardrobeActive && current == "smoke" &&
        clock.ElapsedMilliseconds - started >= pack.Scenes["smoke"].IntroLength;
    private bool IsWardrobeActive => poolModel is not null ? UsingPoolModel : hasSwimsuitRest &&
        (SwimsuitMode || current == "smoke" || IsB2Action(current) || PropPlayback.IsRestOutro(current));
    private long hiddenAt;
    private bool swimsuitWhipPending;
    private long? swimsuitResumeAt;
    private static bool IsB2Action(string scene) => scene.StartsWith("b2-", StringComparison.Ordinal);

    public CompanionWindow(CharacterProfile profile, CompanionModel stageModel, CompanionModel? poolModel,
        bool startDemo, bool startInSwimsuit)
    {
        this.profile = profile;
        this.stageModel = stageModel;
        this.poolModel = poolModel;
        enterCaption = new EnterCaption(profile.Replies);
        stageRest = StageRestPlayback.From(stageModel.Pack);
        hasSwimsuitRest = poolModel is not null;
        danceRotation = new DanceRotation(stageModel.Pack);
        SwimsuitMode = hasSwimsuitRest && startInSwimsuit;
        model = SwimsuitMode && poolModel is not null ? poolModel : stageModel;
        demoSequence = hasSwimsuitRest
            ? stageRest is null
                ? ["idle", "work", "work-out", .. danceRotation.Available]
                : ["idle", "work", "work-out", StageRestPlayback.Smoke, StageRestPlayback.Outro, .. danceRotation.Available]
            : ["idle", "work", "work-out", .. danceRotation.Available, "smoke", "smoke-out"];
        matchAllApps = rules.TypingInAllApps;
        demo = startDemo && !SwimsuitMode;
        if (SwimsuitMode)
        {
            current = "smoke";
            started = clock.ElapsedMilliseconds - pack.Scenes[current].IntroLength;
        }
        Title = profile.Name;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        UseLayoutRounding = true;
        picture.SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        picture.Source = renderer.Surface;
        caption.Child = captionText;
        var layers = new Grid();
        layers.Children.Add(picture);
        layers.Children.Add(caption);
        Content = layers;
        Width = rules.WindowWidth;
        Height = Width * 1.08;
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - Height - 8;

        var menu = new Forms.ContextMenuStrip();
        Add(menu, "Автоматически", () => { demo = false; manual = null; ResetPerformance(); });
        var demonstration = new Forms.ToolStripMenuItem("Демонстрация");
        demonstration.Click += (_, _) =>
        {
            if (IsWardrobeActive) return;
            ResetPerformance();
            demoStarted = clock.ElapsedMilliseconds;
            demo = true;
            manual = null;
        };
        menu.Items.Add(demonstration);
        var preview = new Forms.ToolStripMenuItem("Показать сцену");
        foreach (var (key, label) in stageModel.Pack.Labels)
        {
            if (hasSwimsuitRest && (key == "smoke" || IsB2Action(key) || PropPlayback.IsRestOutro(key))) continue;
            Add(preview, label, () => Preview(key));
        }
        Add(preview, "Взгляд за курсором", () => Preview("look"));
        menu.Items.Add(preview);
        if (hasSwimsuitRest)
        {
            var outfit = new Forms.ToolStripLabel();
            menu.Items.Insert(0, outfit);
            Add(menu, "Сменить образ (Ctrl+Alt+B)", ToggleSwimsuitMode);
            menu.Opening += (_, _) =>
            {
                outfit.Text = SwimsuitMode ? "Купальник · Ctrl+Alt+B" : "Сценический костюм · Ctrl+Alt+B";
                preview.Enabled = demonstration.Enabled = !IsWardrobeActive && !SwimsuitMode;
            };
        }
        if (poolModel is not null)
        {
            AddModelSpeedMenu(menu, "Темп B1", stageModel);
            AddModelSpeedMenu(menu, "Темп B2", poolModel);
        }
        var allApps = new Forms.ToolStripMenuItem("Реагировать на печать во всех приложениях")
        {
            CheckOnClick = true,
            Checked = matchAllApps
        };
        allApps.CheckedChanged += (_, _) =>
        {
            matchAllApps = allApps.Checked;
            lastKey = null;
            ResetPerformance();
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
        visibilityItem = new Forms.ToolStripMenuItem($"Скрыть {profile.Name}  (Alt+Ctrl)");
        visibilityItem.Click += (_, _) => ToggleVisibility();
        menu.Items.Add(visibilityItem);
        Add(menu, "Закрыть компаньона", Close);

        tray = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = SwimsuitMode ? $"{profile.Name} · купальник" : profile.Name,
            ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => ShowCompanion();
        tray.Visible = true;
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => Tick();
        SourceInitialized += (_, _) =>
        {
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)
                ?? throw new InvalidOperationException("Не удалось открыть окно для событий ввода.");
            input = new NativeInput(source);
            input.VisibilityShortcutPressed += ToggleVisibility;
            input.EnterPressed += ReactToEnter;
            if (hasSwimsuitRest) input.RestShortcutPressed += ToggleSwimsuitMode;
            input.KeyboardActivity += () =>
            {
                long now = clock.ElapsedMilliseconds;
                if (!IsVisible || drag.Active || now < ignoreKeyboardUntil || NativeInput.AltHeld ||
                    (!matchAllApps && !input.IsCodexForeground())) return;
                lastKey = now;
                typingDance.Record(now, rules.TypingHoldMs);
                dancePending = false;
                activeDance = null;
            };
            input.WheelActivity += () =>
            {
                if (!IsVisible) return;
                lastWheel = clock.ElapsedMilliseconds;
                ResetPerformance();
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
            ResetPerformance();
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

    internal void ToggleVisibility()
    {
        if (!IsVisible)
        {
            ShowCompanion();
            return;
        }

        FinishDrag();
        enterCaption.Dismiss();
        caption.Visibility = Visibility.Collapsed;
        timer.Stop();
        hiddenAt = clock.ElapsedMilliseconds;
        Hide();
        visibilityItem.Text = $"Показать {profile.Name}  (Alt+Ctrl)";
        tray.Text = $"{profile.Name} · скрыта";
    }

    internal void ShowCompanion()
    {
        if (IsVisible) return;
        lastKey = lastWheel = lastCursorMotion = null;
        cursorMotionAnchor = null;
        stageLookPointer = null;
        long now = clock.ElapsedMilliseconds;
        ignoreKeyboardUntil = now + 250;
        ResetPerformance();
        if (hiddenAt != 0 && IsWardrobeActive)
        {
            started += now - hiddenAt;
            tray.Text = SwimsuitMode ? $"{profile.Name} · купальник" : $"{profile.Name} · возвращается";
        }
        else
            current = "";
        hiddenAt = 0;
        Show();
        visibilityItem.Text = $"Скрыть {profile.Name}  (Alt+Ctrl)";
        Tick();
        timer.Start();
    }

    private void ReactToEnter()
    {
        if (!IsVisible || drag.Active || NativeInput.AltHeld ||
            (!matchAllApps && input?.IsCodexForeground() != true)) return;
        enterCaption.Show(clock.ElapsedMilliseconds);
        captionText.Text = enterCaption.Text;
        caption.Opacity = 1;
        caption.Visibility = Visibility.Visible;
    }

    internal void ToggleSwimsuitMode()
    {
        if (!hasSwimsuitRest) return;
        SwimsuitMode = !SwimsuitMode;
        swimsuitWhipPending = false;
        swimsuitResumeAt = null;
        SwimsuitModeChanged?.Invoke(SwimsuitMode);
        manual = null;
        demo = false;
        ResetPerformance();
        ShowCompanion();
    }

    private void Preview(string scene)
    {
        if (IsWardrobeActive) return;
        ResetPerformance();
        demo = false;
        manual = scene;
        long duration = scene == "look" ? 6500 : pack.Scenes[scene].Length;
        if (PropPlayback.OutroFor(scene) is string outro) duration += pack.Scenes[outro].Length;
        else if (scene == "idle" || scene.StartsWith("step-", StringComparison.Ordinal))
            duration = Math.Max(duration, 6500);
        manualUntil = clock.ElapsedMilliseconds + duration;
        current = "";
        ShowCompanion();
    }

    private void ResetPerformance(bool resetRest = true)
    {
        typingDance.Reset();
        if (resetRest) stageRest?.Reset();
        dancePending = false;
        activeDance = null;
    }

    private void AddModelSpeedMenu(Forms.ContextMenuStrip menu, string title, CompanionModel choice)
    {
        var section = new Forms.ToolStripMenuItem(title);
        var speeds = new[] { 0.75, 0.90, 1.0, 1.10, 1.25, 1.50 };
        foreach (double multiplier in speeds)
        {
            var item = new Forms.ToolStripMenuItem($"{multiplier:0.##}×");
            item.Click += (_, _) => ChangeSpeed(choice, multiplier);
            section.DropDownItems.Add(item);
        }
        section.DropDownOpening += (_, _) =>
        {
            for (int i = 0; i < speeds.Length; i++)
                ((Forms.ToolStripMenuItem)section.DropDownItems[i]).Checked = Math.Abs(choice.Speed - speeds[i]) < .001;
        };
        menu.Items.Add(section);
    }

    private void ChangeSpeed(CompanionModel choice, double speed)
    {
        double prior = choice.Speed;
        if (Math.Abs(prior - speed) < .001) return;
        try
        {
            choice.SetSpeed(speed);
            long now = clock.ElapsedMilliseconds;
            if (ReferenceEquals(choice, model))
            {
                started = now - (long)Math.Round((now - started) * prior / speed);
                if (manual is not null)
                    manualUntil = now + (long)Math.Round((manualUntil - now) * prior / speed);
                if (demo)
                    demoStarted = now - (long)Math.Round((now - demoStarted) * prior / speed);
                if (swimsuitResumeAt is long resume)
                    swimsuitResumeAt = (long)Math.Round(resume * prior / speed);
            }
            if (ReferenceEquals(choice, stageModel))
                stageRest = StageRestPlayback.From(stageModel.Pack);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Темп Весперы", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string UseModel(CompanionModel next, string scene)
    {
        model = next;
        stageLookPointer = null;
        current = "";
        return scene;
    }

    private string? SelectModelHandoff(long now)
    {
        if (poolModel is null) return null;

        long elapsed = now - started;
        if (UsingPoolModel)
        {
            bool dressed = (current == "b2-dress" || PropPlayback.IsRestOutro(current)) &&
                elapsed >= pack.Scenes[current].Length;
            return !SwimsuitMode && dressed ? UseModel(stageModel, "idle") : null;
        }

        if (!SwimsuitMode) return null;
        if (current == "idle")
            return pack.Scenes["idle"].At(elapsed) == pack.Scenes["idle"].At(0)
                ? UseModel(poolModel, "smoke") : "idle";
        if (stageRest is not null && current is StageRestPlayback.Smoke or StageRestPlayback.Outro)
            return stageRest.Select(current, elapsed, now, "idle", quiet: false, scrolling: true);
        if (current is "work" or "smoke" || PropPlayback.IsOutro(current))
            return propPlayback.Select(current, started, now, "idle");
        if (danceRotation.Available.Contains(current) && elapsed < pack.Scenes[current].Length)
            return current;
        return "idle";
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
                if (!IsWardrobeActive)
                {
                    stageRest?.Reset();
                    return drag.SceneAt(now);
                }
            }
        }
        if (SelectModelHandoff(now) is string handoff) return handoff;
        if (poolModel is null && SwimsuitMode && stageRest is not null && current is StageRestPlayback.Smoke or StageRestPlayback.Outro)
        {
            long restElapsed = now - started;
            if (current == StageRestPlayback.Outro && restElapsed >= pack.Scenes[current].Length)
                return "smoke";
            return stageRest.Select(current, restElapsed, now, "smoke", quiet: false, scrolling: false);
        }
        if (IsWardrobeActive && pack.Scenes.ContainsKey("b2-work"))
            return SelectSwimsuitScene(now);
        if (SwimsuitMode) return propPlayback.Select(current, started, now, "smoke");
        if (manual is not null && now < manualUntil)
        {
            if (PropPlayback.OutroFor(manual) is string outro && now >= manualUntil - pack.Scenes[outro].Length)
                return outro;
            return manual;
        }
        manual = null;
        if (demo) return DemoScene(now - demoStarted);

        long elapsed = now - started;
        if (PropPlayback.IsOutro(current) && elapsed >= pack.Scenes[current].Length)
        {
            // Preserve a glance requested during a closing animation.
            if (lastCursorMotion >= started - 40) lastCursorMotion = now;
            if (lastWheel >= started - 40) lastWheel = now;
        }
        string wanted = SelectAutomatic(now);
        if (stageRest is not null)
        {
            bool quiet = input?.IdleMilliseconds >= rules.SmokeAfterMs;
            bool scrolling = lastWheel is long wheel && now - wheel < rules.ScrollHoldMs;
            if (UseDrawnStageGaze && !scrolling && wanted == "look" && (current is StageRestPlayback.Smoke or StageRestPlayback.Outro))
                wanted = "idle";
            wanted = stageRest.Select(current, elapsed, now, wanted, quiet, scrolling);
        }
        return propPlayback.Select(current, started, now, wanted);
    }

    private string SelectSwimsuitScene(long now)
    {
        bool typing = !drag.Active && lastKey is long key && now - key < rules.SwimsuitTypingHoldMs &&
            (matchAllApps || input?.IsCodexForeground() == true);
        long elapsed = now - started;
        if (UsingPoolModel && current == "smoke" && elapsed < pack.Scenes[current].IntroLength)
            return current;
        if (current == "smoke" && SwimsuitMode)
        {
            Scene rest = pack.Scenes[current];
            if (elapsed < rest.IntroLength) return current;
            if (rest.IsFinishingGesture(elapsed))
            {
                swimsuitWhipPending |= typing;
                return current;
            }
            if (!typing && !swimsuitWhipPending) return current;
            string response = rest.LatestExitRoute(elapsed) switch
            {
                "rest-out-standing" => propPlayback.NextB2Work(),
                "rest-out-cross" => "b2-rise-cross",
                "rest-out-knee" => "b2-rise-knee",
                "rest-out-phone" => "b2-rise-phone",
                _ => "b2-rise-side"
            };
            swimsuitResumeAt = rest.NextLoopAnchor(elapsed + 1, "smoke-out");
            swimsuitWhipPending = !PropPlayback.IsB2Work(response);
            return response;
        }
        if (current.StartsWith("b2-rise-", StringComparison.Ordinal))
        {
            if (elapsed < pack.Scenes[current].Length) return current;
            bool playWhip = typing || swimsuitWhipPending;
            swimsuitWhipPending = false;
            return !SwimsuitMode ? "b2-dress" : playWhip ? propPlayback.NextB2Work() : "b2-settle";
        }
        if (PropPlayback.IsB2Work(current))
        {
            if (UsingPoolModel && SwimsuitMode && typing && elapsed >= pack.Scenes[current].Length)
                return propPlayback.NextB2Work(current);
            return propPlayback.Select(current, started, now,
                !SwimsuitMode ? "b2-dress" : typing ? current : "b2-settle");
        }
        if (PropPlayback.IsB2WorkOutro(current))
        {
            if (elapsed < pack.Scenes[current].Length) return current;
            return !SwimsuitMode ? "b2-dress" : typing ? propPlayback.NextB2Work() : "b2-settle";
        }
        if (current == "b2-settle")
        {
            if (elapsed < pack.Scenes[current].Length) return current;
            if (!SwimsuitMode) return "smoke-out";
            swimsuitWhipPending = typing;
            return typing ? "b2-rise-side" : "smoke";
        }
        if (current == "b2-dress" && elapsed < pack.Scenes[current].Length) return current;
        return propPlayback.Select(current, started, now, SwimsuitMode ? "smoke" : UsingPoolModel ? "idle" : SelectAutomatic(now));
    }

    private string SelectAutomatic(long now)
    {
        bool typingAllowed = matchAllApps || input?.IsCodexForeground() == true;
        if (!typingAllowed)
        {
            ResetPerformance(resetRest: false);
        }
        string automatic = rules.Choose(now, lastKey, lastWheel, input?.IdleMilliseconds ?? 0, typingAllowed,
            allowSmoking: !hasSwimsuitRest, preferNewestActivity: hasSwimsuitRest);
        if (automatic == "work")
        {
            if (current == "idle" && pack.Scenes["idle"].IsFinishingGesture(now - started))
                return "idle";
            return "work";
        }
        if (automatic == "look") return pack.InPlaceStageGaze && !UseDrawnStageGaze ? "idle" : automatic;

        if (typingAllowed && typingDance.TryStart(now, rules)) dancePending = true;
        if (activeDance is not null && current == activeDance &&
            now - started < pack.Scenes[activeDance].Length) return activeDance;
        activeDance = null;
        if (dancePending) return danceRotation.Next;
        string? pointerScene = lastCursorMotion is long movement
            ? rules.PointerScene(now - movement, pack.Scenes["idle"].Length) : null;
        return pack.InPlaceStageGaze && !UseDrawnStageGaze && pointerScene == "look" ? "idle" : pointerScene ?? automatic;
    }

    private void TrackPointer(long now)
    {
        if (!NativeInput.TryGetPointer(out Point pointer)) return;
        pointerPosition = pointer;
        if (UseDrawnStageGaze)
            stageLookPointer = stageLookPointer is Point prior ? prior + (pointer - prior) * .34 : pointer;
        if (cursorMotionAnchor is not Point anchor)
        {
            cursorMotionAnchor = pointer;
            return;
        }
        if ((pointer - anchor).LengthSquared < (hasSwimsuitRest ? 1 : 4)) return;
        cursorMotionAnchor = pointer;
        if (!drag.Active) lastCursorMotion = now;
    }

    private void Tick()
    {
        long now = clock.ElapsedMilliseconds;
        RefreshClickThrough();
        TrackPointer(now);
        string scene = SelectScene(now);
        if (scene != current)
        {
            string previous = current;
            if (drag.Active && current.StartsWith("step-", StringComparison.Ordinal) &&
                scene.StartsWith("step-", StringComparison.Ordinal))
            {
                long priorLength = pack.Scenes[current].Length;
                double stridePhase = (double)((now - started) % priorLength) / priorLength;
                started = now - (long)(stridePhase * pack.Scenes[scene].Length);
            }
            else if (PropPlayback.IsB2Work(previous) && PropPlayback.IsB2Work(scene))
            {
                long overrun = Math.Max(0, now - started - pack.Scenes[previous].Length);
                started = now - pack.Scenes[scene].IntroLength - overrun;
            }
            else
            {
                started = now;
                if (previous == "b2-settle" && scene == "smoke")
                {
                    started -= swimsuitResumeAt ?? pack.Scenes[scene].IntroLength;
                    swimsuitResumeAt = null;
                }
            }
            current = scene;
            if (!demo && manual is null && dancePending && scene == danceRotation.Next)
            {
                activeDance = danceRotation.Take();
                dancePending = false;
            }
            tray.Text = scene switch
            {
                "work" or "work-out" => $"{profile.Name} · плеть",
                _ when PropPlayback.IsB2Work(scene) || PropPlayback.IsB2WorkOutro(scene) => $"{profile.Name} · плеть",
                "smoke" or "b2-settle" when hasSwimsuitRest => $"{profile.Name} · отдых",
                _ when hasSwimsuitRest && (PropPlayback.IsRestOutro(scene) || scene == "b2-dress") => $"{profile.Name} · возвращается",
                "smoke" or "smoke-out" or StageRestPlayback.Smoke or StageRestPlayback.Outro => $"{profile.Name} · перекур",
                "look" => $"{profile.Name} · взгляд за курсором",
                _ when scene.StartsWith("idol-dance", StringComparison.Ordinal) => $"{profile.Name} · танец",
                "step-left" or "step-right" or "step-up" or "step-down" or
                    "step-up-left" or "step-up-right" or "step-down-left" or "step-down-right" => $"{profile.Name} · бежит",
                _ => profile.Name
            };
        }
        bool stageLook = scene == "look" && pack.InPlaceStageGaze && !UseDrawnStageGaze;
        string frame = stageLook ? pack.Scenes["idle"].At(0) :
            scene == "look" ? Look(now - started) : pack.Scenes[scene].At(now - started);
        Point? gazePoint = null;
        bool inPlaceGaze = SwimsuitMode
            ? scene == "smoke" || IsB2Action(scene)
            : pack.InPlaceStageGaze && (scene == "idle" || stageLook || scene == StageRestPlayback.Smoke);
        int gazeHoldMs = SwimsuitMode ? rules.SwimsuitCursorLookHoldMs : rules.CursorLookHoldMs;
        if (stageLook && demo)
        {
            double angle = (now - started) * Math.Tau / 4000;
            gazePoint = new Point(renderer.Surface.PixelWidth * .5 + 200 * Math.Sin(angle),
                renderer.Surface.PixelHeight * rules.HeadAnchorY - 200 * Math.Cos(angle));
        }
        else if (inPlaceGaze && !drag.Active && IsLoaded && pointerPosition is Point pointer &&
            (stageLook || lastCursorMotion is long movement && now - movement < gazeHoldMs ||
             lastWheel is long wheel && now - wheel < rules.ScrollHoldMs))
        {
            Point local = PointFromScreen(pointer);
            if (ActualWidth > 0 && ActualHeight > 0)
                gazePoint = new Point(local.X / ActualWidth * renderer.Surface.PixelWidth,
                    local.Y / ActualHeight * renderer.Surface.PixelHeight);
        }
        renderer.Present(frame, gazePoint);
        if (!ReferenceEquals(picture.Source, renderer.Surface)) picture.Source = renderer.Surface;
        if (caption.Visibility == Visibility.Visible)
        {
            double opacity = enterCaption.Opacity(now);
            if (opacity == 0) caption.Visibility = Visibility.Collapsed;
            else if (caption.Opacity != opacity) caption.Opacity = opacity;
        }
    }

    private string Look(long elapsed)
    {
        long now = clock.ElapsedMilliseconds;
        if (nextBlink == 0) nextBlink = now + Random.Shared.Next(1500, 3000);
        if (now >= nextBlink)
        {
            blinkUntil = now + 120;
            nextBlink = now + Random.Shared.Next(3000, 5600);
        }
        bool blink = now < blinkUntil;
        if (demo)
        {
            double angle = elapsed / 4000.0 * 2 * Math.PI;
            return pack.Look(Math.Sin(angle), -Math.Cos(angle), blink);
        }
        if (!IsLoaded || pointerPosition is not Point pointer) return pack.Scenes["idle"].At(0);
        Point center = PointToScreen(new Point(ActualWidth * rules.HeadAnchorX, ActualHeight * rules.HeadAnchorY));
        Point gaze = UseDrawnStageGaze ? stageLookPointer ?? pointer : pointer;
        double dx = gaze.X - center.X;
        double dy = gaze.Y - center.Y;
        return dx * dx + dy * dy < 36 ? pack.Scenes["idle"].At(0) : pack.Look(dx, dy, blink);
    }

    private string DemoScene(long elapsed)
    {
        elapsed %= DemoLength;
        foreach (string scene in demoSequence)
        {
            long duration = pack.Scenes[scene].Length;
            if (elapsed < duration) return scene;
            elapsed -= duration;
        }
        return "look";
    }
}
