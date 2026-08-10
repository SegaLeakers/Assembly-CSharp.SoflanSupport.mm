#pragma warning disable CS0626
using DB;
using Manager;
using SoflanSupport;

namespace Monitor
{
    public class patch_TouchHoldC : TouchHoldC
    {
#if DEBUG
        private SoflanDiagnostic.JudgeProbe __SoflanBeginNoteCheckDiagnostics()
        {
            return SoflanDiagnostic.BeforeJudgeCheck(
                MonitorId,
                NoteIndex,
                NoteKind,
                ButtonId,
                SoflanDiagnostic.GetTouchAreaIndex(TouchArea, ButtonId),
                false,
                SoflanRuntimeTime.FromGameMsecBoundary(AppearMsec),
                SoflanRuntimeTime.FromGameMsecBoundary(TailMsec),
                JudgeType,
                SoflanRuntimeTime.FromGameMsecBoundary(GetJudgeStartMsec()),
                SoflanRuntimeTime.FromGameMsecBoundary(GetJudgeEndMsec()),
                JudgeResult,
                GetJudgeHeadResult(),
                EndFlag,
                IsJudgeNote(),
                SoflanRuntimeTime.FromGameMsecBoundary(JudgeTimingDiffMsec),
                "TouchHoldC.NoteCheck");
        }

        private void __SoflanEndNoteCheckDiagnostics(
            SoflanDiagnostic.JudgeProbe diagnosticProbe)
        {
            SoflanDiagnostic.AfterJudgeCheck(
                diagnosticProbe,
                JudgeResult,
                GetJudgeHeadResult(),
                EndFlag,
                SoflanRuntimeTime.FromGameMsecBoundary(JudgeTimingDiffMsec));
            SoflanDiagnostic.HoldState(
                MonitorId,
                NoteIndex,
                GetJudgeHeadResult(),
                HeadJudged,
                BodyOn,
                LastHoldState,
                TriggerOn,
                SoflanRuntimeTime.FromMilliseconds(HoldReleaseTime),
                EndFlag,
                "TouchHoldC.NoteCheck");
        }
#endif
    }
}
