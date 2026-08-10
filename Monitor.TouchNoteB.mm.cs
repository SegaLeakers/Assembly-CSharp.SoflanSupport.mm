#pragma warning disable CS0626
using MAI2.Util;
using Manager;
using SoflanSupport;
using System;
using UnityEngine;

namespace Monitor
{
    public class patch_TouchNoteB : TouchNoteB
    {
        private SoflanManager touchSoflanManager;
        private bool touchIsInSoflan;
        private int touchSoflanGroup;
        private SoflanPosition touchNoteSoflanPosition;

        public extern void orig_Initialize(NoteData note);

        public override void Initialize(NoteData note)
        {
            orig_Initialize(note);

            var noteKind = note.type.getEnum();
            if (noteKind == NotesTypeID.Def.TouchTap)
                NoteKind = NotesTypeID.Def.TouchTap;

            touchSoflanManager = Singleton<SoflanManager>.Instance;
            touchIsInSoflan = touchSoflanManager.containsSoflans(MonitorId)
                && noteKind == NotesTypeID.Def.TouchTap;
            if (touchIsInSoflan)
            {
                touchSoflanGroup = touchSoflanManager.getNoteSoflanGroup(MonitorId, NoteIndex);
                touchNoteSoflanPosition = touchSoflanManager.GetNoteSoflanPosition(
                    MonitorId,
                    NoteIndex,
                    SoflanRuntimeTime.FromGameMsecBoundary(AppearMsec),
                    touchSoflanGroup);
            }
            else
            {
                touchSoflanGroup = 0;
                touchNoteSoflanPosition = new SoflanPosition(AppearMsec);
            }
        }

        protected extern float orig_GetNoteYPosition();

        protected override float GetNoteYPosition()
        {
            if (touchIsInSoflan && CheckSupportSoflan())
                return GetTouchNoteYPositionSoflan();

            return orig_GetNoteYPosition();
        }

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
                NoteJudge.ETiming.End,
                EndFlag,
                IsJudgeNote(),
                SoflanRuntimeTime.FromGameMsecBoundary(JudgeTimingDiffMsec),
                "TouchNoteB.NoteCheck");
        }

        private void __SoflanEndNoteCheckDiagnostics(
            SoflanDiagnostic.JudgeProbe diagnosticProbe)
        {
            SoflanDiagnostic.AfterJudgeCheck(
                diagnosticProbe,
                JudgeResult,
                NoteJudge.ETiming.End,
                EndFlag,
                SoflanRuntimeTime.FromGameMsecBoundary(JudgeTimingDiffMsec));
        }
#endif

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

        private float GetTouchNoteYPositionSoflan()
        {
            var runtimeTime = SoflanGameClock.CurrentTime;
            var currentSoflanPosition = touchSoflanManager.GetCurrentSoflanPositionCached(
                MonitorId,
                runtimeTime,
                touchSoflanGroup);
            var touchDispDistance = DefaultMsec * 0.25d;
            var soflanStartPosition = touchNoteSoflanPosition.Value - DefaultMsec - touchDispDistance;
            var diffPosition = touchNoteSoflanPosition.DeltaTo(currentSoflanPosition);

            NoteStat = NoteStatus.Move;
            if (currentSoflanPosition.Value <= soflanStartPosition)
            {
                NoteStat = NoteStatus.Init;
                SpriteRender.color = new Color(1f, 1f, 1f, 0f);
                for (int i = 0; i < DefaultCorlsPos.Length; i++)
                {
                    ColorsObject[i].color = new Color(1f, 1f, 1f, 0f);
                }
                SoflanDiagnostic.VisualSample(
                    MonitorId,
                    NoteIndex,
                    NoteKind,
                    touchSoflanGroup,
                    runtimeTime,
                    currentSoflanPosition,
                    touchNoteSoflanPosition,
                    diffPosition,
                    0f,
                    DefaultMsec + touchDispDistance,
                    DefaultMsec,
                    (int)NoteStat,
                    false,
                    "TouchNoteB.GetNoteYPosition");
                return 0f;
            }

            if (currentSoflanPosition.Value <= soflanStartPosition + touchDispDistance)
            {
                NoteStat = NoteStatus.Scale;
                var fadeProgressValue = (currentSoflanPosition.Value - soflanStartPosition)
                    / touchDispDistance;
                var fadeProgress = (float)Math.Min(1d, fadeProgressValue);
                if (fadeProgress > 1f)
                {
                    fadeProgress = 1f;
                }
                SpriteRender.color = new Color(1f, 1f, 1f, 1f);
                for (int j = 0; j < DefaultCorlsPos.Length; j++)
                {
                    ColorsObject[j].color = new Color(1f, 1f, 1f, fadeProgress);
                }
                SoflanDiagnostic.VisualSample(
                    MonitorId,
                    NoteIndex,
                    NoteKind,
                    touchSoflanGroup,
                    runtimeTime,
                    currentSoflanPosition,
                    touchNoteSoflanPosition,
                    diffPosition,
                    fadeProgress,
                    DefaultMsec + touchDispDistance,
                    DefaultMsec,
                    (int)NoteStat,
                    false,
                    "TouchNoteB.GetNoteYPosition");
                return fadeProgress;
            }

            NoteStat = NoteStatus.Move;
            var gatherProgressValue = (currentSoflanPosition.Value
                - (soflanStartPosition + touchDispDistance)
                + DispAdjustFlame * 16.666666d) / DefaultMsec;
            gatherProgressValue = 3.5d * Math.Pow(gatherProgressValue, 4d)
                                - 3.75d * Math.Pow(gatherProgressValue, 3d)
                                + 1.45d * Math.Pow(gatherProgressValue, 2d)
                                - 0.05d * gatherProgressValue
                                + 0.0005d;
            var gatherProgress = (float)Math.Min(1d, gatherProgressValue);
            SpriteRender.color = new Color(1f, 1f, 1f, 1f);
            for (int k = 0; k < DefaultCorlsPos.Length; k++)
            {
                ColorsObject[k].transform.localPosition = Vector3.Lerp(DefaultCorlsPos[k], Vector3.zero, gatherProgress);
                ColorsObject[k].color = new Color(1f, 1f, 1f, 1f);
            }
            if (null != NoticeObject)
            {
                NoticeObject.SetActive(touchNoteSoflanPosition.Value <= currentSoflanPosition.Value);
            }
            SoflanDiagnostic.VisualSample(
                MonitorId,
                NoteIndex,
                NoteKind,
                touchSoflanGroup,
                runtimeTime,
                currentSoflanPosition,
                touchNoteSoflanPosition,
                diffPosition,
                gatherProgress,
                DefaultMsec + touchDispDistance,
                DefaultMsec,
                (int)NoteStat,
                false,
                "TouchNoteB.GetNoteYPosition");
            return 1f;
        }
    }
}
