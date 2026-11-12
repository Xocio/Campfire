using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using Campfire.Localization;
using Campfire.Themes;

namespace Campfire
{
    public partial class App : Application
    {
        private const string LanguageArgument = "--lang=";

        /// <summary>Wait before clearing the old data folder, while the previous Campfire finishes exiting.</summary>
        private static readonly TimeSpan CleanupDelay = TimeSpan.FromSeconds(5);

        protected override void OnStartup(StartupEventArgs e)
        {
            // Opening Campfire again shows the one that is already running.
            if (!SingleInstance.Claim())
            {
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (sender, args) => WriteCrashLog(args.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => WriteCrashLog(args.ExceptionObject as Exception);

            // Turns on TLS 1.2 for Windows 7.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            // Priority: command line (--lang=en / --lang=zh), saved choice, system language.
            string argument = e.Args.FirstOrDefault(a => a.StartsWith(LanguageArgument, StringComparison.OrdinalIgnoreCase));
            Text.Language = AppSettings.ParseLanguage(argument?.Substring(LanguageArgument.Length))
                ?? AppSettings.LoadLanguage()
                ?? Text.DetectSystemLanguage();

            base.OnStartup(e);
            if (AppSettings.IsFirstRun)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var setup = new SetupWindow();
                setup.ShowDialog();
                if (!setup.Confirmed)
                {
                    Shutdown();
                    return;
                }
            }
            new MainWindow().Show();
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            Task.Delay(CleanupDelay).ContinueWith(_ => DataLocation.FinishMove());
        }

        /// <summary>Writes the last unexpected error to the data folder.</summary>
        private static void WriteCrashLog(Exception exception)
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppSettings.DataDirectory);
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppSettings.DataDirectory, "crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + exception);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ThemeManager.Shutdown();
            SingleInstance.Release();
            base.OnExit(e);
        }
    }
}
