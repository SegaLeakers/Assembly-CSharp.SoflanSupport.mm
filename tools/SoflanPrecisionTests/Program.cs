using System;
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.OngekiObjects;
using OngekiFumenEditor.Core.Utils;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;
using SoflanSupport;

namespace SoflanPrecisionTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var runtimeLabel = args.Length > 0 ? args[0] : "unknown";
                Console.WriteLine(
                    "runtime=" + runtimeLabel
                    + " version=" + Environment.Version
                    + " executable=" + Environment.GetCommandLineArgs()[0]);

                LargeAbsolutePositionsPreserveLocalDelta();
                FloatBoundaryPreservesSubMillisecondTicks();
                FractionalBpmTimingPointPreservesTicks();
                ReverseConversionUsesTickInputsAroundBpmBoundary();
                DirectTotalGridConversionPreservesRounding();
                AudioTimeToYPreservesSubGridPrecision();
                AudioTimeToYUsesActiveBpmSegment();
                RawChartTimeUsesTimeSpanArithmetic();
                InvalidPositionsAreRejected();

                Console.WriteLine("SoflanPrecisionTests: PASS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("SoflanPrecisionTests: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void LargeAbsolutePositionsPreserveLocalDelta()
        {
            const double baseline = 100000000.0;
            const double localDelta = 0.5;

            var oldFloatDelta = (float)(baseline + localDelta) - (float)baseline;
            Require(oldFloatDelta == 0f,
                "test precondition failed: float absolute subtraction unexpectedly retained delta");

            var note = new SoflanPosition(baseline + localDelta);
            var current = new SoflanPosition(baseline);
            Require(note.DeltaTo(current) == localDelta,
                "SoflanPosition lost the local double delta");
            Require(note.ToGameFloatDelta(current) == 0.5f,
                "final local float conversion did not preserve 0.5");
        }

        private static void FloatBoundaryPreservesSubMillisecondTicks()
        {
            const float gameMsec = 123.456f;
            var expectedTicks = (long)Math.Round(
                (double)gameMsec * TimeSpan.TicksPerMillisecond,
                MidpointRounding.AwayFromZero);
            var actual = SoflanRuntimeTime.FromGameMsecBoundary(gameMsec);
            Require(actual.Ticks == expectedTicks,
                "float boundary conversion lost sub-millisecond ticks: expected="
                + expectedTicks + " actual=" + actual.Ticks);
        }

        private static void RawChartTimeUsesTimeSpanArithmetic()
        {
            var runtime = TimeSpan.FromTicks(1234567);
            var chartOffset = TimeSpan.FromTicks(600000);
            var visualOffset = TimeSpan.FromTicks(-1234);
            var raw = SoflanRuntimeTime.ToRawChartAudioTime(
                runtime,
                chartOffset,
                visualOffset);
            Require(raw.Ticks == 633333,
                "raw chart time arithmetic changed: " + raw.Ticks);

            Require(
                SoflanRuntimeTime.ToRawChartAudioTime(
                    TimeSpan.FromTicks(1),
                    TimeSpan.FromTicks(2),
                    TimeSpan.Zero) == TimeSpan.Zero,
                "negative raw chart time was not clamped to zero");
        }

        private static void FractionalBpmTimingPointPreservesTicks()
        {
            var bpmList = BuildBpmList(
                new BPMChange { TGrid = TGrid.FromTotalGrid(1), BPM = 120 });
            var points = bpmList.GetCachedAllBpmUniformPositionList();
            var expectedMsec = BpmMathUtils.CalculateBPMLength(0, 1, 240);
            var expectedTicks = (long)Math.Round(
                expectedMsec * TimeSpan.TicksPerMillisecond,
                MidpointRounding.AwayFromZero);

            Require(points.Count == 2, "fractional BPM timing point was not cached");
            Require(points[1].AudioTime.Ticks == expectedTicks,
                "BPM cache quantized a fractional-grid duration: expected="
                + expectedTicks + " actual=" + points[1].AudioTime.Ticks);

            var converted = TGridCalculator.ConvertTGridToAudioTime(
                TGrid.FromTotalGrid(1), bpmList);
            Require(converted.Ticks == expectedTicks,
                "TGrid -> TimeSpan lost fractional-grid ticks: expected="
                + expectedTicks + " actual=" + converted.Ticks);
        }

        private static void ReverseConversionUsesTickInputsAroundBpmBoundary()
        {
            var bpmList = BuildBpmList(
                new BPMChange { TGrid = TGrid.FromTotalGrid(1), BPM = 120 });
            var boundary = BpmMathUtils.CalculateBPMLength(0, 1, 240);
            var boundaryTicks = (long)Math.Round(
                boundary * TimeSpan.TicksPerMillisecond,
                MidpointRounding.AwayFromZero);
            var nextGrid = BpmMathUtils.CalculateBPMLength(1, 2, 120);
            var nextGridTicks = (long)Math.Round(
                nextGrid * TimeSpan.TicksPerMillisecond,
                MidpointRounding.AwayFromZero);

            var before = TGridCalculator.ConvertAudioTimeToTGrid(
                TimeSpan.FromTicks(boundaryTicks - 1), bpmList);
            var exact = TGridCalculator.ConvertAudioTimeToTGrid(
                TimeSpan.FromTicks(boundaryTicks), bpmList);
            var after = TGridCalculator.ConvertAudioTimeToTGrid(
                TimeSpan.FromTicks(boundaryTicks + nextGridTicks), bpmList);

            Require(before.TotalGrid == 1,
                "tick input immediately before BPM boundary changed grid: " + before.TotalGrid);
            Require(exact.TotalGrid == 1,
                "tick input at BPM boundary changed grid: " + exact.TotalGrid);
            Require(after.TotalGrid == 2,
                "tick input after BPM boundary used the wrong BPM segment: " + after.TotalGrid);
        }

        private static void DirectTotalGridConversionPreservesRounding()
        {
            const double testBpm = 250;
            var halfGridMsec = BpmMathUtils.CalculateBPMLength(0, 1, testBpm) / 2;
            var halfGridTicks = (long)Math.Round(
                halfGridMsec * TimeSpan.TicksPerMillisecond,
                MidpointRounding.AwayFromZero);

            Require(BpmMathUtils.CalculateTotalGridOffset(
                TimeSpan.FromTicks(halfGridTicks - 1), testBpm) == 0,
                "below midpoint rounded to the next TotalGrid");
            Require(BpmMathUtils.CalculateTotalGridOffset(
                TimeSpan.FromTicks(halfGridTicks), testBpm) == 0,
                "midpoint rounding changed from the existing ToEven rule");
            Require(BpmMathUtils.CalculateTotalGridOffset(
                TimeSpan.FromTicks(halfGridTicks + 1), testBpm) == 1,
                "above midpoint did not round to the next TotalGrid");
            Require(BpmMathUtils.CalculateTotalGridOffset(
                TimeSpan.FromTicks(-halfGridTicks - 1), testBpm) == -1,
                "negative TotalGrid rounding changed unexpectedly");
        }

        private static void AudioTimeToYPreservesSubGridPrecision()
        {
            const double bpm = 217;
            const float speed = 999f;
            var bpmList = new BpmList { FirstBpm = bpm };
            var soflanList = new SoflanList(new ISoflan[]
            {
                new Soflan
                {
                    TGrid = TGrid.Zero,
                    EndTGrid = TGrid.FromTotalGrid(8),
                    Speed = speed
                }
            });
            var quarterGridTicks = (long)Math.Round(
                BpmMathUtils.CalculateBPMLength(0, 1, bpm)
                * TimeSpan.TicksPerMillisecond / 4d,
                MidpointRounding.AwayFromZero);
            var audioTime = TimeSpan.FromTicks(quarterGridTicks);
            var expected = audioTime.TotalMilliseconds * speed;
            var cachedSpeed = soflanList.GetCachedSoflanPositionList_PreviewMode(bpmList)[0].Speed;

            var actual = TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                audioTime,
                soflanList,
                bpmList,
                1);
            var roundedGrid = TGridCalculator.ConvertTGridToY_PreviewMode(
                TGridCalculator.ConvertAudioTimeToTGrid(audioTime, bpmList),
                soflanList,
                bpmList,
                1);

            Require(cachedSpeed == speed,
                "test setup lost the high-speed preview value: " + cachedSpeed.ToString("R"));
            Near(actual, expected, 0.000001d,
                "continuous TimeSpan -> Y conversion lost a sub-grid position");
            Require(Math.Abs(actual - roundedGrid) > 700d,
                "high-speed sub-grid regression did not distinguish continuous and rounded paths");
        }

        private static void AudioTimeToYUsesActiveBpmSegment()
        {
            var bpmList = BuildBpmList(
                new BPMChange { TGrid = TGrid.FromTotalGrid(1), BPM = 120 });
            var soflanList = new SoflanList();
            var bpmBoundary = bpmList.GetCachedAllBpmUniformPositionList()[1].AudioTime;
            var afterBoundaryTicks = (long)Math.Round(
                BpmMathUtils.CalculateBPMLength(1, 2, 120)
                * TimeSpan.TicksPerMillisecond / 4d,
                MidpointRounding.AwayFromZero);
            var audioTime = bpmBoundary + TimeSpan.FromTicks(afterBoundaryTicks);
            var expected = BpmMathUtils.CalculateBPMLength(0, 1, 240)
                + afterBoundaryTicks / (double)TimeSpan.TicksPerMillisecond;

            var actual = TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                audioTime,
                soflanList,
                bpmList,
                1);

            Near(actual, expected, 0.0001d,
                "continuous TimeSpan -> Y conversion used the BPM before the boundary");
        }

        private static BpmList BuildBpmList(params BPMChange[] changes)
        {
            var bpmList = new BpmList();
            bpmList.FirstBpm = 240;
            foreach (var change in changes)
                bpmList.Add(change);
            return bpmList;
        }

        private static void InvalidPositionsAreRejected()
        {
            RequireThrows<ArgumentOutOfRangeException>(
                delegate { new SoflanPosition(double.NaN); },
                "NaN SoflanPosition was accepted");
            RequireThrows<ArgumentOutOfRangeException>(
                delegate { new SoflanPosition(double.PositiveInfinity); },
                "infinite SoflanPosition was accepted");
        }

        private static void RequireThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void Near(double actual, double expected, double epsilon, string message)
        {
            Require(Math.Abs(actual - expected) <= epsilon,
                message + ": expected=" + expected.ToString("R")
                + " actual=" + actual.ToString("R"));
        }
    }
}
