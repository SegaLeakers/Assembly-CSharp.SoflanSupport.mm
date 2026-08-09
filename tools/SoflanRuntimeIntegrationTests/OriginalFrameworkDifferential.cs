using System;
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.OngekiObjects;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;

namespace SoflanRuntimeIntegrationTests
{
    internal sealed class OriginalFrameworkTimingDiff
    {
        internal int ComparedNoteTimes;
        internal int ComparedEndTimes;
        internal int VisibilityFallbacks;
        internal float MaxNoteTimeDeltaMsec;
        internal float MaxEndTimeDeltaMsec;
    }

    internal static class OriginalFrameworkDifferential
    {
        internal static OriginalFrameworkTimingDiff Compare(OriginalChartSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");
            if (snapshot.Resolution != TGrid.DEFAULT_RES_T)
                throw new NotSupportedException(
                    "framework TGrid resolution mismatch: original=" + snapshot.Resolution
                    + ", framework=" + TGrid.DEFAULT_RES_T);
            if (snapshot.BpmChanges.Count == 0)
                throw new InvalidOperationException("original NotesReader returned no BPM changes");

            var bpmList = BuildBpmList(snapshot);
            var result = new OriginalFrameworkTimingDiff();

            foreach (OriginalNoteSnapshot note in snapshot.Notes)
            {
                if (!note.HasTGrid || note.TotalGrid != note.Grid)
                    result.VisibilityFallbacks++;

                float frameworkMsec = ConvertGridToAudioMsec(note.Grid, bpmList);
                float originalRawMsec = note.RuntimeMsec - snapshot.RuntimeChartOffsetMsec;
                result.MaxNoteTimeDeltaMsec = Math.Max(
                    result.MaxNoteTimeDeltaMsec,
                    Math.Abs(frameworkMsec - originalRawMsec));
                result.ComparedNoteTimes++;

                if (!IsHoldType(note.Type))
                    continue;

                float frameworkEndMsec = ConvertGridToAudioMsec(note.EndGrid, bpmList);
                float originalRawEndMsec = note.RuntimeEndMsec - snapshot.RuntimeChartOffsetMsec;
                result.MaxEndTimeDeltaMsec = Math.Max(
                    result.MaxEndTimeDeltaMsec,
                    Math.Abs(frameworkEndMsec - originalRawEndMsec));
                result.ComparedEndTimes++;
            }

            return result;
        }

        private static BpmList BuildBpmList(OriginalChartSnapshot snapshot)
        {
            var bpmList = new BpmList();
            foreach (OriginalBpmSnapshot bpm in snapshot.BpmChanges)
            {
                if (bpm.Grid == 0)
                {
                    bpmList.FirstBpm = bpm.Bpm;
                    continue;
                }

                bpmList.Add(new BPMChange
                {
                    BPM = bpm.Bpm,
                    TGrid = TGrid.FromTotalGrid(bpm.Grid)
                });
            }
            return bpmList;
        }

        private static bool IsHoldType(string type)
        {
            return type == "Hold"
                || type == "ExHold"
                || type == "BreakHold"
                || type == "ExBreakHold";
        }

        private static float ConvertGridToAudioMsec(int totalGrid, BpmList bpmList)
        {
            return (float)TGridCalculator.ConvertTGridToAudioTime(
                TGrid.FromTotalGrid(totalGrid),
                bpmList).TotalMilliseconds;
        }
    }
}
