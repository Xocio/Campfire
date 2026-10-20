using System;
using System.Linq;
using System.Windows;
using PCLPlus.Localization;

namespace PCLPlus
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
            // Priority: command line (--lang=en / --lang=zh), saved choice, system language.
            string argument = e.Args.FirstOrDefault(a => a.StartsWith(LanguageArgument, StringComparison.OrdinalIgnoreCase));
            Text.Language = AppSettings.ParseLanguage(argument?.Substring(LanguageArgument.Length))
                ?? AppSettings.LoadLanguage()
                ?? Text.DetectSystemLanguage();

            base.OnStartup(e);
        }
    }
}
