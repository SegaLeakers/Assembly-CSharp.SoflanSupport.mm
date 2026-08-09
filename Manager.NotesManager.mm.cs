#pragma warning disable CS0626
using MonoMod;
using System;
using System.Diagnostics;

namespace Manager
{
    [MonoModPatch("global::Manager.NotesManager")]
    public class SoflanNotesManagerHooks
    {
        [MonoModIgnore]
        private static Stopwatch _stopwatch;

        [MonoModIgnore]
        private static float _msecStartGap;

        [MonoModIgnore]
        private static bool _isPlaying;

        private static TimeSpan _soflanCurrentTimeSnapshot;

        public static extern void orig_UpdateTimer();

        public static void UpdateTimer()
        {
            orig_UpdateTimer();

            var elapsed = _isPlaying && _stopwatch != null
                ? _stopwatch.Elapsed
                : TimeSpan.Zero;
            _soflanCurrentTimeSnapshot = elapsed
                + TimeSpan.FromMilliseconds((double)_msecStartGap);
        }

        public extern void orig_clearNotes();

        public void clearNotes()
        {
            orig_clearNotes();
            _soflanCurrentTimeSnapshot = TimeSpan.Zero;
        }

        public static extern void orig_StopPlay();

        public static void StopPlay()
        {
            orig_StopPlay();
            _soflanCurrentTimeSnapshot = TimeSpan.Zero;
        }

        public static TimeSpan GetCurrentTimeSnapshot()
        {
            return _soflanCurrentTimeSnapshot;
        }
    }
}
