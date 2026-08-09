using System;
using System.Collections.Generic;
using DB;
using MAI2.Util;
using Manager;
using Manager.UserDatas;
using SoflanSupport;

namespace SoflanRuntimeIntegrationTests
{
    internal sealed class OriginalNoteSnapshot
    {
        internal int Index;
        internal string Type;
        internal int Grid;
        internal float RuntimeMsec;
        internal bool HasTGrid;
        internal int TotalGrid;
        internal int EndGrid;
        internal float RuntimeEndMsec;
    }

    internal sealed class OriginalChartSnapshot
    {
        internal int Resolution;
        internal float RuntimeChartOffsetMsec;
        internal readonly List<OriginalNoteSnapshot> Notes = new List<OriginalNoteSnapshot>();
        internal readonly List<OriginalBpmSnapshot> BpmChanges = new List<OriginalBpmSnapshot>();
    }

    internal sealed class OriginalBpmSnapshot
    {
        internal int Grid;
        internal float Bpm;
    }

    internal static class OriginalNotesReaderRuntime
    {
        internal static OriginalChartSnapshot LoadChart(
            string chartPath,
            int playerId,
            int adjustTimingId)
        {
            if (string.IsNullOrEmpty(chartPath))
                throw new ArgumentException("chart path is required", "chartPath");

            var gamePlayManager = Singleton<GamePlayManager>.Instance;
            gamePlayManager.ClaerLog();
            gamePlayManager.AddPlayLog();

            var userOption = new UserOption();
            userOption.Initialize();
            userOption.AdjustTiming = (OptionJudgetimingID)adjustTimingId;

            var gameScore = gamePlayManager.GetGameScore(playerId);
            if (gameScore == null)
                throw new InvalidOperationException("failed to create original GameScoreList");
            gameScore.UserOption = userOption;

            var reader = new NotesReader();
            reader.init(playerId);
            if (!reader.load(chartPath, LoadType.LOAD_FULL))
                throw new InvalidOperationException("original NotesReader rejected chart: " + chartPath);

            var result = new OriginalChartSnapshot
            {
                Resolution = reader.getResolution(),
                RuntimeChartOffsetMsec = userOption.GetAdjustMSec()
            };

            foreach (BPMChangeData bpm in reader.GetCompositioin()._bpmList)
            {
                result.BpmChanges.Add(new OriginalBpmSnapshot
                {
                    Grid = bpm.time.grid,
                    Bpm = bpm.bpm
                });
            }

            foreach (NoteData note in reader.GetNoteList())
            {
                var noteSnapshot = new OriginalNoteSnapshot
                {
                    Index = note.indexNote,
                    Type = note.type.getEnum().ToString(),
                    Grid = note.time.grid,
                    RuntimeMsec = note.time.msec,
                    EndGrid = note.end.grid,
                    RuntimeEndMsec = note.end.msec
                };
                try
                {
                    var tGrid = note.time.ToTGrid(reader);
                    noteSnapshot.HasTGrid = tGrid != null;
                    noteSnapshot.TotalGrid = tGrid == null ? 0 : tGrid.TotalGrid;
                }
                catch
                {
                    noteSnapshot.HasTGrid = false;
                }
                result.Notes.Add(noteSnapshot);
            }

            return result;
        }
    }
}
