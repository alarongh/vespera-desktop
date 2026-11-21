using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Vespera;

internal sealed record CharacterProfile(string Name, string Artwork, string SwimsuitArtwork, string[] Replies);

internal static class Program
{
    private static readonly string Settings = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VesperaDesktop");
    private static readonly string OutfitSetting = Path.Combine(Settings, "outfit.txt");

    [STAThread]
    private static int Main(string[] args)
    {
        bool check = args.Contains("--check");
        bool build = args.Contains("--build-sprites");
        bool verify = args.Contains("--verify-sprites");
        bool smokeTest = args.Contains("--smoke-test");
        bool tools = check || build || verify;

        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Vespera.Public.Show");
        using var instance = new Mutex(true, smokeTest ? @"Local\Vespera.Public.Smoke" : @"Local\Vespera.Public.App",
            out bool firstInstance);
        if (!firstInstance && !tools)
        {
            if (!smokeTest) showRequest.Set();
            return smokeTest ? 1 : 0;
        }

        try
        {
            string assets = Path.Combine(AppContext.BaseDirectory, "assets");
            var profile = new CharacterProfile("Веспера", Path.Combine(assets, "B1"), Path.Combine(assets, "B2"),
                ["Не сбивайся с ритмом.", "М-м. Уже лучше.", "Пф. Могло быть хуже.",
                 "Ещё раз. Я смотрю.", "Ладно, зачёт.", "Только без импровизации."]);

            if (tools)
            {
                var b1 = VerifyModel(profile.Artwork, false, check, build, verify);
                var b2 = VerifyModel(profile.SwimsuitArtwork, true, check, build, verify);
                if (check) CompanionModel.CheckHandoffs(profile.Artwork, b1, profile.SwimsuitArtwork, b2);
                return 0;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            using var stage = new CompanionModel(profile.Artwork, speedPath: smokeTest ? null : Path.Combine(Settings, "speed-b1.txt"));
            using var pool = new CompanionModel(profile.SwimsuitArtwork, swimsuit: true,
                speedPath: smokeTest ? null : Path.Combine(Settings, "speed-b2.txt"));
            if (stage.Renderer.Surface.PixelWidth != pool.Renderer.Surface.PixelWidth ||
                stage.Renderer.Surface.PixelHeight != pool.Renderer.Surface.PixelHeight)
                throw new InvalidDataException("У обоих образов должен быть одинаковый холст.");

            var window = new CompanionWindow(profile, stage, pool, args.Contains("--demo"), !smokeTest && ReadOutfit());
            if (smokeTest)
                window.Opacity = 0;
            else
                window.SwimsuitModeChanged += WriteOutfit;
            app.MainWindow = window;
            window.Show();

            var registration = ThreadPool.RegisterWaitForSingleObject(showRequest, (_, _) =>
                app.Dispatcher.BeginInvoke(new Action(window.ShowCompanion)), null, Timeout.Infinite, false);
            try
            {
                if (smokeTest) StartSmokeTest(app, window);
                return app.Run();
            }
            finally { registration.Unregister(null); }
        }
        catch (Exception error)
        {
            if (tools || smokeTest) Console.Error.WriteLine(error);
            else MessageBox.Show(error.Message, "Веспера", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static bool ReadOutfit()
    {
        try { return File.ReadAllText(OutfitSetting).Trim() == "B2"; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void WriteOutfit(bool swimsuit)
    {
        try
        {
            Directory.CreateDirectory(Settings);
            string temporary = OutfitSetting + ".tmp";
            File.WriteAllText(temporary, swimsuit ? "B2" : "B1");
            File.Move(temporary, OutfitSetting, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static AnimationPack VerifyModel(string root, bool swimsuit, bool check, bool build, bool verify)
    {
        ActivityRules rules = ActivityRules.Load(root);
        AnimationPack pack = AnimationPack.Load(root, rules.AnimationSpeed, swimsuit);
        if (build) SpriteArchive.Build(root, pack.Files);
        if (verify) SpriteArchive.Verify(root, pack.Files);
        if (!check) return pack;

        pack.CheckPlayback(swimsuit);
        rules.CheckPriority();
        PropPlayback.Check(pack, swimsuit);
        if (!swimsuit)
        {
            DragPlayback.Check();
            TypingDance.Check(rules);
            StageRestPlayback.Check(pack);
            DanceRotation.Check(pack);
            VisibilityShortcut.Check();
            EnterKey.Check();
            EnterCaption.Check();
        }
        Console.WriteLine($"{(swimsuit ? "B2" : "B1")}: {pack.Files.Count} кадров, {pack.Scenes.Count} сцен.");
        return pack;
    }

    private static void StartSmokeTest(Application app, CompanionWindow window)
    {
        var deadline = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        int step = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                if (!window.IsVisible || deadline.ElapsedMilliseconds > 15000)
                    throw new InvalidOperationException($"Проверка окна или переодевания не завершилась (шаг {step}).");

                if (step == 0)
                {
                    window.ToggleVisibility();
                    if (window.IsVisible) throw new InvalidOperationException("Окно осталось видимым.");
                    window.ShowCompanion();
                    window.ToggleSwimsuitMode();
                    step++;
                }
                else if (step == 1 && window.HasFinishedSwimsuitEntry)
                {
                    if (!window.SwimsuitMode) throw new InvalidOperationException("B2 сбросился.");
                    window.ToggleVisibility();
                    window.ShowCompanion();
                    if (!window.HasFinishedSwimsuitEntry) throw new InvalidOperationException("B2 прервался при скрытии.");
                    window.ToggleSwimsuitMode();
                    deadline.Restart();
                    step++;
                }
                else if (step == 2 && window.HasFinishedStageEntry)
                {
                    timer.Stop();
                    app.Shutdown(0);
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                timer.Stop();
                app.Shutdown(1);
            }
        };
        timer.Start();
    }
}
