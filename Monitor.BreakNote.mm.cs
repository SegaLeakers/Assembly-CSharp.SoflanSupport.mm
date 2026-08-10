#pragma warning disable CS0626
using DB;
using MAI2.Util;
using Manager;
using SoflanSupport;
using System;
using UnityEngine;

namespace Monitor
{
    public class patch_BreakNote : BreakNote
    {
        private SoflanManager breakSoflanManager;
        private bool breakIsInSoflan;
        private int breakSoflanGroup;
        private SoflanPosition breakNoteSoflanPosition;
        private bool breakIsFixedSoflanToUnifiedSpeed;
        private float breakFixedSoflanUnifiedSpeed;
        private double breakVisualDefaultDistance;
        private TimeSpan breakMaiBugAdjust;

        public extern void orig_Initialize(NoteData note);

        public void Initialize(NoteData note)
        {
            orig_Initialize(note);

            breakSoflanManager = Singleton<SoflanManager>.Instance;
            breakIsInSoflan = breakSoflanManager.containsSoflans(MonitorId);
            if (breakIsInSoflan)
            {
                breakSoflanGroup = breakSoflanManager.getNoteSoflanGroup(MonitorId, NoteIndex);
                breakNoteSoflanPosition = breakSoflanManager.GetNoteSoflanPosition(
                    MonitorId,
                    NoteIndex,
                    SoflanRuntimeTime.FromGameMsecBoundary(AppearMsec),
                    breakSoflanGroup);
            }
            else
            {
                breakSoflanGroup = 0;
                breakNoteSoflanPosition = new SoflanPosition(AppearMsec);
            }

            var fixedNote = (patch_NoteData)note;
            breakIsFixedSoflanToUnifiedSpeed = fixedNote.isFixedSoflanToUnifiedSpeed
                && FixedSoflan.IsSupportedTapKind(note.type.getEnum());
            breakFixedSoflanUnifiedSpeed = fixedNote.fixedSoflanUnifiedSpeed > 0f
                ? fixedNote.fixedSoflanUnifiedSpeed
                : FixedSoflan.DefaultUnifiedSpeed;
            breakVisualDefaultDistance = breakIsFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetDefaultTime(breakFixedSoflanUnifiedSpeed).TotalMilliseconds
                : DefaultMsec;
            breakMaiBugAdjust = SoflanVisualTiming.GetMaiBugAdjust(
                note.type.getEnum(),
                SoflanRuntimeTime.FromMilliseconds(2d * breakVisualDefaultDistance));
        }

        protected extern void orig_NoteCheck();

        protected void NoteCheck()
        {
#if DEBUG
            var diagnosticProbe = SoflanDiagnostic.BeforeJudgeCheck(
                MonitorId,
                NoteIndex,
                NoteKind,
                ButtonId,
                -1,
                true,
                SoflanRuntimeTime.FromGameMsecBoundary(AppearMsec),
                SoflanRuntimeTime.FromGameMsecBoundary(TailMsec),
                JudgeType,
                SoflanRuntimeTime.FromGameMsecBoundary(GetJudgeStartMsec()),
                SoflanRuntimeTime.FromGameMsecBoundary(GetJudgeEndMsec()),
                JudgeResult,
                NoteJudge.ETiming.End,
                EndFlag,
                IsJudgeNote(),
                SoflanRuntimeTime.FromGameMsecBoundary(JudgeTimingDiffMsec),
                "BreakNote.NoteCheck");
#endif
            orig_NoteCheck();
#if DEBUG
            SoflanDiagnostic.AfterJudgeCheck(
                diagnosticProbe,
                JudgeResult,
                NoteJudge.ETiming.End,
                EndFlag,
                SoflanRuntimeTime.FromGameMsecBoundary(JudgeTimingDiffMsec));
#endif

            if (breakIsInSoflan && CheckSupportSoflan() && !EndFlag)
            {
                var absDiffTime = Math.Abs(GetBreakSoflanPositionDiff());
                var scale = Mathf.Clamp01(
                    (float)((2d * breakVisualDefaultDistance - absDiffTime)
                        / breakVisualDefaultDistance));
                scale *= Singleton<GamePlayManager>.Instance.GetGameScore(MonitorId).UserOption.NoteSize.GetValue();
                NoteObj.transform.localScale = new Vector3(scale, scale, 0f);
            }
        }

        private double GetBreakSoflanPositionDiff()
        {
            var currentSoflanPosition = breakSoflanManager.GetCurrentSoflanPositionWithOffsetsCached(
                MonitorId,
                SoflanGameClock.CurrentTime,
                breakMaiBugAdjust,
                breakSoflanGroup);
            return breakNoteSoflanPosition.DeltaTo(currentSoflanPosition);
        }

        private bool CheckSupportSoflan()
        {
            switch (NoteKind.getBaseType())
            {
                case NotesTypeID.BaseDef.Tap:
                    return true;
                default:
                    return false;
            }
        }
    }
}
