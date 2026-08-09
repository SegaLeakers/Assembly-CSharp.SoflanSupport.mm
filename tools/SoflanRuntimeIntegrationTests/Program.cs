using System;
using System.Collections.Generic;
using System.IO;
using OngekiFumenEditor.Core.Base.Collections;
using SoflanSupport;

namespace SoflanRuntimeIntegrationTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length != 1)
                    throw new ArgumentException("usage: SoflanRuntimeIntegrationTests <chart.ma2>");

                var chartPath = Path.GetFullPath(args[0]);
                var snapshot = OriginalNotesReaderRuntime.LoadChart(
                    chartPath,
                    playerId: 0,
                    adjustTimingId: 20);

                Require(snapshot.RuntimeChartOffsetMsec > 0f,
                    "original UserOption.GetAdjustMSec() was not captured");
                Require(snapshot.Notes.Count > 0,
                    "original NotesReader returned no notes");

                var timingDiff = OriginalFrameworkDifferential.Compare(snapshot);
                Require(timingDiff.ComparedNoteTimes == snapshot.Notes.Count,
                    "not every original NoteData.time was compared");
                Require(timingDiff.VisibilityFallbacks == 0,
                    "complete chart produced visibility TGrid fallbacks: "
                    + timingDiff.VisibilityFallbacks);
                Require(timingDiff.MaxNoteTimeDeltaMsec <= 0.51f,
                    "original/framework NoteData.time delta exceeded 0.51ms: "
                    + timingDiff.MaxNoteTimeDeltaMsec.ToString("R"));
                Require(timingDiff.ComparedEndTimes > 0,
                    "original NotesReader returned no comparable NoteData.end values");
                Require(timingDiff.MaxEndTimeDeltaMsec <= 0.51f,
                    "original/framework NoteData.end delta exceeded 0.51ms: "
                    + timingDiff.MaxEndTimeDeltaMsec.ToString("R"));

                var soflanMap = new SoflanListMap();
                var compositionLoad = SoflanCompositionParser.LoadLines(
                    File.ReadLines(chartPath),
                    soflanMap);
                ReadExpectedSoflanShape(
                    chartPath,
                    out var expectedSoflanCount,
                    out var expectedGroupCount);
                Require(compositionLoad.Success,
                    "production SFL parser rejected line: " + compositionLoad.FailedLine);
                Require(compositionLoad.ParsedCount == expectedSoflanCount,
                    "SFL parser count mismatch: expected " + expectedSoflanCount
                    + ", actual " + compositionLoad.ParsedCount);
                Require(soflanMap.Count == expectedGroupCount,
                    "Soflan group count mismatch: expected " + expectedGroupCount
                    + ", actual " + soflanMap.Count);

                System.Console.WriteLine(
                    "OriginalNotesReaderIntegration: PASS "
                    + "notes=" + snapshot.Notes.Count
                    + " runtimeChartOffsetMsec=" + snapshot.RuntimeChartOffsetMsec.ToString("R")
                    + " maxNoteTimeDeltaMsec=" + timingDiff.MaxNoteTimeDeltaMsec.ToString("R")
                    + " visibilityFallbacks=" + timingDiff.VisibilityFallbacks
                    + " endTimes=" + timingDiff.ComparedEndTimes
                    + " maxEndTimeDeltaMsec=" + timingDiff.MaxEndTimeDeltaMsec.ToString("R")
                    + " sfl=" + compositionLoad.ParsedCount
                    + " groups=" + soflanMap.Count);
                return 0;
            }
            catch (Exception exception)
            {
                System.Console.Error.WriteLine("OriginalNotesReaderIntegration: FAIL");
                System.Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void ReadExpectedSoflanShape(
            string chartPath,
            out int recordCount,
            out int groupCount)
        {
            recordCount = 0;
            var groups = new HashSet<int> { 0 };
            foreach (var line in File.ReadLines(chartPath))
            {
                var fields = line.Split('\t');
                if (fields.Length == 0
                    || !string.Equals(fields[0].Trim(), "SFL", StringComparison.OrdinalIgnoreCase))
                    continue;

                recordCount++;
                if (fields.Length > 5
                    && int.TryParse(fields[5].Trim(), out var group))
                    groups.Add(group);
            }

            groupCount = groups.Count;
        }
    }
}
