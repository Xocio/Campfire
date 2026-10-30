using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Campfire.Link.Runtime
{
    /// <summary>
    /// A Windows job object that ends every assigned process when Campfire exits, even after a crash.
    /// </summary>
    internal static class ChildProcessJob
    {
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x2000;

        private static readonly Lazy<IntPtr> Handle = new Lazy<IntPtr>(CreateJob);

        /// <summary>Ties <paramref name="process"/> to the lifetime of Campfire. Failures are logged and ignored.</summary>
        public static void Assign(Process process)
        {
            IntPtr job = Handle.Value;
            if (job == IntPtr.Zero)
                return;
            try
            {
                if (!AssignProcessToJobObject(job, process.Handle))
                    LinkLog.Warn("Could not assign process to job object: error " + Marshal.GetLastWin32Error());
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }
        }

        private static IntPtr CreateJob()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
                return IntPtr.Zero;

            var info = new JobObjectExtendedLimitInformationData
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose },
            };
            int size = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformationData));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, buffer, false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size))
                {
                    CloseHandle(job);
                    return IntPtr.Zero;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            // The handle stays open for the lifetime of Campfire; Windows closes it on exit, ending the children.
            return job;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformationData
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
