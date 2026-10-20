using System;

namespace PCLPlus.Launcher
{
    /// <summary>
    /// A failure whose message can be shown to the user as-is.
    /// </summary>
    internal sealed class LauncherException : Exception
    {
        public LauncherException(string message) : base(message)
        {
        }

        public LauncherException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
