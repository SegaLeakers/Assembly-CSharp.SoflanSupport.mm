using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace SoflanClockTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length != 1)
                    throw new ArgumentException("usage: SoflanClockTests <patched-Assembly-CSharp.dll>");

                Console.WriteLine(
                    "runtime=unity-mono version=" + Environment.Version
                    + " executable=" + Environment.GetCommandLineArgs()[0]);

                var assemblyPath = Path.GetFullPath(args[0]);
                var assembly = Assembly.LoadFrom(assemblyPath);
                var notesManager = assembly.GetType("Manager.NotesManager", true);
                var startPlay = RequireMethod(notesManager, "StartPlay", typeof(float));
                var updateTimer = RequireMethod(notesManager, "UpdateTimer");
                var pause = RequireMethod(notesManager, "Pause", typeof(bool));
                var stopPlay = RequireMethod(notesManager, "StopPlay");
                var getCurrentMsec = RequireMethod(notesManager, "GetCurrentMsec");
                var getSnapshot = RequireMethod(notesManager, "GetCurrentTimeSnapshot");
                var gameClock = assembly.GetType("SoflanSupport.SoflanGameClock", true);
                var getGameClockTime = RequireMethod(gameClock, "get_CurrentTime");

                Require(getSnapshot.ReturnType == typeof(TimeSpan),
                    "GetCurrentTimeSnapshot must return TimeSpan");

                stopPlay.Invoke(null, null);
                updateTimer.Invoke(null, null);
                Require(ReadSnapshot(getSnapshot) == TimeSpan.Zero,
                    "stopped clock snapshot must be zero");

                startPlay.Invoke(null, new object[] { 0f });
                Thread.Sleep(35);
                updateTimer.Invoke(null, null);

                var first = ReadSnapshot(getSnapshot);
                Require(ReadSnapshot(getGameClockTime) == first,
                    "SoflanGameClock did not return the NotesManager snapshot");
                var originalFloat = (float)getCurrentMsec.Invoke(null, null);
                Require(first > TimeSpan.Zero, "running clock snapshot did not advance");
                Require(Math.Abs(first.TotalMilliseconds - originalFloat) < 1.0,
                    "TimeSpan snapshot diverged from original frame clock: snapshot="
                    + first.TotalMilliseconds.ToString("R")
                    + " original=" + originalFloat.ToString("R"));

                Thread.Sleep(25);
                Require(ReadSnapshot(getSnapshot) == first,
                    "snapshot changed without UpdateTimer");

                updateTimer.Invoke(null, null);
                var second = ReadSnapshot(getSnapshot);
                Require(second > first, "snapshot did not advance on UpdateTimer");
                Require(ReadSnapshot(getGameClockTime) == second,
                    "SoflanGameClock diverged after UpdateTimer");

                pause.Invoke(null, new object[] { true });
                updateTimer.Invoke(null, null);
                var paused = ReadSnapshot(getSnapshot);
                Thread.Sleep(25);
                updateTimer.Invoke(null, null);
                Require(ReadSnapshot(getSnapshot) == paused,
                    "paused snapshot continued advancing");

                pause.Invoke(null, new object[] { false });
                Thread.Sleep(25);
                updateTimer.Invoke(null, null);
                Require(ReadSnapshot(getSnapshot) > paused,
                    "resumed snapshot did not advance");

                stopPlay.Invoke(null, null);
                updateTimer.Invoke(null, null);
                Require(ReadSnapshot(getSnapshot) == TimeSpan.Zero,
                    "StopPlay did not clear the snapshot");

                Console.WriteLine(
                    "SoflanClockTests: PASS firstMs=" + first.TotalMilliseconds.ToString("R")
                    + " originalFloatMs=" + originalFloat.ToString("R"));
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("SoflanClockTests: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static MethodInfo RequireMethod(Type type, string name, params Type[] parameterTypes)
        {
            var method = type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                parameterTypes,
                null);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static TimeSpan ReadSnapshot(MethodInfo getSnapshot)
        {
            return (TimeSpan)getSnapshot.Invoke(null, null);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
