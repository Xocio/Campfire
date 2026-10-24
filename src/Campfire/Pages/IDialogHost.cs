using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Campfire.Localization;

namespace Campfire.Pages
{
    internal enum DialogChoice
    {
        Primary,
        Secondary,
        Dismissed,

        /// <summary>The text button, when <see cref="DialogRequest.LinkCloses"/> is set.</summary>
        Link,
    }

    /// <summary>
    /// Content of an in-app dialog.
    /// </summary>
    internal sealed class DialogRequest
    {
        public string Title { get; set; }

        public string Message { get; set; }

        /// <summary>Optional smaller text below the message, such as what to try next.</summary>
        public string Detail { get; set; }

        public string PrimaryText { get; set; }

        /// <summary>Second button; <c>null</c> shows only the primary button.</summary>
        public string SecondaryText { get; set; }

        /// <summary>Shows the title in red.</summary>
        public bool IsError { get; set; }

        /// <summary>Optional text button at the bottom-left that runs <see cref="LinkAction"/> without closing.</summary>
        public string LinkText { get; set; }

        public Action LinkAction { get; set; }

        /// <summary>The text button closes the dialog with <see cref="DialogChoice.Link"/> instead of running an action.</summary>
        public bool LinkCloses { get; set; }

        /// <summary>Title color instead of the theme's; used by notices from the author.</summary>
        public Color? TitleColor { get; set; }

        /// <summary>Message color instead of the theme's.</summary>
        public Color? MessageColor { get; set; }
    }

    /// <summary>
    /// The window that shows in-app dialogs for its pages. Dialogs are shown one at a time, in order.
    /// </summary>
    internal interface IDialogHost
    {
        Task<DialogChoice> ShowDialogAsync(DialogRequest request);
    }

    internal static class Dialogs
    {
        /// <summary>Asks for confirmation; returns whether the user confirmed.</summary>
        public static async Task<bool> ConfirmAsync(this IDialogHost host, string title, string message, string confirmText)
        {
            var choice = await host.ShowDialogAsync(new DialogRequest
            {
                Title = title,
                Message = message,
                PrimaryText = confirmText,
                SecondaryText = Text.Get("Dialog.Cancel"),
            });
            return choice == DialogChoice.Primary;
        }

        /// <summary>Shows a message with a single OK button.</summary>
        public static Task AlertAsync(this IDialogHost host, string title, string message, string detail = null)
        {
            return host.ShowDialogAsync(new DialogRequest
            {
                Title = title,
                Message = message,
                Detail = detail,
                PrimaryText = Text.Get("Dialog.OK"),
            });
        }

        /// <summary>Shows an error with a single OK button.</summary>
        public static Task ErrorAsync(this IDialogHost host, string title, string message, string detail = null)
        {
            return host.ShowDialogAsync(new DialogRequest
            {
                Title = title,
                Message = message,
                Detail = detail,
                PrimaryText = Text.Get("Dialog.OK"),
                IsError = true,
            });
        }

        /// <summary>The dialog host of the window containing <paramref name="element"/>.</summary>
        public static IDialogHost Of(DependencyObject element)
        {
            return Window.GetWindow(element) as IDialogHost;
        }
    }
}
