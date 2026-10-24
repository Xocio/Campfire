using System;

namespace Campfire.Patching
{
    /// <summary>
    /// Thrown when the launcher assembly does not have the structure a patch expects.
    /// </summary>
    internal sealed class PatchException : Exception
    {
        public PatchException(string message) : base(message)
        {
        }

        public PatchException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
