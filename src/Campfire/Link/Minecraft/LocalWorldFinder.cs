using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// Waits for "Open to LAN" on this computer, so the host never has to type a port.
    /// </summary>
    /// <remarks>
    /// Rather than listening for Minecraft's LAN announcements, which would make Windows Firewall ask for
    /// permission, it reads the system table of listening TCP ports, picks those owned by a Java process and
    /// pings them locally. A dedicated server running on this computer is found the same way.
    /// </remarks>
    internal static class LocalWorldFinder
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

        /// <summary>Ports that did not answer a ping are skipped for a while; Java programs listen on other ports too.</summary>
        private static readonly TimeSpan RetryRejected = TimeSpan.FromSeconds(20);

        /// <summary>Returns the game port of a Minecraft world or server running on this computer.</summary>
        public static async Task<int> WaitForLocalWorldAsync(CancellationToken cancellation)
        {
            var rejected = new Dictionary<int, DateTime>();
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (int port in GetJavaListeningPorts())
                {
                    if (rejected.TryGetValue(port, out var since) && DateTime.UtcNow - since < RetryRejected)
                        continue;
                    if (await MinecraftProbe.IsServerAsync(port, TimeSpan.FromSeconds(2)).ConfigureAwait(false))
                        return port;
                    rejected[port] = DateTime.UtcNow;
                }
                await Task.Delay(PollInterval, cancellation).ConfigureAwait(false);
            }
        }

        private static IEnumerable<int> GetJavaListeningPorts()
        {
            var ports = new SortedSet<int>();
            var names = new Dictionary<int, bool>();
            foreach (var (port, pid) in ReadListeners(AfInet, 24, 8, 20).Concat(ReadListeners(AfInet6, 56, 20, 52)))
            {
                if (!names.TryGetValue(pid, out bool isJava))
                {
                    isJava = IsJava(pid);
                    names[pid] = isJava;
                }
                if (isJava)
                    ports.Add(port);
            }
            return ports;
        }

        private static bool IsJava(int pid)
        {
            try
            {
                using (var process = Process.GetProcessById(pid))
                    return process.ProcessName.StartsWith("java", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is Win32Exception)
            {
                return false;
            }
        }

        /// <summary>Reads (port, process id) pairs of listening sockets from the IP Helper table.</summary>
        /// <param name="rowSize">Size of one row in bytes.</param>
        /// <param name="portOffset">Offset of the local port within a row.</param>
        /// <param name="pidOffset">Offset of the owning process id within a row.</param>
        private static List<(int Port, int Pid)> ReadListeners(int family, int rowSize, int portOffset, int pidOffset)
        {
            var result = new List<(int, int)>();
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TcpTableOwnerPidListener, 0);

            for (int attempt = 0; attempt < 3 && size > 0; attempt++)
            {
                IntPtr table = Marshal.AllocHGlobal(size);
                try
                {
                    uint error = GetExtendedTcpTable(table, ref size, false, family, TcpTableOwnerPidListener, 0);
                    if (error == ErrorInsufficientBuffer)
                        continue;
                    if (error != 0)
                        break;

                    int count = Marshal.ReadInt32(table);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr row = table + 4 + i * rowSize;
                        int rawPort = Marshal.ReadInt32(row, portOffset);
                        // The port is stored in network byte order in the low 16 bits.
                        int port = ((rawPort & 0xFF) << 8) | ((rawPort >> 8) & 0xFF);
                        result.Add((port, Marshal.ReadInt32(row, pidOffset)));
                    }
                    break;
                }
                finally
                {
                    Marshal.FreeHGlobal(table);
                }
            }
            return result;
        }

        private const int AfInet = 2;
        private const int AfInet6 = 23;
        private const int TcpTableOwnerPidListener = 3;
        private const uint ErrorInsufficientBuffer = 122;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);
    }
}
