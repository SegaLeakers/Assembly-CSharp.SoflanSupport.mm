// Soflan 现场诊断事件。仅观察运行时状态，不参与可见性、输入消费或判定计算。
using DB;
using MAI2.Util;
using Manager;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SoflanSupport
{
    internal static class SoflanDiagnostic
    {
        private static readonly TimeSpan NearNoteTime = TimeSpan.FromMilliseconds(2500);
        private static readonly TimeSpan HeartbeatTime = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan HoldHeartbeatTime = TimeSpan.FromMilliseconds(500);
        private static readonly Dictionary<int, PlayerState> PlayerStates = new();
        private static readonly HashSet<string> LoggedErrors = new();

        internal struct JudgeProbe
        {
            public bool Active;
            public int PlayerId;
            public int NoteIndex;
            public string Source;
            public TimeSpan RuntimeTime;
            public NoteJudge.ETiming Result;
            public NoteJudge.ETiming HeadResult;
            public bool EndFlag;
        }

        private sealed class PlayerState
        {
            public bool HasSoflan;
            public string ChartPath = string.Empty;
            public TimeSpan RuntimeChartOffset;
            public TimeSpan? LastFrameTime;
            public TimeSpan? LastAnyInputTime;
            public string LastAnyInput = "none";
            public readonly bool[] ButtonPush = new bool[8];
            public readonly TimeSpan?[] ButtonDownTime = new TimeSpan?[8];
            public readonly TimeSpan?[] ButtonLastDownTime = new TimeSpan?[8];
            public readonly TimeSpan?[] ButtonLastUpTime = new TimeSpan?[8];
            public readonly bool[] TouchPush = new bool[34];
            public readonly TimeSpan?[] TouchDownTime = new TimeSpan?[34];
            public readonly TimeSpan?[] TouchLastDownTime = new TimeSpan?[34];
            public readonly TimeSpan?[] TouchLastUpTime = new TimeSpan?[34];
            public readonly Dictionary<int, NoteState> Notes = new();
        }

        private sealed class NoteState
        {
            public NotesTypeID.Def Kind = NotesTypeID.Def.End;
            public int Lane;
            public int TouchAreaIndex = -1;
            public bool VisibilityKnown;
            public bool Visible;
            public TimeSpan? LastVisibilityLogTime;
            public bool VisibilityFallbackLogged;
            public bool VisibilityTGridFallbackLogged;
            public bool RegisterAttemptLogged;
            public TimeSpan? LastRegisterAttemptTime;
            public bool RegisterKnown;
            public bool Registered;
            public TimeSpan? LastRegisterResultTime;
            public bool ObjectInitialized;
            public bool JudgeWindowOpenLogged;
            public bool JudgeDeadlineLogged;
            public bool VisualKnown;
            public int VisualStatus = int.MinValue;
            public double? LastVisualDiff;
            public TimeSpan? LastVisualLogTime;
            public bool HoldKnown;
            public NoteJudge.ETiming HoldHeadResult = NoteJudge.ETiming.End;
            public bool HoldHeadJudged;
            public bool HoldBodyOn;
            public bool HoldPressed;
            public bool HoldTrigger;
            public TimeSpan? LastHoldLogTime;
            public bool SlideKnown;
            public int SlideHitIndex = -1;
            public int SlideHitCount = -1;
            public bool SlideHitIn;
            public int SlideSubIndex = -1;
            public string SlideDetail = string.Empty;
            public NoteJudge.ETiming SlideResult = NoteJudge.ETiming.End;
            public bool SlideEndFlag;
            public TimeSpan? LastSlideLogTime;
        }

        public static void BeginChartLoad(int playerId)
        {
            if (!Setting.EnableSoflanDiagnosticLog)
                return;

            try
            {
                PlayerStates[playerId] = new PlayerState();
                Write("SESSION_BEGIN", $"player={playerId}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "BeginChartLoad", ex);
            }
        }

        public static void CompositionLoaded(
            int playerId,
            string chartPath,
            bool hasSoflan,
            TimeSpan runtimeChartOffset)
        {
            if (!Setting.EnableSoflanDiagnosticLog)
                return;

            try
            {
                var state = GetOrCreatePlayer(playerId);
                state.HasSoflan = hasSoflan;
                state.ChartPath = chartPath ?? string.Empty;
                state.RuntimeChartOffset = runtimeChartOffset;
                Write(
                    "SESSION_READY",
                    $"player={playerId} hasSoflan={B(hasSoflan)} runtimeChartOffset={F(runtimeChartOffset)} chart={Q(state.ChartPath)}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "CompositionLoaded", ex);
            }
        }

        public static void SoflanLineLoaded(int playerId, string line)
        {
            if (!Setting.EnableSoflanDiagnosticLog)
                return;

            try
            {
                GetOrCreatePlayer(playerId);
                Write("SOFLAN_LOAD", $"player={playerId} line={Q(line)}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "SoflanLineLoaded", ex);
            }
        }

        public static void NoteLoaded(
            int playerId,
            NoteData note,
            int soflanGroup,
            bool fixedSoflan,
            float fixedSpeed,
            string marker)
        {
            if (!Setting.EnableSoflanDiagnosticLog || note == null)
                return;

            try
            {
                var state = GetOrCreatePlayer(playerId);
                var noteState = GetOrCreateNote(state, note.indexNote);
                noteState.Kind = note.type.getEnum();
                noteState.Lane = note.startButtonPos;
                noteState.TouchAreaIndex = GetTouchAreaIndex(note.touchArea, note.startButtonPos);
                Write(
                    "NOTE_LOAD",
                    $"player={playerId} note={note.indexNote} kind={note.type.getEnum()} lane={note.startButtonPos} touchArea={note.touchArea} touchIndex={noteState.TouchAreaIndex} runtimeNoteMsec={F(note.time.msec)} runtimeEndMsec={F(note.end.msec)} grid={note.time.grid} endGrid={note.end.grid} group={soflanGroup} fixed={B(fixedSoflan)} fixedSpeed={F(fixedSpeed)} marker={Q(marker)}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "NoteLoaded", ex);
            }
        }

        public static void CaptureInputFrame(int playerId)
        {
            if (!TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var runtimeTime = SoflanGameClock.CurrentTime;
                if (state.LastFrameTime.HasValue)
                {
                    var frameDelta = runtimeTime - state.LastFrameTime.Value;
                    if (frameDelta < TimeSpan.FromMilliseconds(-1)
                        || frameDelta > HeartbeatTime)
                    {
                        Write(
                            "TIME_DISCONTINUITY",
                            $"player={playerId} previousRuntimeMsec={F(state.LastFrameTime)} runtimeMsec={F(runtimeTime)} deltaMsec={F(frameDelta)}");
                    }
                }
                state.LastFrameTime = runtimeTime;

                for (var i = 0; i < 8; i++)
                {
                    var button = (InputManager.ButtonSetting)i;
                    var gameDown = InputManager.InGameButtonDown(playerId, button);
                    var rawDown = InputManager.GetButtonDown(playerId, button);
                    var gamePush = InputManager.InGameButtonPush(playerId, button);
                    var rawPush = InputManager.GetButtonPush(playerId, button);
                    var down = gameDown || rawDown;
                    var push = gamePush || rawPush;

                    if (down || (!state.ButtonPush[i] && push))
                    {
                        state.ButtonDownTime[i] = runtimeTime;
                        state.ButtonLastDownTime[i] = runtimeTime;
                        state.LastAnyInputTime = runtimeTime;
                        state.LastAnyInput = "BUTTON:" + button;
                        Write(
                            "INPUT",
                            $"player={playerId} runtimeMsec={F(runtimeTime)} device=BUTTON control={button} edge=DOWN inGameDown={B(gameDown)} rawDown={B(rawDown)} inGamePush={B(gamePush)} rawPush={B(rawPush)} used={B(InputManager.IsUsedThisFrame(playerId, button))} pushTime={InputManager.GetButtonPushTime(playerId, button)}");
                    }
                    if (state.ButtonPush[i] && !push)
                    {
                        state.ButtonLastUpTime[i] = runtimeTime;
                        state.LastAnyInputTime = runtimeTime;
                        state.LastAnyInput = "BUTTON:" + button;
                        Write(
                            "INPUT",
                            $"player={playerId} runtimeMsec={F(runtimeTime)} device=BUTTON control={button} edge=UP heldMsec={F(Duration(runtimeTime, state.ButtonDownTime[i]))} inGamePush={B(gamePush)} rawPush={B(rawPush)} used={B(InputManager.IsUsedThisFrame(playerId, button))}");
                    }
                    state.ButtonPush[i] = push;
                }

                for (var i = 0; i < 34; i++)
                {
                    var area = (InputManager.TouchPanelArea)i;
                    var gameDown = InputManager.InGameTouchPanelAreaDown(playerId, area);
                    var rawDown = InputManager.GetTouchPanelAreaDown(playerId, area);
                    var gamePush = InputManager.InGameTouchPanelAreaPush(playerId, area);
                    var rawPush = InputManager.GetTouchPanelAreaPush(playerId, area);
                    var down = gameDown || rawDown;
                    var push = gamePush || rawPush;

                    if (down || (!state.TouchPush[i] && push))
                    {
                        state.TouchDownTime[i] = runtimeTime;
                        state.TouchLastDownTime[i] = runtimeTime;
                        state.LastAnyInputTime = runtimeTime;
                        state.LastAnyInput = "TOUCH:" + area;
                        Write(
                            "INPUT",
                            $"player={playerId} runtimeMsec={F(runtimeTime)} device=TOUCH control={area} index={i} edge=DOWN inGameDown={B(gameDown)} rawDown={B(rawDown)} inGamePush={B(gamePush)} rawPush={B(rawPush)} used={B(InputManager.IsUsedThisFrame(playerId, area))} pushTime={InputManager.GetTouchPanelAreaPushTime(playerId, area)}");
                    }
                    if (state.TouchPush[i] && !push)
                    {
                        state.TouchLastUpTime[i] = runtimeTime;
                        state.LastAnyInputTime = runtimeTime;
                        state.LastAnyInput = "TOUCH:" + area;
                        Write(
                            "INPUT",
                            $"player={playerId} runtimeMsec={F(runtimeTime)} device=TOUCH control={area} index={i} edge=UP heldMsec={F(Duration(runtimeTime, state.TouchDownTime[i]))} inGamePush={B(gamePush)} rawPush={B(rawPush)} used={B(InputManager.IsUsedThisFrame(playerId, area))}");
                    }
                    state.TouchPush[i] = push;
                }
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "CaptureInputFrame", ex);
            }
        }

        public static void VisibilityDecision(
            int playerId,
            NoteData note,
            TimeSpan runtimeTime,
            TimeSpan visibleTime,
            TimeSpan normalVisibleTime,
            int soflanGroup,
            SoflanPosition currentSoflanPosition,
            bool soflanVisible,
            bool visible)
        {
            if (note == null || !TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var noteState = GetOrCreateNote(state, note.indexNote);
                var changed = !noteState.VisibilityKnown || noteState.Visible != visible;
                var runtimeNoteTime = SoflanRuntimeTime.FromGameMsecBoundary(note.time.msec);
                var near = DurationAbsolute(runtimeTime - runtimeNoteTime) <= NearNoteTime;
                var heartbeat = near
                    && Elapsed(runtimeTime, noteState.LastVisibilityLogTime) >= HeartbeatTime;
                if (!changed && !heartbeat)
                    return;

                noteState.VisibilityKnown = true;
                noteState.Visible = visible;
                noteState.LastVisibilityLogTime = runtimeTime;

                var manager = Singleton<SoflanManager>.Instance;
                var rawCurrentTime = RawCurrent(state, runtimeTime);
                var rawNoteTime = manager.GetNoteAudioTimeForSoflan(playerId, note);
                var noteSoflanPosition = manager.GetNoteSoflanPosition(
                    playerId,
                    note.indexNote,
                    runtimeNoteTime,
                    soflanGroup);
                Write(
                    "VISIBILITY",
                    $"player={playerId} note={note.indexNote} kind={note.type.getEnum()} lane={note.startButtonPos} runtimeMsec={F(runtimeTime)} rawCurrentMsec={F(rawCurrentTime)} runtimeNoteMsec={F(runtimeNoteTime)} rawNoteMsec={F(rawNoteTime)} visibleMsec={F(visibleTime)} normalVisibleMsec={F(normalVisibleTime)} normalDue={B(SoflanVisibilityPolicy.IsNormallyDue(runtimeTime, runtimeNoteTime, normalVisibleTime))} soflanVisible={B(soflanVisible)} group={soflanGroup} speed={D(manager.GetCurrentSpeed(playerId, soflanGroup, runtimeTime))} currentSoflan={D(currentSoflanPosition.Value)} noteSoflan={D(noteSoflanPosition.Value)} soflanDiff={D(noteSoflanPosition.DeltaTo(currentSoflanPosition))} decision={(visible ? "VISIBLE" : "BLOCKED")}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "VisibilityDecision", ex);
            }
        }

        public static void VisibilityFallback(
            int playerId,
            NoteData note,
            TimeSpan runtimeTime,
            TimeSpan visibleTime)
        {
            if (note == null || !TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var noteState = GetOrCreateNote(state, note.indexNote);
                if (noteState.VisibilityFallbackLogged)
                    return;
                noteState.VisibilityFallbackLogged = true;
                Write(
                    "VISIBILITY_FALLBACK",
                    $"player={playerId} note={note.indexNote} kind={note.type.getEnum()} lane={note.startButtonPos} runtimeMsec={F(runtimeTime)} runtimeNoteMsec={F(SoflanRuntimeTime.FromGameMsecBoundary(note.time.msec))} visibleMsec={F(visibleTime)} reason=unsupported_visual_kind");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "VisibilityFallback", ex);
            }
        }

        public static void VisibilityTGridFallback(
            int playerId,
            NoteData note,
            TimeSpan runtimeTime,
            TimeSpan visibleTime,
            long fallbackCount,
            string reason)
        {
            if (note == null || !TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var noteState = GetOrCreateNote(state, note.indexNote);
                if (noteState.VisibilityTGridFallbackLogged)
                    return;
                noteState.VisibilityTGridFallbackLogged = true;
                Write(
                    "VISIBILITY_TGRID_FALLBACK",
                    $"player={playerId} note={note.indexNote} kind={note.type.getEnum()} lane={note.startButtonPos} runtimeMsec={F(runtimeTime)} runtimeNoteMsec={F(SoflanRuntimeTime.FromGameMsecBoundary(note.time.msec))} visibleMsec={F(visibleTime)} fallbackCount={fallbackCount} reason={reason}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "VisibilityTGridFallback", ex);
            }
        }

        public static void RegisterAttempt(int playerId, NoteData note, string source)
        {
            if (note == null || !TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var runtimeTime = SoflanGameClock.CurrentTime;
                var noteState = GetOrCreateNote(state, note.indexNote);
                if (noteState.RegisterAttemptLogged
                    && Elapsed(runtimeTime, noteState.LastRegisterAttemptTime) < HeartbeatTime)
                    return;

                noteState.RegisterAttemptLogged = true;
                noteState.LastRegisterAttemptTime = runtimeTime;
                Write(
                    "REGISTER_ATTEMPT",
                    $"player={playerId} note={note.indexNote} kind={note.type.getEnum()} lane={note.startButtonPos} runtimeMsec={F(runtimeTime)} runtimeNoteMsec={F(SoflanRuntimeTime.FromGameMsecBoundary(note.time.msec))} runtimeEndMsec={F(SoflanRuntimeTime.FromGameMsecBoundary(note.end.msec))} isUsed={B(note.isUsed)} source={source}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "RegisterAttempt", ex);
            }
        }

        public static void RegisterResult(int playerId, NoteData note, bool registered, string source)
        {
            if (note == null || !TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var runtimeTime = SoflanGameClock.CurrentTime;
                var noteState = GetOrCreateNote(state, note.indexNote);
                var changed = !noteState.RegisterKnown || noteState.Registered != registered;
                if (!registered && !changed
                    && Elapsed(runtimeTime, noteState.LastRegisterResultTime) < HeartbeatTime)
                    return;

                noteState.RegisterKnown = true;
                noteState.Registered = registered;
                noteState.LastRegisterResultTime = runtimeTime;
                Write(
                    "REGISTER_RESULT",
                    $"player={playerId} note={note.indexNote} kind={note.type.getEnum()} lane={note.startButtonPos} runtimeMsec={F(runtimeTime)} runtimeNoteMsec={F(SoflanRuntimeTime.FromGameMsecBoundary(note.time.msec))} result={(registered ? "SUCCESS" : "FAILED")} source={source}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "RegisterResult", ex);
            }
        }

        public static void SkipRegisterResult(int playerId, NoteData note, bool skipped)
        {
            if (note == null || !TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var runtimeTime = SoflanGameClock.CurrentTime;
                var runtimeEndTime = SoflanRuntimeTime.FromGameMsecBoundary(note.end.msec);
                Write(
                    "SKIP_REGISTER",
                    $"player={playerId} note={note.indexNote} kind={note.type.getEnum()} lane={note.startButtonPos} runtimeMsec={F(runtimeTime)} runtimeNoteMsec={F(SoflanRuntimeTime.FromGameMsecBoundary(note.time.msec))} runtimeEndMsec={F(runtimeEndTime)} deltaFromEndMsec={F(runtimeTime - runtimeEndTime)} forcedResult={(skipped ? "TooLate" : "NONE")}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "SkipRegisterResult", ex);
            }
        }

        public static void ObjectInitialized(
            int playerId,
            NoteData note,
            TimeSpan appearTime,
            TimeSpan tailTime,
            TimeSpan defaultTime,
            int soflanGroup,
            bool fixedSoflan,
            float fixedSpeed,
            SoflanPosition noteSoflanPosition,
            TimeSpan maiBugAdjust,
            string source)
        {
            if (note == null || !TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var noteState = GetOrCreateNote(state, note.indexNote);
                if (noteState.ObjectInitialized)
                    return;
                noteState.ObjectInitialized = true;
                var runtimeTime = SoflanGameClock.CurrentTime;
                Write(
                    "OBJECT_INITIALIZE",
                    $"player={playerId} note={note.indexNote} kind={note.type.getEnum()} lane={note.startButtonPos} touchArea={note.touchArea} runtimeMsec={F(runtimeTime)} appearMsec={F(appearTime)} tailMsec={F(tailTime)} defaultMsec={F(defaultTime)} group={soflanGroup} fixed={B(fixedSoflan)} fixedSpeed={F(fixedSpeed)} noteSoflan={D(noteSoflanPosition.Value)} maiBugAdjustMsec={F(maiBugAdjust)} source={source}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "ObjectInitialized", ex);
            }
        }

        public static JudgeProbe BeforeJudgeCheck(
            int playerId,
            int noteIndex,
            NotesTypeID.Def kind,
            int lane,
            int touchAreaIndex,
            bool includeButton,
            TimeSpan appearTime,
            TimeSpan tailTime,
            NoteJudge.EJudgeType judgeType,
            TimeSpan judgeStart,
            TimeSpan judgeEnd,
            NoteJudge.ETiming result,
            NoteJudge.ETiming headResult,
            bool endFlag,
            bool isJudgeNote,
            TimeSpan judgeTimingDiff,
            string source)
        {
            var probe = new JudgeProbe();
            if (!TryGetActivePlayer(playerId, out var state))
                return probe;

            try
            {
                var runtimeTime = SoflanGameClock.CurrentTime;
                var timingFrame = Singleton<GamePlayManager>.Instance
                    .GetGameScore(playerId)
                    .UserOption
                    .GetJudgeTimingFrame();
                var timingOffset = SoflanRuntimeTime.FromMilliseconds(timingFrame * 16.666666d);
                var windowStart = appearTime + judgeStart + timingOffset;
                var windowEnd = appearTime + judgeEnd + timingOffset;
                var noteState = GetOrCreateNote(state, noteIndex);

                if (!noteState.JudgeWindowOpenLogged && runtimeTime >= windowStart)
                {
                    noteState.JudgeWindowOpenLogged = true;
                    Write(
                        "JUDGE_WINDOW_OPEN",
                        $"player={playerId} note={noteIndex} kind={kind} lane={lane} runtimeMsec={F(runtimeTime)} appearMsec={F(appearTime)} tailMsec={F(tailTime)} windowStartMsec={F(windowStart)} windowEndMsec={F(windowEnd)} judgeType={judgeType} timingFrame={F(timingFrame)} source={source}");
                }
                if (!noteState.JudgeDeadlineLogged
                    && runtimeTime > windowEnd
                    && result == NoteJudge.ETiming.End
                    && headResult == NoteJudge.ETiming.End
                    && !endFlag)
                {
                    noteState.JudgeDeadlineLogged = true;
                    Write(
                        "JUDGE_DEADLINE",
                        $"player={playerId} note={noteIndex} kind={kind} lane={lane} runtimeMsec={F(runtimeTime)} windowEndMsec={F(windowEnd)} overdueMsec={F(runtimeTime - windowEnd)} source={source}");
                }

                ReadRelevantInput(
                    playerId,
                    lane,
                    touchAreaIndex,
                    includeButton,
                    out var buttonGameDown,
                    out var buttonRawDown,
                    out var buttonGamePush,
                    out var buttonRawPush,
                    out var touchGameDown,
                    out var touchRawDown,
                    out var touchGamePush,
                    out var touchRawPush,
                    out var used);
                var engineDown = buttonGameDown || touchGameDown;
                var observedDown = engineDown || buttonRawDown || touchRawDown;
                if (observedDown)
                {
                    var inWindow = runtimeTime >= windowStart && runtimeTime <= windowEnd;
                    var candidate = !GameManager.IsAutoPlay()
                        && engineDown
                        && inWindow
                        && isJudgeNote
                        && !used
                        && result == NoteJudge.ETiming.End
                        && headResult == NoteJudge.ETiming.End;
                    Write(
                        "JUDGE_ATTEMPT",
                        $"player={playerId} note={noteIndex} kind={kind} lane={lane} touchIndex={touchAreaIndex} runtimeMsec={F(runtimeTime)} deltaFromHeadMsec={F(runtimeTime - appearTime)} engineDown={B(engineDown)} buttonGameDown={B(buttonGameDown)} buttonRawDown={B(buttonRawDown)} buttonGamePush={B(buttonGamePush)} buttonRawPush={B(buttonRawPush)} touchGameDown={B(touchGameDown)} touchRawDown={B(touchRawDown)} touchGamePush={B(touchGamePush)} touchRawPush={B(touchRawPush)} used={B(used)} isJudgeNote={B(isJudgeNote)} inWindow={B(inWindow)} candidate={B(candidate)} resultBefore={result} headResultBefore={headResult} judgeDiffBefore={F(judgeTimingDiff)} source={source}");
                }

                probe.Active = true;
                probe.PlayerId = playerId;
                probe.NoteIndex = noteIndex;
                probe.Source = source;
                probe.RuntimeTime = runtimeTime;
                probe.Result = result;
                probe.HeadResult = headResult;
                probe.EndFlag = endFlag;
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "BeforeJudgeCheck", ex);
            }
            return probe;
        }

        public static void AfterJudgeCheck(
            JudgeProbe probe,
            NoteJudge.ETiming result,
            NoteJudge.ETiming headResult,
            bool endFlag,
            TimeSpan judgeTimingDiff)
        {
            if (!probe.Active)
                return;

            try
            {
                var runtimeTime = SoflanGameClock.CurrentTime;
                if (headResult != probe.HeadResult)
                {
                    Write(
                        "JUDGE_RESULT",
                        $"player={probe.PlayerId} note={probe.NoteIndex} phase=HEAD runtimeMsec={F(runtimeTime)} resultBefore={probe.HeadResult} resultAfter={headResult} judgeBox={NoteJudge.ConvertJudge(headResult)} judgeDiffMsec={F(judgeTimingDiff)} source={probe.Source}");
                }
                if (result != probe.Result)
                {
                    Write(
                        "JUDGE_RESULT",
                        $"player={probe.PlayerId} note={probe.NoteIndex} phase=FINAL runtimeMsec={F(runtimeTime)} resultBefore={probe.Result} resultAfter={result} judgeBox={NoteJudge.ConvertJudge(result)} judgeDiffMsec={F(judgeTimingDiff)} source={probe.Source}");
                }
                if (!probe.EndFlag && endFlag)
                {
                    Write(
                        "OBJECT_END",
                        $"player={probe.PlayerId} note={probe.NoteIndex} runtimeMsec={F(runtimeTime)} result={result} headResult={headResult} judgeDiffMsec={F(judgeTimingDiff)} source={probe.Source}");
                }
            }
            catch (Exception ex)
            {
                ErrorOnce(probe.PlayerId, "AfterJudgeCheck", ex);
            }
        }

        public static void HoldState(
            int playerId,
            int noteIndex,
            NoteJudge.ETiming headResult,
            bool headJudged,
            bool bodyOn,
            bool pressed,
            bool trigger,
            TimeSpan releaseTime,
            bool endFlag,
            string source)
        {
            if (!TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var runtimeTime = SoflanGameClock.CurrentTime;
                var noteState = GetOrCreateNote(state, noteIndex);
                var changed = !noteState.HoldKnown
                    || noteState.HoldHeadResult != headResult
                    || noteState.HoldHeadJudged != headJudged
                    || noteState.HoldBodyOn != bodyOn
                    || noteState.HoldPressed != pressed
                    || noteState.HoldTrigger != trigger;
                var heartbeat = headJudged
                    && !endFlag
                    && Elapsed(runtimeTime, noteState.LastHoldLogTime) >= HoldHeartbeatTime;
                if (!changed && !heartbeat)
                    return;

                noteState.HoldKnown = true;
                noteState.HoldHeadResult = headResult;
                noteState.HoldHeadJudged = headJudged;
                noteState.HoldBodyOn = bodyOn;
                noteState.HoldPressed = pressed;
                noteState.HoldTrigger = trigger;
                noteState.LastHoldLogTime = runtimeTime;
                Write(
                    "HOLD_STATE",
                    $"player={playerId} note={noteIndex} runtimeMsec={F(runtimeTime)} headResult={headResult} headJudged={B(headJudged)} bodyOn={B(bodyOn)} pressed={B(pressed)} trigger={B(trigger)} releaseMsec={F(releaseTime)} end={B(endFlag)} source={source}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "HoldState", ex);
            }
        }

        public static void VisualSample(
            int playerId,
            int noteIndex,
            NotesTypeID.Def kind,
            int soflanGroup,
            TimeSpan runtimeTime,
            SoflanPosition currentSoflanPosition,
            SoflanPosition noteSoflanPosition,
            double diff,
            float visualValue,
            double scaleStartDistance,
            double moveStartDistance,
            int status,
            bool fixedSoflan,
            string source)
        {
            if (!TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var noteState = GetOrCreateNote(state, noteIndex);
                var visualStatus = status * 2 + (diff >= 0d ? 0 : 1);
                var crossed = noteState.VisualKnown
                    && noteState.LastVisualDiff.HasValue
                    && ((noteState.LastVisualDiff.Value > 0d && diff <= 0d)
                        || (noteState.LastVisualDiff.Value < 0d && diff >= 0d));
                var changed = !noteState.VisualKnown || noteState.VisualStatus != visualStatus;
                var near = Math.Abs(diff) <= Math.Max(scaleStartDistance, 1000d);
                var heartbeat = near
                    && Elapsed(runtimeTime, noteState.LastVisualLogTime) >= HeartbeatTime;
                noteState.LastVisualDiff = diff;
                if (!changed && !crossed && !heartbeat)
                    return;

                noteState.VisualKnown = true;
                noteState.VisualStatus = visualStatus;
                noteState.LastVisualLogTime = runtimeTime;
                var manager = Singleton<SoflanManager>.Instance;
                var fields = $"player={playerId} note={noteIndex} kind={kind} runtimeMsec={F(runtimeTime)} rawCurrentMsec={F(RawCurrent(state, runtimeTime))} group={soflanGroup} speed={D(manager.GetCurrentSpeed(playerId, soflanGroup, runtimeTime))} currentSoflan={D(currentSoflanPosition.Value)} noteSoflan={D(noteSoflanPosition.Value)} diff={D(diff)} visualValue={F(visualValue)} scaleStart={D(scaleStartDistance)} moveStart={D(moveStartDistance)} status={VisualStatusName(status)} direction={(diff >= 0d ? "BEFORE" : "AFTER")} fixed={B(fixedSoflan)} source={source}";
                if (crossed)
                    Write("VISUAL_CROSS", fields);
                Write("VISUAL_STATE", fields);
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "VisualSample", ex);
            }
        }

        public static void SlideProgress(
            int playerId,
            int noteIndex,
            NotesTypeID.Def kind,
            int hitIndex,
            int hitCount,
            bool hitIn,
            int subIndex,
            TimeSpan tailTime,
            TimeSpan lastWaitTime,
            NoteJudge.ETiming result,
            bool endFlag,
            TimeSpan judgeTimingDiff,
            string detail,
            string source)
        {
            if (!TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var runtimeTime = SoflanGameClock.CurrentTime;
                var noteState = GetOrCreateNote(state, noteIndex);
                var changed = !noteState.SlideKnown
                    || noteState.SlideHitIndex != hitIndex
                    || noteState.SlideHitCount != hitCount
                    || noteState.SlideHitIn != hitIn
                    || noteState.SlideSubIndex != subIndex
                    || noteState.SlideDetail != (detail ?? string.Empty)
                    || noteState.SlideResult != result
                    || noteState.SlideEndFlag != endFlag;
                var near = DurationAbsolute(runtimeTime - tailTime) <= NearNoteTime;
                var heartbeat = near
                    && Elapsed(runtimeTime, noteState.LastSlideLogTime) >= HeartbeatTime;
                if (!changed && !heartbeat)
                    return;

                noteState.SlideKnown = true;
                noteState.SlideHitIndex = hitIndex;
                noteState.SlideHitCount = hitCount;
                noteState.SlideHitIn = hitIn;
                noteState.SlideSubIndex = subIndex;
                noteState.SlideDetail = detail ?? string.Empty;
                noteState.SlideResult = result;
                noteState.SlideEndFlag = endFlag;
                noteState.LastSlideLogTime = runtimeTime;
                Write(
                    "SLIDE_PROGRESS",
                    $"player={playerId} note={noteIndex} kind={kind} runtimeMsec={F(runtimeTime)} tailMsec={F(tailTime)} hitIndex={hitIndex} hitCount={hitCount} hitIn={B(hitIn)} subIndex={subIndex} lastWaitMsec={F(lastWaitTime)} result={result} end={B(endFlag)} judgeDiffMsec={F(judgeTimingDiff)} detail={Q(noteState.SlideDetail)} source={source}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "SlideProgress", ex);
            }
        }

        public static bool GetIsJudged(int playerId, int noteIndex)
        {
            if (!TryGetActivePlayer(playerId, out _))
                return false;
            try
            {
                return TryGetRuntimeNote(playerId, noteIndex, out var note) && note.isJudged;
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "GetIsJudged", ex);
                return false;
            }
        }

        public static void ScoreResult(
            int playerId,
            int noteIndex,
            NoteScore.EScoreType scoreType,
            NoteJudge.ETiming requestedTiming,
            bool trackSkip,
            bool wasJudged)
        {
            if (!TryGetActivePlayer(playerId, out var state))
                return;

            try
            {
                var runtimeTime = SoflanGameClock.CurrentTime;
                var hasNote = TryGetRuntimeNote(playerId, noteIndex, out var note);
                var isJudged = hasNote && note.isJudged;
                var accepted = !wasJudged && isJudged;
                var effectiveTiming = trackSkip ? NoteJudge.ETiming.TooLate : requestedTiming;
                Write(
                    "SCORE_RESULT",
                    $"player={playerId} note={noteIndex} scoreType={scoreType} runtimeMsec={F(runtimeTime)} requested={requestedTiming} effective={effectiveTiming} wasJudged={B(wasJudged)} isJudged={B(isJudged)} accepted={B(accepted)} trackSkip={B(trackSkip)}");

                if (!accepted || (effectiveTiming != NoteJudge.ETiming.TooFast
                    && effectiveTiming != NoteJudge.ETiming.TooLate))
                    return;

                var noteState = GetOrCreateNote(state, noteIndex);
                var kind = hasNote ? note.type.getEnum() : noteState.Kind;
                var lane = hasNote ? note.startButtonPos : noteState.Lane;
                var touchAreaIndex = hasNote
                    ? GetTouchAreaIndex(note.touchArea, lane)
                    : noteState.TouchAreaIndex;
                if (touchAreaIndex < 0)
                    touchAreaIndex = lane >= 0 && lane < 8 ? lane : -1;
                var manager = Singleton<SoflanManager>.Instance;
                var group = manager.getNoteSoflanGroup(playerId, noteIndex);
                var runtimeNoteTime = hasNote
                    ? (TimeSpan?)SoflanRuntimeTime.FromGameMsecBoundary(note.time.msec)
                    : null;
                var rawNoteTime = hasNote
                    ? (TimeSpan?)manager.GetNoteAudioTimeForSoflan(playerId, note)
                    : null;
                var rawCurrentTime = RawCurrent(state, runtimeTime);
                var lastButtonDown = lane >= 0 && lane < 8
                    ? state.ButtonLastDownTime[lane]
                    : null;
                var lastButtonUp = lane >= 0 && lane < 8
                    ? state.ButtonLastUpTime[lane]
                    : null;
                var lastTouchDown = touchAreaIndex >= 0 && touchAreaIndex < 34
                    ? state.TouchLastDownTime[touchAreaIndex]
                    : null;
                var lastTouchUp = touchAreaIndex >= 0 && touchAreaIndex < 34
                    ? state.TouchLastUpTime[touchAreaIndex]
                    : null;
                if (touchAreaIndex == 16 || touchAreaIndex == 17)
                {
                    lastTouchDown = Latest(
                        state.TouchLastDownTime[16],
                        state.TouchLastDownTime[17]);
                    lastTouchUp = Latest(
                        state.TouchLastUpTime[16],
                        state.TouchLastUpTime[17]);
                }
                Write(
                    "MISS",
                    $"player={playerId} note={noteIndex} kind={kind} scoreType={scoreType} lane={lane} touchIndex={touchAreaIndex} result={effectiveTiming} runtimeMsec={F(runtimeTime)} rawCurrentMsec={F(rawCurrentTime)} runtimeNoteMsec={F(runtimeNoteTime)} rawNoteMsec={F(rawNoteTime)} runtimeDeltaMsec={F(Difference(runtimeTime, runtimeNoteTime))} rawDeltaMsec={F(Difference(rawCurrentTime, rawNoteTime))} group={group} speed={D(manager.GetCurrentSpeed(playerId, group, runtimeTime))} visibilityKnown={B(noteState.VisibilityKnown)} lastVisible={B(noteState.Visible)} lastVisibilityLogMsec={F(noteState.LastVisibilityLogTime)} registerKnown={B(noteState.RegisterKnown)} registered={B(noteState.Registered)} lastRegisterMsec={F(noteState.LastRegisterResultTime)} objectInitialized={B(noteState.ObjectInitialized)} lastButtonDownMsec={F(lastButtonDown)} lastButtonUpMsec={F(lastButtonUp)} lastTouchDownMsec={F(lastTouchDown)} lastTouchUpMsec={F(lastTouchUp)} lastAnyInput={Q(state.LastAnyInput)} lastAnyInputMsec={F(state.LastAnyInputTime)}");
            }
            catch (Exception ex)
            {
                ErrorOnce(playerId, "ScoreResult", ex);
            }
        }

        public static void Exception(int playerId, int noteIndex, string source, Exception ex)
        {
            if (!TryGetActivePlayer(playerId, out _))
                return;
            Write(
                "RUNTIME_EXCEPTION",
                $"player={playerId} note={noteIndex} source={source} type={ex?.GetType().FullName} message={Q(ex?.Message)}");
        }

        public static int GetTouchAreaIndex(TouchSensorType touchArea, int lane)
        {
            switch (touchArea)
            {
                case TouchSensorType.A:
                    return lane >= 0 && lane < 8 ? lane : -1;
                case TouchSensorType.B:
                    return lane >= 0 && lane < 8 ? lane + 8 : -1;
                case TouchSensorType.C:
                    return lane >= 0 && lane < 2 ? lane + 16 : -1;
                case TouchSensorType.D:
                    return lane >= 0 && lane < 8 ? lane + 18 : -1;
                case TouchSensorType.E:
                    return lane >= 0 && lane < 8 ? lane + 26 : -1;
                default:
                    return -1;
            }
        }

        private static void ReadRelevantInput(
            int playerId,
            int lane,
            int touchAreaIndex,
            bool includeButton,
            out bool buttonGameDown,
            out bool buttonRawDown,
            out bool buttonGamePush,
            out bool buttonRawPush,
            out bool touchGameDown,
            out bool touchRawDown,
            out bool touchGamePush,
            out bool touchRawPush,
            out bool used)
        {
            buttonGameDown = false;
            buttonRawDown = false;
            buttonGamePush = false;
            buttonRawPush = false;
            touchGameDown = false;
            touchRawDown = false;
            touchGamePush = false;
            touchRawPush = false;
            used = false;

            if (includeButton && lane >= 0 && lane < 8)
            {
                var button = (InputManager.ButtonSetting)lane;
                buttonGameDown = InputManager.InGameButtonDown(playerId, button);
                buttonRawDown = InputManager.GetButtonDown(playerId, button);
                buttonGamePush = InputManager.InGameButtonPush(playerId, button);
                buttonRawPush = InputManager.GetButtonPush(playerId, button);
                used |= InputManager.IsUsedThisFrame(playerId, button);
            }

            var areaIndex = touchAreaIndex;
            if (areaIndex < 0 && lane >= 0 && lane < 8)
                areaIndex = lane;
            if (areaIndex >= 0 && areaIndex < 34)
            {
                var area = (InputManager.TouchPanelArea)areaIndex;
                touchGameDown = InputManager.InGameTouchPanelAreaDown(playerId, area);
                touchRawDown = InputManager.GetTouchPanelAreaDown(playerId, area);
                touchGamePush = InputManager.InGameTouchPanelAreaPush(playerId, area);
                touchRawPush = InputManager.GetTouchPanelAreaPush(playerId, area);
                if (areaIndex == 16 || areaIndex == 17)
                {
                    var otherArea = (InputManager.TouchPanelArea)(areaIndex == 16 ? 17 : 16);
                    touchGameDown |= InputManager.InGameTouchPanelAreaDown(playerId, otherArea);
                    touchRawDown |= InputManager.GetTouchPanelAreaDown(playerId, otherArea);
                    touchGamePush |= InputManager.InGameTouchPanelAreaPush(playerId, otherArea);
                    touchRawPush |= InputManager.GetTouchPanelAreaPush(playerId, otherArea);
                }
                used |= InputManager.IsUsedThisFrame(playerId, area);
            }
        }

        private static bool TryGetRuntimeNote(int playerId, int noteIndex, out NoteData note)
        {
            note = null;
            var reader = NotesManager.Instance(playerId)?.getReader();
            var notes = reader?.GetNoteList();
            if (notes == null || noteIndex < 0 || noteIndex >= notes.Count)
                return false;
            note = notes[noteIndex];
            return note != null;
        }

        private static PlayerState GetOrCreatePlayer(int playerId)
        {
            if (!PlayerStates.TryGetValue(playerId, out var state))
            {
                state = new PlayerState();
                PlayerStates[playerId] = state;
            }
            return state;
        }

        private static bool TryGetActivePlayer(int playerId, out PlayerState state)
        {
            state = null;
            return Setting.EnableSoflanDiagnosticLog
                && PlayerStates.TryGetValue(playerId, out state)
                && state.HasSoflan;
        }

        private static NoteState GetOrCreateNote(PlayerState state, int noteIndex)
        {
            if (!state.Notes.TryGetValue(noteIndex, out var noteState))
            {
                noteState = new NoteState();
                state.Notes[noteIndex] = noteState;
            }
            return noteState;
        }

        private static TimeSpan RawCurrent(PlayerState state, TimeSpan runtimeTime)
        {
            return SoflanRuntimeTime.ToRawChartAudioTime(
                runtimeTime,
                state.RuntimeChartOffset,
                TimeSpan.Zero);
        }

        private static TimeSpan? Duration(TimeSpan now, TimeSpan? start)
        {
            return start.HasValue ? now - start.Value : (TimeSpan?)null;
        }

        private static TimeSpan? Latest(TimeSpan? first, TimeSpan? second)
        {
            if (!first.HasValue)
                return second;
            if (!second.HasValue)
                return first;
            return first.Value >= second.Value ? first : second;
        }

        private static TimeSpan Elapsed(TimeSpan now, TimeSpan? previous)
        {
            return previous.HasValue ? now - previous.Value : TimeSpan.MaxValue;
        }

        private static TimeSpan DurationAbsolute(TimeSpan value)
        {
            return value < TimeSpan.Zero ? value.Negate() : value;
        }

        private static TimeSpan? Difference(TimeSpan value, TimeSpan? other)
        {
            return other.HasValue ? value - other.Value : (TimeSpan?)null;
        }

        private static string VisualStatusName(int status)
        {
            switch (status)
            {
                case 0:
                    return "Init";
                case 1:
                    return "Scale";
                case 2:
                    return "Move";
                case 3:
                    return "Check";
                case 4:
                    return "End";
                default:
                    return status.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static void ErrorOnce(int playerId, string scope, Exception ex)
        {
            var key = playerId + ":" + scope + ":" + ex.GetType().FullName;
            if (!LoggedErrors.Add(key))
                return;
            PatchLog.Diagnostic(
                $"evt=DIAGNOSTIC_ERROR player={playerId} scope={scope} type={ex.GetType().FullName} message={Q(ex.Message)}");
        }

        private static void Write(string eventName, string fields)
        {
            PatchLog.Diagnostic("evt=" + eventName + " " + fields);
        }

        private static string B(bool value)
        {
            return value ? "1" : "0";
        }

        private static string F(float value)
        {
            return float.IsNaN(value)
                ? "NaN"
                : value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string F(TimeSpan value)
        {
            return value.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string F(TimeSpan? value)
        {
            return value.HasValue ? F(value.Value) : "NaN";
        }

        private static string D(double value)
        {
            return double.IsNaN(value)
                ? "NaN"
                : value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Q(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "'") + "\"";
        }
    }
}
