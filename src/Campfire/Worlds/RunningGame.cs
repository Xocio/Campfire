using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Campfire.Link.Minecraft;

namespace Campfire.Worlds
{
    /// <summary>
    /// A Minecraft game running on this computer and the folder it keeps its worlds in, read from its command line.
    /// </summary>
    /// <remarks>
    /// Every launcher passes the game folder as --gameDir, so this is where the game really looks for worlds,
    /// whatever the launcher's isolation settings are. Only that value and the version path are read; the access
    /// token on the same command line is never kept.
    /// </remarks>
    internal sealed class RunningGame
    {
        private readonly string _commandLine;

        private RunningGame(int processId, string gameDirectory, string commandLine)
        {
            ProcessId = processId;
            GameDirectory = gameDirectory;
            _commandLine = commandLine;
        }

        public int ProcessId { get; }

        /// <summary>Full path of the game folder; its saves folder holds the worlds.</summary>
        public string GameDirectory { get; }

        public string SavesDirectory => Path.Combine(GameDirectory, "saves");

        /// <summary>Whether the game was started from <paramref name="version"/>, judged by the version folder on its class path.</summary>
        public bool Runs(InstalledVersion version)
        {
            // Launchers may pass the folder through a junction or in another spelling, so only the part from
            // "versions" on is compared.
            string marker = Path.DirectorySeparatorChar + "versions" + Path.DirectorySeparatorChar + version.Name + Path.DirectorySeparatorChar;
            return _commandLine.Replace('/', Path.DirectorySeparatorChar).IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Every running game whose command line names its game folder.</summary>
        public static IReadOnlyList<RunningGame> Find()
        {
            var games = new List<RunningGame>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (!process.ProcessName.StartsWith("java", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var game = Read(process.Id);
                    if (game != null)
                        games.Add(game);
                }
            }
            return games;
        }

        /// <summary>The game running as process <paramref name="processId"/>, or <c>null</c> when that process is not one.</summary>
        public static RunningGame FromProcess(int processId)
        {
            return processId > 0 ? Read(processId) : null;
        }

        /// <summary>
        /// The installed version this game was started from, found through the version folder its launcher named,
        /// or <c>null</c> when the command line does not tell.
        /// </summary>
        public InstalledVersion FindVersion()
        {
            var arguments = GameAccount.Split(_commandLine);
            int index = arguments.IndexOf("--version");
            if (index < 0 || index + 1 >= arguments.Count || arguments[index + 1].Length == 0
                || arguments[index + 1].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return null;
            string name = arguments[index + 1];

            // An isolated game folder is the version folder itself; otherwise the folder shows up in the paths the
            // launcher passes, such as the natives folder.
            var folders = new List<string>();
            string parent = Path.GetDirectoryName(GameDirectory);
            if (parent != null && string.Equals(Path.GetFileName(parent), "versions", StringComparison.OrdinalIgnoreCase))
                folders.Add(Path.GetDirectoryName(parent));
            folders.Add(GameDirectory);
            string pattern = @"([A-Za-z]:\\[^""=;]*?)\\versions\\" + Regex.Escape(name) + @"\\";
            foreach (Match match in Regex.Matches(_commandLine.Replace('/', '\\'), pattern, RegexOptions.IgnoreCase))
                folders.Add(match.Groups[1].Value);

            foreach (string folder in folders.Select(Full).Where(f => f != null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(Path.Combine(folder, "versions", name)))
                    continue;
                var version = InstalledVersion.Load(new GameFolder(folder, GameFolderSource.Default), name);
                if (version != null)
                    return version;
            }
            return null;
        }

        private static RunningGame Read(int processId)
        {
            string commandLine = GameAccount.ReadCommandLine(processId);
            if (commandLine == null || commandLine.IndexOf("--gameDir", StringComparison.Ordinal) < 0)
                return null;

            var arguments = GameAccount.Split(commandLine);
            int index = arguments.IndexOf("--gameDir");
            if (index < 0 || index + 1 >= arguments.Count)
                return null;

            string directory = Full(arguments[index + 1]);
            return directory == null ? null : new RunningGame(processId, directory, commandLine);
        }

        private static string Full(string path)
        {
            try
            {
                return Path.IsPathRooted(path) ? Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) : null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }
    }
}
