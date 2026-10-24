using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Campfire
{
    /// <summary>
    /// Keeps one Campfire per Windows session, wherever its program file lies. Two of them would share and overwrite
    /// each other's settings and fight over the network ports. Opening Campfire again brings the running one back,
    /// which matters when its window is hidden and Windows has tucked the notification area icon away.
    /// </summary>
    internal static class SingleInstance
    {
        private const string Name = @"Local\Campfire";

        private static Mutex _mutex;
        private static EventWaitHandle _wake;
        private static RegisteredWaitHandle _listener;

        /// <summary>
        /// Claims the session. Returns <c>false</c> when a Campfire already runs; that one is then asked to show its
        /// window.
        /// </summary>
        public static bool Claim()
        {
            _wake = new EventWaitHandle(false, EventResetMode.AutoReset, Name + "-show");
            _mutex = new Mutex(false, Name);
            bool owned;
            try
            {
                owned = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // The previous Campfire ended without letting go; the claim is ours now.
                owned = true;
            }
            if (owned)
                return true;

            // Lets the running Campfire take the foreground, which only the program the user just opened may grant.
            AllowSetForegroundWindow(AsfwAny);
            _wake.Set();
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        /// <summary>Calls <paramref name="show"/> on a pool thread whenever Campfire is opened again.</summary>
        public static void Listen(Action show)
        {
            if (_wake == null || _listener != null)
                return;
            _listener = ThreadPool.RegisterWaitForSingleObject(_wake, (state, timedOut) => show(), null,
                Timeout.Infinite, false);
        }

        /// <summary>Gives up the claim, so a Campfire started right after this one exits runs normally.</summary>
        public static void Release()
        {
            _listener?.Unregister(null);
            _listener = null;
            if (_mutex == null)
                return;
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
        }

        private const int AsfwAny = -1;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }
}
