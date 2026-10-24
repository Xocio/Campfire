using System;
using Campfire.Localization;

namespace Campfire.Link
{
    /// <summary>
    /// A multiplayer failure with a message for the user. The text is looked up on demand so it follows the
    /// display language.
    /// </summary>
    internal sealed class LinkException : Exception
    {
        public LinkException(string textKey, params object[] args)
            : this(null, textKey, args)
        {
        }

        public LinkException(Exception inner, string textKey, params object[] args)
            : base(Text.Format(textKey, args), inner)
        {
            TextKey = textKey;
            TextArgs = args;
        }

        public string TextKey { get; }

        public object[] TextArgs { get; }

        public string LocalizedMessage => Text.Format(TextKey, TextArgs);
    }
}
