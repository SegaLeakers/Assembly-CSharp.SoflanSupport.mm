// SoflanSupport.SoflanManager — 新增类型，管理按 player/monitor 隔离的 Soflan 运行时状态。
// SimpleSoflanFramework.Core 源码由 Shared Project 内嵌进 .mm.dll，运行时无需外部 Core DLL。
// MA2/BPM/SFL 使用原始谱面时间轴；运行时当前时间统一通过 t - GetAdjustMSec + visualOffset 转换。
using Manager;
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.OngekiObjects;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SoflanSupport
{
    public class SoflanManager
    {
        private readonly Dictionary<int, PlayerSoflanState> playerStateMap = new();

        private sealed class PlayerSoflanState
        {
            public readonly int PlayerId;
            public SoflanListMap SoflanListMap = new();
            public BpmList BpmList = new BpmList();
            public bool ContainSoflans;
            public TimeSpan RuntimeChartOffset;
            public readonly Dictionary<int, int> NoteIndexToSoflanGroupMap = new();
            public readonly Dictionary<int, TGrid> NoteIndexToSoflanTGridMap = new();
            public readonly Dictionary<int, TGrid> NoteIndexToSoflanEndTGridMap = new();

            public TimeSpan CachedCalculatedCurrentTime;
            public TimeSpan CachedCalculatedAppearTime;
            public bool HasCalculatedVisibleFrame;
            public int VisibleRangeCacheVersion;
            public readonly Dictionary<int, VisibleTotalGridRangeCache> VisibleRangeListMap = new();
            public readonly Dictionary<int, FallbackVisibleTimeRangeCache> FallbackVisibleRangeListMap = new();
            public long VisibilityFallbackCount;
            public TimeSpan CachedRuntimeCurrentTime;
            public bool HasCachedRuntimeCurrentTime;
            public readonly Dictionary<CurrentSoflanPositionCacheKey, SoflanPosition> CachedCurrentSoflanPositionMap = new();

            public PlayerSoflanState(int playerId)
            {
                PlayerId = playerId;
            }

            public void ResetComposition(TimeSpan runtimeChartOffset)
            {
                SoflanListMap = new SoflanListMap();
                BpmList = new BpmList();
                ContainSoflans = false;
                RuntimeChartOffset = runtimeChartOffset;
                ResetCaches();
            }

            public void ResetCaches()
            {
                CachedCalculatedCurrentTime = default;
                CachedCalculatedAppearTime = default;
                HasCalculatedVisibleFrame = false;
                VisibleRangeCacheVersion = 0;
                VisibleRangeListMap.Clear();
                FallbackVisibleRangeListMap.Clear();
                VisibilityFallbackCount = 0;
                CachedRuntimeCurrentTime = default;
                HasCachedRuntimeCurrentTime = false;
                CachedCurrentSoflanPositionMap.Clear();
            }
        }

        private PlayerSoflanState GetOrCreatePlayerState(int playerId)
        {
            if (!playerStateMap.TryGetValue(playerId, out var state))
            {
                state = new PlayerSoflanState(playerId);
                playerStateMap[playerId] = state;
            }

            return state;
        }

        private bool TryGetPlayerState(int playerId, out PlayerSoflanState state)
        {
            return playerStateMap.TryGetValue(playerId, out state);
        }

        /// <summary>
        /// clear all
        /// </summary>
        public void clearAll()
        {
            playerStateMap.Clear();

            PatchLog.WriteLine("SoflanManager cleared");
        }

        public void clearPlayer(int playerId)
        {
            playerStateMap.Remove(playerId);
            PatchLog.WriteLine($"SoflanManager player {playerId} cleared");
        }

        public void loadNote(NoteData noteData, MA2Record record, NotesReader sr, int playerId)
        {
            if (noteData == null)
                return;

            var state = GetOrCreatePlayerState(playerId);

            // A chart reload can reuse a player state. Remove old index entries before
            // attempting to decode this note so a failed/missing decode cannot reuse a
            // TGrid or group from a previous chart.
            state.NoteIndexToSoflanGroupMap.Remove(noteData.indexNote);
            state.NoteIndexToSoflanTGridMap.Remove(noteData.indexNote);
            state.NoteIndexToSoflanEndTGridMap.Remove(noteData.indexNote);

            var fixedNoteData = (patch_NoteData)noteData;
            fixedNoteData.isFixedSoflanToUnifiedSpeed = false;
            fixedNoteData.fixedSoflanUnifiedSpeed = FixedSoflan.DefaultUnifiedSpeed;

            if (TryReadNotesTimeTGrid(noteData.time, sr, out var noteTGrid))
                state.NoteIndexToSoflanTGridMap[noteData.indexNote] = noteTGrid;
            if (HasMeaningfulEndTime(noteData) && TryReadNotesTimeTGrid(noteData.end, sr, out var noteEndTGrid))
                state.NoteIndexToSoflanEndTGridMap[noteData.indexNote] = noteEndTGrid;

            SoflanMarkerParseResult marker;
            string markerReason;
            if (!SoflanMarkerParser.TryParse(record?._str, out marker, out markerReason))
                FailSoflanMarker(noteData, marker.Marker, markerReason);

            if (!marker.HasMarker)
            {
                SoflanDiagnostic.NoteLoaded(
                    playerId,
                    noteData,
                    0,
                    false,
                    FixedSoflan.DefaultUnifiedSpeed,
                    marker.Marker);
                return;
            }

            var soflanGroup = marker.Group;
            var isFixedSoflan = marker.IsFixedSoflan;
            var fixedSoflanUnifiedSpeed = marker.HasFixedSpeed
                ? marker.FixedSpeed
                : FixedSoflan.DefaultUnifiedSpeed;

            state.NoteIndexToSoflanGroupMap[noteData.indexNote] = soflanGroup;
            fixedNoteData.isFixedSoflanToUnifiedSpeed = isFixedSoflan;
            fixedNoteData.fixedSoflanUnifiedSpeed = fixedSoflanUnifiedSpeed;

            PatchLog.WriteLine(
                $"register player:{playerId}, noteIndex:{noteData.indexNote}, marker:{marker.Marker}, soflanGroup:{soflanGroup}, fixedSoflan:{isFixedSoflan}, fixedSoflanSpeed:{fixedSoflanUnifiedSpeed.ToString(CultureInfo.InvariantCulture)}");
            SoflanDiagnostic.NoteLoaded(
                playerId,
                noteData,
                soflanGroup,
                isFixedSoflan,
                fixedSoflanUnifiedSpeed,
                marker.Marker);
        }

        private static bool TryReadNotesTimeTGrid(NotesTime notesTime, NotesReader sr, out TGrid tGrid)
        {
            tGrid = default;
            if (sr == null)
                return false;

            try
            {
                tGrid = notesTime.ToTGrid(sr);
                return tGrid != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasMeaningfulEndTime(NoteData noteData)
        {
            return noteData.end.grid != 0 || noteData.end.msec != 0f;
        }

        private static void FailSoflanMarker(NoteData noteData, string marker, string reason)
        {
            var message = $"register noteIndex:{noteData.indexNote} failed, marker:{marker}, reason:{reason}";
            PatchLog.Error(message);
            throw new FormatException(message);
        }

        public void loadComposition(
            MA2RecordList records,
            NotesReader sr,
            int playerId,
            TimeSpan runtimeChartOffset)
        {
            var state = GetOrCreatePlayerState(playerId);
            state.ResetComposition(runtimeChartOffset);

            var filePath = sr.GetHeader()._notesName;
            if (!TryOpenCompositionReader(filePath, out var reader))
            {
                SoflanDiagnostic.CompositionLoaded(
                    playerId,
                    filePath,
                    false,
                    state.RuntimeChartOffset);
                return;
            }

            using (reader)
            {
                var loadResult = SoflanCompositionParser.Load(
                    reader,
                    state.SoflanListMap,
                    (line, soflan) =>
                    {
                        PatchLog.WriteLine($"parse soflan: {soflan}");
                        SoflanDiagnostic.SoflanLineLoaded(playerId, line);
                    });
                state.ContainSoflans = loadResult.ParsedCount > 0;
                if (!loadResult.Success)
                {
                    PatchLog.Error(
                        $"parse soflan failed, line content:{loadResult.FailedLine}");
                }
            }

            foreach (var item in sr.GetCompositioin()._bpmList)
            {
                if (item.time.grid == 0)
                {
                    state.BpmList.FirstBpm = item.bpm;
                }
                else
                {
                    var bpmChange = new BPMChange
                    {
                        BPM = item.bpm,
                        TGrid = item.time.ToTGrid(sr)
                    };

                    state.BpmList.Add(bpmChange);
                }
            }

            PatchLog.WriteLine($"-------DUMP SOFLAN TIMING POINTS-------");
            PatchLog.WriteLine($"PlayerId: {playerId}");
            PatchLog.WriteLine($"RuntimeChartOffset: {state.RuntimeChartOffset.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}ms");
            PatchLog.WriteLine($"FilePath: {sr.GetHeader()._notesName}");
            foreach (KeyValuePair<int, SoflanList> pair in state.SoflanListMap)
            {
                var soflanGroup = pair.Key;
                var soflanList = pair.Value;

                PatchLog.WriteLine($"");
                PatchLog.WriteLine($"SoflanGroup: {soflanGroup}");
                foreach (var timingPoint in soflanList.GetCachedSoflanPositionList_PreviewMode(state.BpmList))
                    PatchLog.WriteLine($"\t\t * AudioTime:{TGridCalculator.ConvertTGridToAudioTime(timingPoint.TGrid, state.BpmList).TotalMilliseconds}ms {timingPoint}");
            }

            PatchLog.WriteLine($"---------------------------------------");
            SoflanDiagnostic.CompositionLoaded(
                playerId,
                filePath,
                state.ContainSoflans,
                state.RuntimeChartOffset);
        }

        private static bool TryOpenCompositionReader(string filePath, out TextReader reader)
        {
            reader = null;
            try
            {
                if (!LCPackage.Manager.FileExist(filePath, SearchOption.TopDirectoryOnly))
                    return false;

                reader = LCPackage.Manager.OpenText(filePath);
                return reader != null;
            }
            catch (EntryPointNotFoundException)
            {
                return TryOpenLocalCompositionReader(filePath, out reader);
            }
            catch (DllNotFoundException)
            {
                return TryOpenLocalCompositionReader(filePath, out reader);
            }
        }

        private static bool TryOpenLocalCompositionReader(string filePath, out TextReader reader)
        {
            if (File.Exists(filePath))
            {
                reader = File.OpenText(filePath);
                return true;
            }
            reader = null;
            return false;
        }

        public bool containsSoflans(int playerId)
        {
            return TryGetPlayerState(playerId, out var state) && state.ContainSoflans;
        }

        public TimeSpan getRuntimeChartOffset(int playerId)
        {
            return TryGetPlayerState(playerId, out var state)
                ? state.RuntimeChartOffset
                : TimeSpan.Zero;
        }

        public static bool IsSupportedVisualSoflanKind(NotesTypeID.Def noteKind)
        {
            switch (noteKind)
            {
                case NotesTypeID.Def.Begin:
                case NotesTypeID.Def.Break:
                case NotesTypeID.Def.ExTap:
                case NotesTypeID.Def.Star:
                case NotesTypeID.Def.BreakStar:
                case NotesTypeID.Def.ExStar:
                case NotesTypeID.Def.TouchTap:
                case NotesTypeID.Def.ExBreakTap:
                case NotesTypeID.Def.ExBreakStar:
                case NotesTypeID.Def.Hold:
                case NotesTypeID.Def.ExHold:
                case NotesTypeID.Def.BreakHold:
                case NotesTypeID.Def.ExBreakHold:
                    return true;
                default:
                    return false;
            }
        }

        public SoflanList getSoflanList(int playerId, int soflanGroup)
        {
            return GetOrCreatePlayerState(playerId).SoflanListMap[soflanGroup];
        }

        //-------------------------------------------

        private sealed class VisibleTotalGridRangeCache
        {
            public int Version;
            public SoflanPosition CurrentSoflanPosition;
            public TimeSpan AppearTime;
            public readonly List<SoflanList.VisibleTotalGridRange> Ranges = new List<SoflanList.VisibleTotalGridRange>();
            public readonly SoflanList.VisibleRangeQueryScratch VisibleRangeScratch = new SoflanList.VisibleRangeQueryScratch();
        }

        private sealed class FallbackVisibleTimeRangeCache
        {
            public int Version;
            public SoflanPosition CurrentSoflanPosition;
            public TimeSpan AppearTime;
            public readonly List<SoflanList.VisibleTimeSpanRange> Ranges = new List<SoflanList.VisibleTimeSpanRange>();
            public readonly SoflanList.VisibleRangeQueryScratch VisibleRangeScratch = new SoflanList.VisibleRangeQueryScratch();
        }

        private struct CurrentSoflanPositionCacheKey : IEquatable<CurrentSoflanPositionCacheKey>
        {
            public readonly int PlayerId;
            public readonly int Group;
            public readonly TimeSpan RuntimeChartOffset;
            public readonly TimeSpan VisualAudioOffset;

            public CurrentSoflanPositionCacheKey(
                int playerId,
                int group,
                TimeSpan runtimeChartOffset,
                TimeSpan visualAudioOffset)
            {
                PlayerId = playerId;
                Group = group;
                RuntimeChartOffset = runtimeChartOffset;
                VisualAudioOffset = visualAudioOffset;
            }

            public bool Equals(CurrentSoflanPositionCacheKey other)
            {
                return PlayerId == other.PlayerId
                    && Group == other.Group
                    && RuntimeChartOffset.Equals(other.RuntimeChartOffset)
                    && VisualAudioOffset.Equals(other.VisualAudioOffset);
            }

            public override bool Equals(object obj)
            {
                return obj is CurrentSoflanPositionCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = PlayerId;
                    hashCode = (hashCode * 397) ^ Group;
                    hashCode = (hashCode * 397) ^ RuntimeChartOffset.GetHashCode();
                    hashCode = (hashCode * 397) ^ VisualAudioOffset.GetHashCode();
                    return hashCode;
                }
            }
        }

        public bool checkNoteVisible(
            int playerId,
            NoteData noteData,
            TimeSpan currentTime,
            TimeSpan appearTime)
        {
            if (noteData == null)
                return false;

            var soflanGroup = getNoteSoflanGroup(playerId, noteData);
            var maiBugAdjust = SoflanVisualTiming.GetMaiBugAdjust(
                noteData.type.getEnum(),
                appearTime);
            var currentSoflanPosition = GetCurrentSoflanPositionWithOffsetsCached(
                playerId,
                currentTime,
                maiBugAdjust,
                soflanGroup);
            return checkNoteVisible(
                playerId,
                noteData,
                currentTime,
                appearTime,
                soflanGroup,
                currentSoflanPosition);
        }

        public bool checkNoteVisible(
            int playerId,
            NoteData noteData,
            TimeSpan currentTime,
            TimeSpan appearTime,
            int soflanGroup,
            SoflanPosition currentSoflanPosition)
        {
            if (noteData == null || !TryGetPlayerState(playerId, out var state))
                return false;

            BeginVisibleRangeFrame(state, currentTime, appearTime);

            var visibleRangeList = GetVisibleTotalGridRangeList(
                state,
                soflanGroup,
                currentSoflanPosition,
                appearTime);
            if (visibleRangeList == null)
                return false;

            if (TryGetNoteTotalGrid(state, noteData.indexNote, out var noteTotalGrid, out var fallbackReason))
            {
                // foreach avoids LINQ delegate/iterator allocations in the per-note hot path.
                foreach (var range in visibleRangeList)
                {
                    if (range.Contain(noteTotalGrid))
                        return true;
                }
                return false;
            }

            return CheckNoteVisibleByAudioTimeFallback(
                state,
                noteData,
                currentTime,
                appearTime,
                soflanGroup,
                currentSoflanPosition,
                fallbackReason);
        }

        private static bool TryGetNoteTotalGrid(
            PlayerSoflanState state,
            int noteIndex,
            out int totalGrid,
            out string fallbackReason)
        {
            totalGrid = 0;
            if (!state.NoteIndexToSoflanTGridMap.TryGetValue(noteIndex, out var tGrid)
                || tGrid == null)
            {
                fallbackReason = "missing_tgrid";
                return false;
            }

            try
            {
                totalGrid = tGrid.TotalGrid;
                fallbackReason = null;
                return true;
            }
            catch
            {
                fallbackReason = "invalid_tgrid";
                return false;
            }
        }

        private bool CheckNoteVisibleByAudioTimeFallback(
            PlayerSoflanState state,
            NoteData noteData,
            TimeSpan currentTime,
            TimeSpan appearTime,
            int soflanGroup,
            SoflanPosition currentSoflanPosition,
            string reason)
        {
            if (state.VisibilityFallbackCount < long.MaxValue)
                state.VisibilityFallbackCount++;

            SoflanDiagnostic.VisibilityTGridFallback(
                state.PlayerId,
                noteData,
                currentTime,
                appearTime,
                state.VisibilityFallbackCount,
                reason);

            var fallbackRanges = GetFallbackVisibleTimeRangeList(
                state,
                soflanGroup,
                currentSoflanPosition,
                appearTime);
            var noteTime = GetNoteAudioTimeForSoflan(state.PlayerId, noteData);
            foreach (var range in fallbackRanges)
            {
                if (range.Contain(noteTime))
                    return true;
            }
            return false;
        }

        public int getNoteSoflanGroup(int playerId, int noteIndex)
        {
            return TryGetPlayerState(playerId, out var state)
                && state.NoteIndexToSoflanGroupMap.TryGetValue(noteIndex, out var soflanGroup)
                ? soflanGroup
                : 0;
        }

        public int getNoteSoflanGroup(int playerId, NoteData noteData)
        {
            return noteData == null ? 0 : getNoteSoflanGroup(playerId, noteData.indexNote);
        }

        public TimeSpan GetNoteAudioTimeForSoflan(int playerId, NoteData noteData)
        {
            return noteData == null
                ? TimeSpan.Zero
                : GetNoteAudioTimeForSoflan(
                    playerId,
                    noteData.indexNote,
                    SoflanRuntimeTime.FromGameMsecBoundary(noteData.time.msec));
        }

        public TimeSpan GetNoteAudioTimeForSoflan(
            int playerId,
            int noteIndex,
            TimeSpan fallbackRuntimeTime)
        {
            if (!TryGetPlayerState(playerId, out var state))
                return fallbackRuntimeTime;
            if (!state.NoteIndexToSoflanTGridMap.TryGetValue(noteIndex, out var tGrid))
                return SoflanRuntimeTime.ToRawChartAudioTime(
                    fallbackRuntimeTime,
                    state.RuntimeChartOffset,
                    TimeSpan.Zero);

            try
            {
                return TGridCalculator.ConvertTGridToAudioTime(tGrid, state.BpmList);
            }
            catch
            {
                return SoflanRuntimeTime.ToRawChartAudioTime(
                    fallbackRuntimeTime,
                    state.RuntimeChartOffset,
                    TimeSpan.Zero);
            }
        }

        public TimeSpan GetNoteEndAudioTimeForSoflan(int playerId, NoteData noteData)
        {
            return noteData == null
                ? TimeSpan.Zero
                : GetNoteEndAudioTimeForSoflan(
                    playerId,
                    noteData.indexNote,
                    SoflanRuntimeTime.FromGameMsecBoundary(noteData.end.msec));
        }

        public TimeSpan GetNoteEndAudioTimeForSoflan(
            int playerId,
            int noteIndex,
            TimeSpan fallbackRuntimeTime)
        {
            if (!TryGetPlayerState(playerId, out var state))
                return fallbackRuntimeTime;
            if (!state.NoteIndexToSoflanEndTGridMap.TryGetValue(noteIndex, out var tGrid))
                return SoflanRuntimeTime.ToRawChartAudioTime(
                    fallbackRuntimeTime,
                    state.RuntimeChartOffset,
                    TimeSpan.Zero);

            try
            {
                return TGridCalculator.ConvertTGridToAudioTime(tGrid, state.BpmList);
            }
            catch
            {
                return SoflanRuntimeTime.ToRawChartAudioTime(
                    fallbackRuntimeTime,
                    state.RuntimeChartOffset,
                    TimeSpan.Zero);
            }
        }

        private static void BeginVisibleRangeFrame(
            PlayerSoflanState state,
            TimeSpan currentTime,
            TimeSpan appearTime)
        {
            if (state.HasCalculatedVisibleFrame
                && state.CachedCalculatedCurrentTime == currentTime
                && state.CachedCalculatedAppearTime == appearTime)
                return;

            state.CachedCalculatedCurrentTime = currentTime;
            state.CachedCalculatedAppearTime = appearTime;
            state.HasCalculatedVisibleFrame = true;

            if (state.VisibleRangeCacheVersion == int.MaxValue)
            {
                state.VisibleRangeListMap.Clear();
                state.FallbackVisibleRangeListMap.Clear();
                state.VisibleRangeCacheVersion = 1;
            }
            else
            {
                state.VisibleRangeCacheVersion++;
            }
        }

        private List<SoflanList.VisibleTotalGridRange> GetVisibleTotalGridRangeList(
            PlayerSoflanState state,
            int soflanGroup,
            SoflanPosition currentSoflanPosition,
            TimeSpan appearTime)
        {
            if (!state.VisibleRangeListMap.TryGetValue(soflanGroup, out var cache))
            {
                cache = new VisibleTotalGridRangeCache();
                state.VisibleRangeListMap[soflanGroup] = cache;
            }

            if (cache.Version == state.VisibleRangeCacheVersion
                && cache.CurrentSoflanPosition == currentSoflanPosition
                && cache.AppearTime == appearTime)
                return cache.Ranges;

            cache.Ranges.Clear();

            // Lazy per-group rebuild: only groups touched by notes in this frame are recalculated.
            var soflanList = state.SoflanListMap[soflanGroup];
            soflanList.FillVisibleTotalGridRangesForGamePreview(
                currentSoflanPosition.Value,
                appearTime.TotalMilliseconds,
                state.BpmList,
                cache.Ranges,
                cache.VisibleRangeScratch);

            cache.Version = state.VisibleRangeCacheVersion;
            cache.CurrentSoflanPosition = currentSoflanPosition;
            cache.AppearTime = appearTime;
            return cache.Ranges;
        }

        private List<SoflanList.VisibleTimeSpanRange> GetFallbackVisibleTimeRangeList(
            PlayerSoflanState state,
            int soflanGroup,
            SoflanPosition currentSoflanPosition,
            TimeSpan appearTime)
        {
            if (!state.FallbackVisibleRangeListMap.TryGetValue(soflanGroup, out var cache))
            {
                cache = new FallbackVisibleTimeRangeCache();
                state.FallbackVisibleRangeListMap[soflanGroup] = cache;
            }

            if (cache.Version == state.VisibleRangeCacheVersion
                && cache.CurrentSoflanPosition == currentSoflanPosition
                && cache.AppearTime == appearTime)
                return cache.Ranges;

            var soflanList = state.SoflanListMap[soflanGroup];
            soflanList.FillVisibleTimeSpanRangesForGamePreview(
                currentSoflanPosition.Value,
                appearTime.TotalMilliseconds,
                state.BpmList,
                cache.Ranges,
                cache.VisibleRangeScratch);

            cache.Version = state.VisibleRangeCacheVersion;
            cache.CurrentSoflanPosition = currentSoflanPosition;
            cache.AppearTime = appearTime;
            return cache.Ranges;
        }

        public long GetVisibilityFallbackCount(int playerId)
        {
            return TryGetPlayerState(playerId, out var state)
                ? state.VisibilityFallbackCount
                : 0;
        }

        public SoflanPosition ConvertAudioTimeToSoflanPosition(
            int playerId,
            TimeSpan audioTime,
            int soflanGroup)
        {
            var state = GetOrCreatePlayerState(playerId);
            return new SoflanPosition(TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                audioTime,
                state.SoflanListMap[soflanGroup],
                state.BpmList,
                1));
        }

        public SoflanPosition GetNoteSoflanPosition(
            int playerId,
            int noteIndex,
            TimeSpan fallbackRuntimeTime,
            int soflanGroup)
        {
            if (!TryGetPlayerState(playerId, out var state))
                return new SoflanPosition(fallbackRuntimeTime.TotalMilliseconds);

            if (state.NoteIndexToSoflanTGridMap.TryGetValue(noteIndex, out var tGrid))
            {
                try
                {
                    return new SoflanPosition(TGridCalculator.ConvertTGridToY_PreviewMode(
                        tGrid,
                        state.SoflanListMap[soflanGroup],
                        state.BpmList,
                        1));
                }
                catch
                {
                    // Fall through to the runtime-time path when a chart cache is incomplete.
                }
            }

            return ConvertAudioTimeToSoflanPosition(
                playerId,
                SoflanRuntimeTime.ToRawChartAudioTime(
                    fallbackRuntimeTime,
                    state.RuntimeChartOffset,
                    TimeSpan.Zero),
                soflanGroup);
        }

        public SoflanPosition GetNoteEndSoflanPosition(
            int playerId,
            int noteIndex,
            TimeSpan fallbackRuntimeTime,
            int soflanGroup)
        {
            if (!TryGetPlayerState(playerId, out var state))
                return new SoflanPosition(fallbackRuntimeTime.TotalMilliseconds);

            if (state.NoteIndexToSoflanEndTGridMap.TryGetValue(noteIndex, out var tGrid))
            {
                try
                {
                    return new SoflanPosition(TGridCalculator.ConvertTGridToY_PreviewMode(
                        tGrid,
                        state.SoflanListMap[soflanGroup],
                        state.BpmList,
                        1));
                }
                catch
                {
                    // Fall through to the runtime-time path when a chart cache is incomplete.
                }
            }

            return ConvertAudioTimeToSoflanPosition(
                playerId,
                SoflanRuntimeTime.ToRawChartAudioTime(
                    fallbackRuntimeTime,
                    state.RuntimeChartOffset,
                    TimeSpan.Zero),
                soflanGroup);
        }

        public void clearCurrentSoflanPositionCache()
        {
            foreach (var state in playerStateMap.Values)
            {
                state.HasCachedRuntimeCurrentTime = false;
                state.CachedCurrentSoflanPositionMap.Clear();
            }
        }

        public void clearCurrentSoflanPositionCache(int playerId)
        {
            if (!TryGetPlayerState(playerId, out var state))
                return;

            state.HasCachedRuntimeCurrentTime = false;
            state.CachedCurrentSoflanPositionMap.Clear();
        }

        public SoflanPosition GetCurrentSoflanPositionCached(
            int playerId,
            TimeSpan currentTime,
            int soflanGroup)
        {
            return GetCurrentSoflanPositionWithOffsetsCached(
                playerId,
                currentTime,
                TimeSpan.Zero,
                soflanGroup);
        }

        public SoflanPosition GetCurrentSoflanPositionWithOffsetsCached(
            int playerId,
            TimeSpan runtimeCurrentTime,
            TimeSpan visualAudioOffset,
            int soflanGroup)
        {
            var state = GetOrCreatePlayerState(playerId);
            if (!state.HasCachedRuntimeCurrentTime
                || state.CachedRuntimeCurrentTime != runtimeCurrentTime)
            {
                state.CachedRuntimeCurrentTime = runtimeCurrentTime;
                state.HasCachedRuntimeCurrentTime = true;
                state.CachedCurrentSoflanPositionMap.Clear();
            }

            var key = new CurrentSoflanPositionCacheKey(
                playerId,
                soflanGroup,
                state.RuntimeChartOffset,
                visualAudioOffset);
            if (!state.CachedCurrentSoflanPositionMap.TryGetValue(key, out var soflanPosition))
            {
                var rawChartAudioTime = SoflanRuntimeTime.ToRawChartAudioTime(
                    runtimeCurrentTime,
                    state.RuntimeChartOffset,
                    visualAudioOffset);
                soflanPosition = ConvertAudioTimeToSoflanPosition(
                    playerId,
                    rawChartAudioTime,
                    soflanGroup);
                state.CachedCurrentSoflanPositionMap[key] = soflanPosition;
            }

            return soflanPosition;
        }

        // 调试面板用: soflan 组号 + 当前变速倍率 (值类型, 零堆分配).
        public struct GroupSpeed
        {
            public readonly int Group;
            public readonly double Speed;
            public GroupSpeed(int group, double speed) { Group = group; Speed = speed; }
        }

        // 返回指定 soflan 组在指定音频时间的当前变速倍率。无该组或无 soflan 时返回 1.0。
        public double GetCurrentSpeed(int playerId, int soflanGroup, TimeSpan runtimeAudioTime)
        {
            if (!TryGetPlayerState(playerId, out var state) || !state.ContainSoflans)
                return 1.0;
            if (!state.SoflanListMap.ContainsKey(soflanGroup))
                return 1.0;
            var rawChartAudioTime = SoflanRuntimeTime.ToRawChartAudioTime(
                runtimeAudioTime,
                state.RuntimeChartOffset,
                TimeSpan.Zero);
            var tGrid = TGridCalculator.ConvertAudioTimeToTGrid(
                rawChartAudioTime,
                state.BpmList);
            return state.SoflanListMap[soflanGroup].CalculateSpeed(state.BpmList, tGrid);
        }

        // 把所有 soflan 组的 (group, currentSpeed) 写入调用方复用的 outList (Clear 后追加), 零 List 分配。
        public void FillCurrentSpeeds(
            int playerId,
            TimeSpan runtimeAudioTime,
            List<GroupSpeed> outList,
            int maxCount = int.MaxValue)
        {
            outList.Clear();
            if (!TryGetPlayerState(playerId, out var state) || !state.ContainSoflans)
                return;
            var rawChartAudioTime = SoflanRuntimeTime.ToRawChartAudioTime(
                runtimeAudioTime,
                state.RuntimeChartOffset,
                TimeSpan.Zero);
            var tGrid = TGridCalculator.ConvertAudioTimeToTGrid(
                rawChartAudioTime,
                state.BpmList);
            foreach (KeyValuePair<int, SoflanList> pair in state.SoflanListMap)
            {
                if (outList.Count >= maxCount)
                    break;
                outList.Add(new GroupSpeed(pair.Key, pair.Value.CalculateSpeed(state.BpmList, tGrid)));
            }
        }

        public void DumpCurrent(int playerId)
        {
            if (!TryGetPlayerState(playerId, out var state))
                return;

            PatchLog.WriteLine($"-------DUMP SOFLAN TIMING POINTS-------");
            PatchLog.WriteLine($"PlayerId: {playerId}");
            PatchLog.WriteLine($"RuntimeChartOffset: {state.RuntimeChartOffset.TotalMilliseconds}ms");
            foreach (KeyValuePair<int, SoflanList> pair in state.SoflanListMap)
            {
                var soflanGroup = pair.Key;
                var soflanList = pair.Value;

                PatchLog.WriteLine($"");
                PatchLog.WriteLine($"SoflanGroup: {soflanGroup}");
                foreach (var timingPoint in soflanList.GetCachedSoflanPositionList_PreviewMode(state.BpmList))
                    PatchLog.WriteLine($"\t\t * AudioTime:{TGridCalculator.ConvertTGridToAudioTime(timingPoint.TGrid, state.BpmList).TotalMilliseconds}ms {timingPoint}");
            }
            PatchLog.WriteLine($"---------------------------------------");

            PatchLog.WriteLine($"containSoflans: {state.ContainSoflans}");
            PatchLog.WriteLine($"cachedCalculatedCurrentMsec: {state.CachedCalculatedCurrentTime.TotalMilliseconds}");
            PatchLog.WriteLine($"visibilityFallbackCount: {state.VisibilityFallbackCount}");
            PatchLog.WriteLine($"cachedVisibleTotalGridRangeListMap:");
            foreach (KeyValuePair<int, VisibleTotalGridRangeCache> pair in state.VisibleRangeListMap)
            {
                PatchLog.WriteLine($"[{pair.Key}]:");
                foreach (var visibleRange in pair.Value.Ranges)
                {
                    var rawCurrentTime = SoflanRuntimeTime.ToRawChartAudioTime(
                        state.CachedCalculatedCurrentTime,
                        state.RuntimeChartOffset,
                        TimeSpan.Zero);
                    var currentPosition = ConvertAudioTimeToSoflanPosition(
                        playerId,
                        rawCurrentTime,
                        pair.Key);
                    PatchLog.WriteLine(
                        $"\t\t{visibleRange.MinTotalGrid} ~ {visibleRange.MaxTotalGrid} totalGrid, current:{currentPosition.Value}");
                }
            }
        }
    }
}
