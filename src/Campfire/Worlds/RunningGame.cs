using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

                    string commandLine = GameAccount.ReadCommandLine(process.Id);
                    if (commandLine == null || commandLine.IndexOf("--gameDir", StringComparison.Ordinal) < 0)
                        continue;

                    var arguments = GameAccount.Split(commandLine);
                    int index = arguments.IndexOf("--gameDir");
                    if (index < 0 || index + 1 >= arguments.Count)
                        continue;

                    string directory = Full(arguments[index + 1]);
                    if (directory != null)
                        games.Add(new RunningGame(process.Id, directory, commandLine));
                }
            }
            return games;
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
