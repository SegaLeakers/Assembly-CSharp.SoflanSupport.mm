using System;
using System.Collections.Generic;
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.EditorObjects;
using OngekiFumenEditor.Core.Base.OngekiObjects;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;
using OngekiFumenEditor.Core.Utils;

namespace SoflanVisibilityTests
{
    internal static class Program
    {
        private const int GridsPerBar = (int)TGrid.DEFAULT_RES_T;
        private const double FrameMsec = 1000.0 / 60.0;
        private static int allocationChecksum;

        private sealed class VisibilityCase
        {
            internal string Name;
            internal BpmList BpmList;
            internal SoflanList SoflanList;
            internal double ViewHeight;
            internal int MaxTotalGrid;
        }

        private static int Main(string[] args)
        {
            try
            {
                var runtimeLabel = args.Length > 0 ? args[0] : "unknown";
                Console.WriteLine(
                    "runtime=" + runtimeLabel
                    + " version=" + Environment.Version
                    + " executable=" + Environment.GetCommandLineArgs()[0]);

                var cases = BuildCases();
                var comparisons = 0L;
                var frames = 0;
                foreach (var visibilityCase in cases)
                {
                    comparisons += RunBooleanDifferential(visibilityCase);
                    frames += RunRegistrationDifferential(visibilityCase);
                }

                RunMultiGroupRegistrationDifferential(cases[4], cases[5]);
                RunReuseAndAllocationCheck(cases[5], runtimeLabel);

                Console.WriteLine(
                    "SoflanVisibilityTests: PASS cases=" + cases.Count
                    + " frames=" + frames
                    + " comparisons=" + comparisons
                    + " checksum=" + allocationChecksum);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("SoflanVisibilityTests: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static List<VisibilityCase> BuildCases()
        {
            var baseBpm = BuildBpmList(120.0);
            var changingBpm = BuildBpmList(
                120.0,
                Bpm(3 * GridsPerBar, 180.0),
                Bpm(5 * GridsPerBar, 90.0),
                Bpm(8 * GridsPerBar, 240.0));

            return new List<VisibilityCase>
            {
                Case("constant-1x", baseBpm, 1600.0, 12, Point(0, 1f)),
                Case("constant-2x", baseBpm, 1600.0, 12, Point(0, 2f)),
                Case("constant-0.5x", baseBpm, 1600.0, 12, Point(0, 0.5f)),
                Case(
                    "stop-tail",
                    baseBpm,
                    1600.0,
                    12,
                    Point(0, 1f),
                    Point(2, 0f),
                    Point(3, 1f)),
                Case(
                    "negative-foldback-group-1",
                    changingBpm,
                    1800.0,
                    14,
                    Point(0, 1f, 1),
                    Point(2, -1f, 1),
                    Point(4, 2f, 1),
                    Point(6, 0.5f, 1),
                    Point(9, 1f, 1)),
                Case(
                    "stop-negative-multibpm-group-2",
                    changingBpm,
                    1800.0,
                    14,
                    Point(0, 0.5f, 2),
                    Point(2, 0f, 2),
                    Point(3, -0.75f, 2),
                    Point(5, 2f, 2),
                    Point(7, -0.5f, 2),
                    Point(9, 1f, 2))
            };
        }

        private static VisibilityCase Case(
            string name,
            BpmList bpmList,
            double viewHeight,
            int maxBar,
            params KeyframeSoflan[] points)
        {
            var soflans = new ISoflan[points.Length];
            for (var i = 0; i < points.Length; i++)
                soflans[i] = points[i];

            return new VisibilityCase
            {
                Name = name,
                BpmList = bpmList,
                SoflanList = new SoflanList(soflans),
                ViewHeight = viewHeight,
                MaxTotalGrid = maxBar * GridsPerBar
            };
        }

        private static KeyframeSoflan Point(int bar, float speed, int group = 0)
        {
            return new KeyframeSoflan
            {
                TGrid = TGrid.FromTotalGrid(bar * GridsPerBar),
                Speed = speed,
                SoflanGroup = group
            };
        }

        private static BPMChange Bpm(int totalGrid, double bpm)
        {
            return new BPMChange
            {
                TGrid = TGrid.FromTotalGrid(totalGrid),
                BPM = bpm
            };
        }

        private static BpmList BuildBpmList(double firstBpm, params BPMChange[] changes)
        {
            var result = new BpmList { FirstBpm = firstBpm };
            for (var i = 0; i < changes.Length; i++)
                result.Add(changes[i]);
            return result;
        }

        private static long RunBooleanDifferential(VisibilityCase visibilityCase)
        {
            var oldRanges = new List<SoflanList.VisibleTimeSpanRange>(32);
            var newRanges = new List<SoflanList.VisibleTotalGridRange>(32);
            var oldScratch = new SoflanList.VisibleRangeQueryScratch();
            var newScratch = new SoflanList.VisibleRangeQueryScratch();
            var samples = new List<int>(160);
            var random = new Random(StableSeed(visibilityCase.Name));
            var endMsec = ChartEndMsec(visibilityCase);
            long comparisons = 0;

            for (var currentMsec = 0.0; currentMsec <= endMsec; currentMsec += 37.0)
            {
                var currentY = AudioMsecToY(visibilityCase, currentMsec);
                visibilityCase.SoflanList.FillVisibleTimeSpanRangesForGamePreview(
                    currentY,
                    visibilityCase.ViewHeight,
                    visibilityCase.BpmList,
                    oldRanges,
                    oldScratch);
                visibilityCase.SoflanList.FillVisibleTotalGridRangesForGamePreview(
                    currentY,
                    visibilityCase.ViewHeight,
                    visibilityCase.BpmList,
                    newRanges,
                    newScratch);

                samples.Clear();
                AddRangeBoundarySamples(samples, newRanges, visibilityCase.MaxTotalGrid);
                AddTimeBoundarySamples(samples, oldRanges, visibilityCase);
                for (var totalGrid = 0; totalGrid <= visibilityCase.MaxTotalGrid; totalGrid += 97)
                    samples.Add(totalGrid);
                for (var i = 0; i < 32; i++)
                    samples.Add(random.Next(0, visibilityCase.MaxTotalGrid + 1));

                for (var i = 0; i < samples.Count; i++)
                {
                    var totalGrid = samples[i];
                    var noteTime = TotalGridToAudioTime(visibilityCase, totalGrid);
                    var oldVisible = Contains(oldRanges, noteTime);
                    var newVisible = Contains(newRanges, totalGrid);
                    Require(
                        oldVisible == newVisible,
                        visibilityCase.Name
                        + ": visibility mismatch currentMsec=" + currentMsec.ToString("R")
                        + " currentY=" + currentY.ToString("R")
                        + " totalGrid=" + totalGrid
                        + " noteTicks=" + noteTime.Ticks
                        + " old=" + oldVisible
                        + " new=" + newVisible
                        + " oldRanges=" + Format(oldRanges)
                        + " newRanges=" + Format(newRanges));
                    comparisons++;
                }
            }

            return comparisons;
        }

        private static int RunRegistrationDifferential(VisibilityCase visibilityCase)
        {
            var noteGrids = BuildNoteGrids(visibilityCase.MaxTotalGrid);
            var oldFirstFrames = NewFilledArray(noteGrids.Length, -1);
            var newFirstFrames = NewFilledArray(noteGrids.Length, -1);
            var oldRanges = new List<SoflanList.VisibleTimeSpanRange>(32);
            var newRanges = new List<SoflanList.VisibleTotalGridRange>(32);
            var oldScratch = new SoflanList.VisibleRangeQueryScratch();
            var newScratch = new SoflanList.VisibleRangeQueryScratch();
            var endMsec = ChartEndMsec(visibilityCase);
            var frameCount = (int)Math.Ceiling(endMsec / FrameMsec) + 1;
            var oldPeak = 0;
            var newPeak = 0;

            for (var frame = 0; frame < frameCount; frame++)
            {
                var currentMsec = frame * FrameMsec;
                var currentY = AudioMsecToY(visibilityCase, currentMsec);
                visibilityCase.SoflanList.FillVisibleTimeSpanRangesForGamePreview(
                    currentY,
                    visibilityCase.ViewHeight,
                    visibilityCase.BpmList,
                    oldRanges,
                    oldScratch);
                visibilityCase.SoflanList.FillVisibleTotalGridRangesForGamePreview(
                    currentY,
                    visibilityCase.ViewHeight,
                    visibilityCase.BpmList,
                    newRanges,
                    newScratch);

                var oldVisibleCount = 0;
                var newVisibleCount = 0;
                for (var noteIndex = 0; noteIndex < noteGrids.Length; noteIndex++)
                {
                    var totalGrid = noteGrids[noteIndex];
                    var oldVisible = Contains(
                        oldRanges,
                        TotalGridToAudioTime(visibilityCase, totalGrid));
                    var newVisible = Contains(newRanges, totalGrid);
                    Require(
                        oldVisible == newVisible,
                        visibilityCase.Name
                        + ": frame visibility mismatch frame=" + frame
                        + " totalGrid=" + totalGrid);

                    if (oldVisible)
                    {
                        oldVisibleCount++;
                        if (oldFirstFrames[noteIndex] < 0)
                            oldFirstFrames[noteIndex] = frame;
                    }
                    if (newVisible)
                    {
                        newVisibleCount++;
                        if (newFirstFrames[noteIndex] < 0)
                            newFirstFrames[noteIndex] = frame;
                    }
                }

                oldPeak = Math.Max(oldPeak, oldVisibleCount);
                newPeak = Math.Max(newPeak, newVisibleCount);
            }

            Require(
                oldPeak == newPeak,
                visibilityCase.Name + ": peak visible count changed old=" + oldPeak + " new=" + newPeak);
            for (var i = 0; i < noteGrids.Length; i++)
            {
                Require(
                    oldFirstFrames[i] == newFirstFrames[i],
                    visibilityCase.Name
                    + ": first registration frame changed totalGrid=" + noteGrids[i]
                    + " old=" + oldFirstFrames[i]
                    + " new=" + newFirstFrames[i]);
            }

            Console.WriteLine(
                "visibility-case=" + visibilityCase.Name
                + " frames=" + frameCount
                + " notes=" + noteGrids.Length
                + " peak=" + newPeak);
            return frameCount;
        }

        private static void RunMultiGroupRegistrationDifferential(
            VisibilityCase firstGroup,
            VisibilityCase secondGroup)
        {
            var firstNotes = BuildNoteGrids(firstGroup.MaxTotalGrid);
            var secondNotes = BuildNoteGrids(secondGroup.MaxTotalGrid);
            var firstOld = new List<SoflanList.VisibleTimeSpanRange>(32);
            var firstNew = new List<SoflanList.VisibleTotalGridRange>(32);
            var secondOld = new List<SoflanList.VisibleTimeSpanRange>(32);
            var secondNew = new List<SoflanList.VisibleTotalGridRange>(32);
            var firstOldScratch = new SoflanList.VisibleRangeQueryScratch();
            var firstNewScratch = new SoflanList.VisibleRangeQueryScratch();
            var secondOldScratch = new SoflanList.VisibleRangeQueryScratch();
            var secondNewScratch = new SoflanList.VisibleRangeQueryScratch();
            var endMsec = Math.Max(ChartEndMsec(firstGroup), ChartEndMsec(secondGroup));
            var frameCount = (int)Math.Ceiling(endMsec / FrameMsec) + 1;
            var oldPeak = 0;
            var newPeak = 0;

            for (var frame = 0; frame < frameCount; frame++)
            {
                var currentMsec = frame * FrameMsec;
                FillBoth(firstGroup, currentMsec, firstOld, firstNew, firstOldScratch, firstNewScratch);
                FillBoth(secondGroup, currentMsec, secondOld, secondNew, secondOldScratch, secondNewScratch);

                var oldVisible = CountVisible(firstGroup, firstNotes, firstOld)
                    + CountVisible(secondGroup, secondNotes, secondOld);
                var newVisible = CountVisible(firstNotes, firstNew)
                    + CountVisible(secondNotes, secondNew);
                Require(
                    oldVisible == newVisible,
                    "multi-group visible count changed frame=" + frame
                    + " old=" + oldVisible
                    + " new=" + newVisible);
                oldPeak = Math.Max(oldPeak, oldVisible);
                newPeak = Math.Max(newPeak, newVisible);
            }

            Require(oldPeak == newPeak, "multi-group peak visible count changed");
            Console.WriteLine(
                "visibility-multigroup frames=" + frameCount
                + " peak=" + newPeak);
        }

        private static void FillBoth(
            VisibilityCase visibilityCase,
            double currentMsec,
            List<SoflanList.VisibleTimeSpanRange> oldRanges,
            List<SoflanList.VisibleTotalGridRange> newRanges,
            SoflanList.VisibleRangeQueryScratch oldScratch,
            SoflanList.VisibleRangeQueryScratch newScratch)
        {
            var currentY = AudioMsecToY(visibilityCase, currentMsec);
            visibilityCase.SoflanList.FillVisibleTimeSpanRangesForGamePreview(
                currentY,
                visibilityCase.ViewHeight,
                visibilityCase.BpmList,
                oldRanges,
                oldScratch);
            visibilityCase.SoflanList.FillVisibleTotalGridRangesForGamePreview(
                currentY,
                visibilityCase.ViewHeight,
                visibilityCase.BpmList,
                newRanges,
                newScratch);
        }

        private static void RunReuseAndAllocationCheck(
            VisibilityCase visibilityCase,
            string runtimeLabel)
        {
            var output = new List<SoflanList.VisibleTotalGridRange>(64);
            var scratch = new SoflanList.VisibleRangeQueryScratch();
            var currentYs = new double[256];
            var endMsec = ChartEndMsec(visibilityCase);
            for (var i = 0; i < currentYs.Length; i++)
            {
                currentYs[i] = AudioMsecToY(
                    visibilityCase,
                    endMsec * i / (currentYs.Length - 1));
            }

            for (var pass = 0; pass < 8; pass++)
                FillAll(visibilityCase, currentYs, output, scratch);

            var outputCapacity = output.Capacity;
            var queryCapacity = scratch.QuerySegments.Capacity;
            var gridCapacity = scratch.GridRanges.Capacity;
            var outputReference = output;
            var scratchReference = scratch;

#if NET8_0_OR_GREATER
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
#endif

            for (var pass = 0; pass < 128; pass++)
                FillAll(visibilityCase, currentYs, output, scratch);

#if NET8_0_OR_GREATER
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Require(allocated == 0, "steady-state TotalGrid range fill allocated " + allocated + " bytes");
            Console.WriteLine("visibility-allocation runtime=" + runtimeLabel + " bytes=" + allocated);
#else
            Console.WriteLine(
                "visibility-allocation runtime=" + runtimeLabel
                + " check=profiler-only reason=GetAllocatedBytesForCurrentThread-unavailable");
#endif

            Require(ReferenceEquals(outputReference, output), "output List instance changed");
            Require(ReferenceEquals(scratchReference, scratch), "scratch instance changed");
            Require(output.Capacity == outputCapacity, "output List capacity grew after warmup");
            Require(scratch.QuerySegments.Capacity == queryCapacity, "query scratch capacity grew after warmup");
            Require(scratch.GridRanges.Capacity == gridCapacity, "grid scratch capacity grew after warmup");
        }

        private static void FillAll(
            VisibilityCase visibilityCase,
            double[] currentYs,
            List<SoflanList.VisibleTotalGridRange> output,
            SoflanList.VisibleRangeQueryScratch scratch)
        {
            for (var i = 0; i < currentYs.Length; i++)
            {
                visibilityCase.SoflanList.FillVisibleTotalGridRangesForGamePreview(
                    currentYs[i],
                    visibilityCase.ViewHeight,
                    visibilityCase.BpmList,
                    output,
                    scratch);
                allocationChecksum ^= output.Count;
            }
        }

        private static int[] BuildNoteGrids(int maxTotalGrid)
        {
            var result = new List<int>();
            for (var totalGrid = 0; totalGrid <= maxTotalGrid; totalGrid += 31)
                result.Add(totalGrid);
            if (result[result.Count - 1] != maxTotalGrid)
                result.Add(maxTotalGrid);
            return result.ToArray();
        }

        private static int[] NewFilledArray(int length, int value)
        {
            var result = new int[length];
            for (var i = 0; i < result.Length; i++)
                result[i] = value;
            return result;
        }

        private static void AddRangeBoundarySamples(
            List<int> samples,
            List<SoflanList.VisibleTotalGridRange> ranges,
            int maxTotalGrid)
        {
            for (var i = 0; i < ranges.Count; i++)
            {
                AddBoundary(samples, ranges[i].MinTotalGrid, maxTotalGrid);
                AddBoundary(samples, ranges[i].MaxTotalGrid, maxTotalGrid);
            }
        }

        private static void AddTimeBoundarySamples(
            List<int> samples,
            List<SoflanList.VisibleTimeSpanRange> ranges,
            VisibilityCase visibilityCase)
        {
            for (var i = 0; i < ranges.Count; i++)
            {
                AddBoundary(
                    samples,
                    AudioTimeToTotalGrid(visibilityCase, ranges[i].MinAudioTime),
                    visibilityCase.MaxTotalGrid);
                AddBoundary(
                    samples,
                    AudioTimeToTotalGrid(visibilityCase, ranges[i].MaxAudioTime),
                    visibilityCase.MaxTotalGrid);
            }
        }

        private static void AddBoundary(List<int> samples, int boundary, int maxTotalGrid)
        {
            for (var delta = -1; delta <= 1; delta++)
            {
                var totalGrid = boundary + delta;
                if (0 <= totalGrid && totalGrid <= maxTotalGrid)
                    samples.Add(totalGrid);
            }
        }

        private static int CountVisible(
            VisibilityCase visibilityCase,
            int[] noteGrids,
            List<SoflanList.VisibleTimeSpanRange> ranges)
        {
            var count = 0;
            for (var i = 0; i < noteGrids.Length; i++)
            {
                if (Contains(ranges, TotalGridToAudioTime(visibilityCase, noteGrids[i])))
                    count++;
            }
            return count;
        }

        private static int CountVisible(
            int[] noteGrids,
            List<SoflanList.VisibleTotalGridRange> ranges)
        {
            var count = 0;
            for (var i = 0; i < noteGrids.Length; i++)
            {
                if (Contains(ranges, noteGrids[i]))
                    count++;
            }
            return count;
        }

        private static bool Contains(List<SoflanList.VisibleTimeSpanRange> ranges, TimeSpan audioTime)
        {
            for (var i = 0; i < ranges.Count; i++)
            {
                if (ranges[i].Contain(audioTime))
                    return true;
            }
            return false;
        }

        private static bool Contains(
            List<SoflanList.VisibleTotalGridRange> ranges,
            int totalGrid)
        {
            for (var i = 0; i < ranges.Count; i++)
            {
                if (ranges[i].Contain(totalGrid))
                    return true;
            }
            return false;
        }

        private static double AudioMsecToY(VisibilityCase visibilityCase, double msec)
        {
            return TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                MathUtils.FromMillisecondsExact(msec),
                visibilityCase.SoflanList,
                visibilityCase.BpmList,
                1.0);
        }

        private static int AudioTimeToTotalGrid(VisibilityCase visibilityCase, TimeSpan audioTime)
        {
            var tGrid = TGridCalculator.ConvertAudioTimeToTGrid(
                audioTime < TimeSpan.Zero ? TimeSpan.Zero : audioTime,
                visibilityCase.BpmList);
            return tGrid == null ? 0 : tGrid.TotalGrid;
        }

        private static TimeSpan TotalGridToAudioTime(
            VisibilityCase visibilityCase,
            int totalGrid)
        {
            return TGridCalculator.ConvertTGridToAudioTime(
                TGrid.FromTotalGrid(totalGrid),
                visibilityCase.BpmList);
        }

        private static double ChartEndMsec(VisibilityCase visibilityCase)
        {
            return TotalGridToAudioTime(visibilityCase, visibilityCase.MaxTotalGrid).TotalMilliseconds
                + visibilityCase.ViewHeight
                + 1000.0;
        }

        private static int StableSeed(string value)
        {
            unchecked
            {
                var hash = 17;
                for (var i = 0; i < value.Length; i++)
                    hash = hash * 31 + value[i];
                return hash;
            }
        }

        private static string Format(List<SoflanList.VisibleTimeSpanRange> ranges)
        {
            var result = string.Empty;
            for (var i = 0; i < ranges.Count; i++)
            {
                if (i > 0)
                    result += ",";
                result += "[" + ranges[i].MinAudioTime.Ticks
                    + "," + ranges[i].MaxAudioTime.Ticks + "]";
            }
            return result;
        }

        private static string Format(List<SoflanList.VisibleTotalGridRange> ranges)
        {
            var result = string.Empty;
            for (var i = 0; i < ranges.Count; i++)
            {
                if (i > 0)
                    result += ",";
                result += "[" + ranges[i].MinTotalGrid
                    + "," + ranges[i].MaxTotalGrid + "]";
            }
            return result;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
