using System;
using System.Linq;
using System.Net;
using System.Windows;
using Campfire.Localization;
using Campfire.Themes;

namespace Campfire
{
    public partial class App : Application
    {
        private const string LanguageArgument = "--lang=";

        static App()
        {
            // Must run before any type that references an embedded dependency is loaded.
            EmbeddedAssemblyLoader.Register();
        }

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

            // Windows 7 only offers TLS 1.2 when a program asks for it; download sites require it.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            // Priority: command line (--lang=en / --lang=zh), saved choice, system language.
            string argument = e.Args.FirstOrDefault(a => a.StartsWith(LanguageArgument, StringComparison.OrdinalIgnoreCase));
            Text.Language = AppSettings.ParseLanguage(argument?.Substring(LanguageArgument.Length))
                ?? AppSettings.LoadLanguage()
                ?? Text.DetectSystemLanguage();

            base.OnStartup(e);
            new MainWindow().Show();
        }

        /// <summary>Keeps the last unexpected error in the data folder, so it can be attached to a bug report.</summary>
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
