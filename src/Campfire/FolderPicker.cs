using System;
using System.Runtime.InteropServices;

namespace Campfire
{
    /// <summary>
    /// The folder chooser of current Windows: the Explorer-style window with an address bar, where a path can be typed
    /// or pasted. The older tree-only chooser of Windows Forms has neither.
    /// </summary>
    internal static class FolderPicker
    {
        private const uint PickFolders = 0x20;
        private const uint ForceFileSystem = 0x40;
        private const uint PathMustExist = 0x800;
        private const uint FileSystemPath = 0x80058000;
        private const int Cancelled = unchecked((int)0x800704C7);

        /// <summary>Returns the chosen folder, or <c>null</c> when the user cancelled.</summary>
        public static string Pick(IntPtr owner, string title)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            try
            {
                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | PickFolders | ForceFileSystem | PathMustExist);
                dialog.SetTitle(title);

                int result = dialog.Show(owner);
                if (result == Cancelled)
                    return null;
                if (result != 0)
                    Marshal.ThrowExceptionForHR(result);

                dialog.GetResult(out IShellItem item);
                try
                {
                    item.GetDisplayName(FileSystemPath, out IntPtr name);
                    try
                    {
                        return Marshal.PtrToStringUni(name);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(name);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);

            void GetParent(out IShellItem parent);

            void GetDisplayName(uint form, out IntPtr name);

            void GetAttributes(uint mask, out uint attributes);

            void Compare(IShellItem other, uint hint, out int order);
        }

        /// <summary>IFileOpenDialog with the methods of IModalWindow and IFileDialog first, in the order of the interface.</summary>
        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr owner);

            void SetFileTypes(uint count, IntPtr filters);

            void SetFileTypeIndex(uint index);

            void GetFileTypeIndex(out uint index);

            void Advise(IntPtr events, out uint cookie);

            void Unadvise(uint cookie);

            void SetOptions(uint options);

            void GetOptions(out uint options);

            void SetDefaultFolder(IShellItem folder);

            void SetFolder(IShellItem folder);

            void GetFolder(out IShellItem folder);

            void GetCurrentSelection(out IShellItem item);

            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

            void GetFileName(out IntPtr name);

            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

            void GetResult(out IShellItem item);

            void AddPlace(IShellItem item, uint placement);

            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

            void Close(int result);

            void SetClientGuid(ref Guid guid);

            void ClearClientData();

            void SetFilter(IntPtr filter);

            void GetResults(out IntPtr items);

            void GetSelectedItems(out IntPtr items);
        }
    }
}
