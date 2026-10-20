using System;
using PCLPlus.Localization;

namespace PCLPlus.Launcher
{
    /// <summary>
    /// A failure whose message can be shown to the user as-is, in the current display language.
    /// </summary>
    internal sealed class LauncherException : Exception
    {
        private readonly string _textKey;
        private readonly object[] _args;

        public LauncherException(string textKey, params object[] args) : this(null, textKey, args)
        {
        }

        public LauncherException(Exception innerException, string textKey, params object[] args)
            : base(textKey, innerException)
        {
            _textKey = textKey;
            _args = args;
        }

        public override string Message => Text.Format(_textKey, _args);
    }
}
