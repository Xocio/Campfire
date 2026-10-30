using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// The account of a Minecraft game running on this computer, read from the game's command line. Every
    /// launcher passes the player name, UUID and account type there.
    /// </summary>
    /// <remarks>
    /// Only these three values are read. The command line also carries the access token, which is never kept,
    /// logged or sent anywhere.
    /// </remarks>
    internal sealed class GameAccount
    {
        private GameAccount(string name, string uuid, bool isPremium, int processId)
        {
            Name = name;
            Uuid = uuid;
            IsPremium = isPremium;
            ProcessId = processId;
        }

        public string Name { get; }

        /// <summary>UUID without dashes, or <c>null</c>.</summary>
        public string Uuid { get; }

        /// <summary>Whether the game was started with a Microsoft (premium) account.</summary>
        public bool IsPremium { get; }

        /// <summary>Process id of the running game.</summary>
        public int ProcessId { get; }

        /// <summary>The account of the running game, or <c>null</c> when no game is running.</summary>
        public static GameAccount Current { get; private set; }

        /// <summary>Looks for a running game and updates <see cref="Current"/>.</summary>
        public static async Task<GameAccount> RefreshAsync()
        {
            var account = LinkSettings.ReadGameAccount ? await Task.Run(() => Detect()).ConfigureAwait(false) : null;
            Current = account;
            return account;
        }

        private static GameAccount Detect()
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (!process.ProcessName.StartsWith("java", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string commandLine;
                    try
                    {
                        commandLine = ReadCommandLine(process.Id);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
                    {
                        // One unreadable process must not stop the search or fail the room.
                        continue;
                    }
                    if (commandLine == null || commandLine.IndexOf("--username", StringComparison.Ordinal) < 0)
                        continue;

                    var arguments = Split(commandLine);
                    string name = ValueAfter(arguments, "--username");
                    if (string.IsNullOrEmpty(name))
                        continue;

                    string uuid = ValueAfter(arguments, "--uuid")?.Replace("-", "").ToLowerInvariant();
                    string type = ValueAfter(arguments, "--userType");
                    bool premium = (type == "msa" || type == "mojang") && uuid != null && uuid.Length == 32;
                    return new GameAccount(name, uuid, premium, process.Id);
                }
            }
            return null;
        }

        private static string ValueAfter(IReadOnlyList<string> arguments, string option)
        {
            for (int i = 0; i + 1 < arguments.Count; i++)
            {
                if (arguments[i] == option)
                    return arguments[i + 1];
            }
            return null;
        }

        /// <summary>Splits a Windows command line into arguments, honoring double quotes.</summary>
        internal static List<string> Split(string commandLine)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            bool hasToken = false;
            foreach (char c in commandLine)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    hasToken = true;
                }
                else if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (hasToken)
                        result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
                else
                {
                    current.Append(c);
                    hasToken = true;
                }
            }
            if (hasToken)
                result.Add(current.ToString());
            return result;
        }

        internal static string ReadCommandLine(int processId)
        {
            // Windows 7 has no direct way to ask for another process's command line; it goes through WMI there.
            if (!_commandLineQuerySupported)
                return ReadCommandLineFromWmi(processId);

            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero)
                return null;
            try
            {
                int status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out int size);
                if (status == StatusInvalidInfoClass)
                {
                    _commandLineQuerySupported = false;
                    return ReadCommandLineFromWmi(processId);
                }
                if (size <= 0)
                    return null;

                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, size, out _) != 0)
                        return null;

                    // The buffer starts with a UNICODE_STRING that points into the same buffer.
                    // Length is an unsigned 16-bit byte count; reading it signed goes negative for command lines
                    // over 32 KB (a modded game with a long classpath), which made PtrToStringUni throw.
                    int length = (ushort)Marshal.ReadInt16(buffer);
                    IntPtr text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                    if (text == IntPtr.Zero || length < 2)
                        return null;
                    // Never read past the buffer we allocated.
                    long available = size - (text.ToInt64() - buffer.ToInt64());
                    if (available < 2)
                        return null;
                    int chars = (int)Math.Min(length, available) / 2;
                    return Marshal.PtrToStringUni(text, chars);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private const int ProcessQueryLimitedInformation = 0x1000;
        private const int ProcessCommandLineInformation = 60;
        private const int StatusInvalidInfoClass = unchecked((int)0xC0000003);

        private static bool _commandLineQuerySupported = true;

        private static string ReadCommandLineFromWmi(int processId)
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                           "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + processId))
                using (var results = searcher.Get())
                {
                    foreach (System.Management.ManagementBaseObject process in results)
                    {
                        using (process)
                            return process["CommandLine"] as string;
                    }
                }
            }
            catch (Exception ex) when (ex is System.Management.ManagementException || ex is COMException || ex is UnauthorizedAccessException)
            {
            }
            return null;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int size, out int returned);
    }
}
