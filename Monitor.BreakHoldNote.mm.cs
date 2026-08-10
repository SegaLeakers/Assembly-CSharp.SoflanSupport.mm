#pragma warning disable CS0626
using DB;
using MAI2.Util;
using Manager;
using OngekiFumenEditor.Core.Utils;
using SoflanSupport;
using System;
using UnityEngine;

namespace Monitor
{
    public class patch_BreakHoldNote : BreakHoldNote
    {
        private SoflanManager breakHoldSoflanManager;
        private bool breakHoldIsInSoflan;
        private int breakHoldSoflanGroup;
        private SoflanPosition breakHoldHeadSoflanPosition;
        private SoflanPosition breakHoldTailSoflanPosition;
        private TimeSpan breakHoldMaiBugAdjust;

        public extern void orig_Initialize(NoteData note);

        public void Initialize(NoteData note)
        {
            orig_Initialize(note);

            breakHoldSoflanManager = Singleton<SoflanManager>.Instance;
            breakHoldIsInSoflan = breakHoldSoflanManager.containsSoflans(MonitorId);
            if (breakHoldIsInSoflan)
            {
                breakHoldSoflanGroup = breakHoldSoflanManager.getNoteSoflanGroup(MonitorId, NoteIndex);
                breakHoldHeadSoflanPosition = breakHoldSoflanManager.GetNoteSoflanPosition(
                    MonitorId,
                    NoteIndex,
                    SoflanRuntimeTime.FromGameMsecBoundary(AppearMsec),
                    breakHoldSoflanGroup);
                breakHoldTailSoflanPosition = breakHoldSoflanManager.GetNoteEndSoflanPosition(
                    MonitorId,
                    NoteIndex,
                    SoflanRuntimeTime.FromGameMsecBoundary(TailMsec),
                    breakHoldSoflanGroup);
            }
            else
            {
                breakHoldSoflanGroup = 0;
                breakHoldHeadSoflanPosition = new SoflanPosition(AppearMsec);
                breakHoldTailSoflanPosition = new SoflanPosition(TailMsec);
            }
            breakHoldMaiBugAdjust = SoflanVisualTiming.GetMaiBugAdjust(
                note.type.getEnum(),
                SoflanRuntimeTime.FromMilliseconds(2d * DefaultMsec));
        }

        public extern void orig_Execute();

        public void Execute()
        {
            if (breakHoldIsInSoflan && CheckSupportSoflan())
            {
                var currentTime = SoflanGameClock.CurrentTime;
                var currentSoflanPosition = breakHoldSoflanManager.GetCurrentSoflanPositionWithOffsetsCached(
                    MonitorId,
                    currentTime,
                    breakHoldMaiBugAdjust,
                    breakHoldSoflanGroup);

                var headDiffPosition = breakHoldHeadSoflanPosition.DeltaTo(currentSoflanPosition);
                var tailDiffPosition = breakHoldTailSoflanPosition.DeltaTo(currentSoflanPosition);

                ExecuteSoflanVisual(headDiffPosition, tailDiffPosition, currentTime);
#if DEBUG
                const string diagnosticSource = "BreakHoldNote.ExecuteSoflan";
                var diagnosticProbe = BeginNoteCheckDiagnostics(diagnosticSource);
#endif
                orig_NoteCheck();
#if DEBUG
                EndNoteCheckDiagnostics(diagnosticProbe, diagnosticSource);
#endif
                ApplySoflanScale(headDiffPosition);
                return;
            }

            orig_Execute();
        }

        protected extern void orig_NoteCheck();

        protected void NoteCheck()
        {
#if DEBUG
            const string diagnosticSource = "BreakHoldNote.NoteCheck";
            var diagnosticProbe = BeginNoteCheckDiagnostics(diagnosticSource);
#endif
            orig_NoteCheck();
#if DEBUG
            EndNoteCheckDiagnostics(diagnosticProbe, diagnosticSource);
#endif

            if (breakHoldIsInSoflan && CheckSupportSoflan())
            {
                var currentSoflanPosition = breakHoldSoflanManager.GetCurrentSoflanPositionWithOffsetsCached(
                    MonitorId,
                    SoflanGameClock.CurrentTime,
                    breakHoldMaiBugAdjust,
                    breakHoldSoflanGroup);

                ApplySoflanScale(breakHoldHeadSoflanPosition.DeltaTo(currentSoflanPosition));
            }
        }

#if DEBUG
        private SoflanDiagnostic.JudgeProbe BeginNoteCheckDiagnostics(string source)
        {
            return SoflanDiagnostic.BeforeJudgeCheck(
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
                GetJudgeHeadResult(),
                EndFlag,
                IsJudgeNote(),
                SoflanRuntimeTime.FromGameMsecBoundary(JudgeTimingDiffMsec),
                source);
        }

        private void EndNoteCheckDiagnostics(
            SoflanDiagnostic.JudgeProbe diagnosticProbe,
            string source)
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
                TrigetOn,
                SoflanRuntimeTime.FromMilliseconds(HoldReleaseTime),
                EndFlag,
                source);
        }
#endif

        private void ExecuteSoflanVisual(
            double headDiffPosition,
            double tailDiffPosition,
            TimeSpan currentTime)
        {
            if (EndFlag)
            {
                return;
            }

            UpdateHoldEffectVisual();

            var moveStartDistance = (double)DefaultMsec;
            var scaleStartDistance = 2d * moveStartDistance;
            var headY = GetHoldHeadYPositionSoflan(
                headDiffPosition,
                moveStartDistance,
                scaleStartDistance);

            if (headY >= EndPos)
            {
                headY = EndPos;
            }

            if (headDiffPosition > moveStartDistance)
            {
                SpriteRender.size = new Vector2(SpriteRender.size.x, DefaultHeight);
                NoteObj.transform.localPosition = new Vector3(0f, headY, GetBaseZPosition());
                EndPointObj.transform.localPosition = new Vector3(0f, headY, GetBaseZPosition());
            }
            else
            {
                if (SoflanRuntimeTime.FromGameMsecBoundary(TailMsec) <= currentTime)
                {
                    NoteObj.transform.localPosition = new Vector3(0f, EndPos, GetBaseZPosition());
                    SpriteRender.size = new Vector2(SpriteRender.size.x, DefaultHeight);
                }
                else if (tailDiffPosition <= moveStartDistance)
                {
                    if (!EndPointObj.activeSelf)
                    {
                        EndPointObj.SetActive(value: true);
                    }

                    var tailY = GetHoldEndpointYPositionSoflan(tailDiffPosition, moveStartDistance);
                    var bodyLength = Math.Max(0d, headY - tailY);

                    SpriteRender.size = new Vector2(SpriteRender.size.x, (float)(bodyLength + DefaultHeight));
                    NoteObj.transform.localPosition = new Vector3(0f, (float)(headY - bodyLength / 2d), GetBaseZPosition());
                    EndPointObj.transform.localPosition = new Vector3(0f, (float)tailY, GetBaseZPosition());
                }
                else
                {
                    var bodyLength = Math.Max(0d, headY - StartPos);

                    SpriteRender.size = new Vector2(SpriteRender.size.x, (float)(bodyLength + DefaultHeight));
                    NoteObj.transform.localPosition = new Vector3(0f, (float)(headY - bodyLength / 2d), GetBaseZPosition());
                    EndPointObj.transform.localPosition = new Vector3(0f, StartPos, GetBaseZPosition());
                }
            }

            SpriteRenderEx.size = SpriteRender.size;
            EffectSprite.size = SpriteRender.size;
            BreakEffectSprite.size = SpriteRender.size;

            SoflanDiagnostic.VisualSample(
                MonitorId,
                NoteIndex,
                NoteKind,
                breakHoldSoflanGroup,
                currentTime,
                new SoflanPosition(breakHoldHeadSoflanPosition.Value - headDiffPosition),
                breakHoldHeadSoflanPosition,
                headDiffPosition,
                (float)headY,
                scaleStartDistance,
                moveStartDistance,
                (int)NoteStat,
                false,
                "BreakHoldNote.ExecuteSoflanVisual");
        }

        private float GetHoldHeadYPositionSoflan(
            double diffPosition,
            double moveStartDistance,
            double scaleStartDistance)
        {
            if (diffPosition > scaleStartDistance)
            {
                if (NoteGuideTrans != null)
                {
                    NoteGuideTrans.localScale = new Vector3(0f, 0f, 1f);
                    GuideObj.SetAlpha(0f);
                }
            }
            else if (diffPosition > moveStartDistance)
            {
                NoteStat = NoteStatus.Scale;
                if (NoteGuideTrans != null)
                {
                    var scaleProgress = (float)SoflanVisualMath.MapValue(
                        diffPosition,
                        scaleStartDistance,
                        moveStartDistance,
                        0d,
                        1d);
                    NoteGuideTrans.localScale = new Vector3(0.25f, 0.25f, 1f);
                    GuideObj.SetAlpha(scaleProgress);
                }
            }
            else
            {
                NoteStat = NoteStatus.Move;
                if (NoteGuideTrans != null)
                {
                    var moveProgress = (float)Math.Max(0d, SoflanVisualMath.MapValue(
                        diffPosition,
                        0d,
                        moveStartDistance,
                        1d,
                        0d,
                        false));
                    float finalScale = 0.25f + 0.75f * moveProgress;
                    float guideScale = !GuideStop || finalScale <= 1f ? finalScale : 1f;

                    NoteGuideTrans.localScale = new Vector3(guideScale, guideScale, 1f);
                    GuideObj.SetAlpha(1f);
                }
            }

            return GetHoldYPositionSoflan(diffPosition, moveStartDistance);
        }

        private float GetHoldEndpointYPositionSoflan(double diffPosition, double moveStartDistance)
        {
            return GetHoldYPositionSoflan(diffPosition, moveStartDistance);
        }

        private float GetHoldYPositionSoflan(double diffPosition, double moveStartDistance)
        {
            var insideY = (double)StartPos;
            var outsideY = EndPos + (double)(EndPos - StartPos);
            var y = SoflanVisualMath.MapValue(
                diffPosition,
                -moveStartDistance,
                moveStartDistance,
                outsideY,
                insideY);

            return (float)Math.Max(StartPos, Math.Min(EndPos, y));
        }

        private void ApplySoflanScale(double headDiffPosition)
        {
            if (EndFlag)
            {
                return;
            }

            var moveStartDistance = (double)DefaultMsec;
            var scaleStartDistance = 2d * moveStartDistance;
            float scale = headDiffPosition <= moveStartDistance
                ? 1f
                : Mathf.Clamp01((float)((scaleStartDistance - headDiffPosition) / moveStartDistance));
            float noteSize = Singleton<GamePlayManager>.Instance.GetGameScore(MonitorId).UserOption.NoteSize.GetValue();

            NoteObj.transform.localScale = new Vector3(scale * noteSize, scale, 0f);
            EndPointObj.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void UpdateHoldEffectVisual()
        {
            if (HoldBodyOnFlg)
            {
                EffectSprite.color = new Color(1f, 1f, 1f, Mathf.Sin(GameManager.GetGameFrame() * 0.4f) * 0.25f + 0.25f);
            }
            else
            {
                EffectSprite.color = new Color(1f, 1f, 1f, 0f);
            }

            if (IsNoteCheckTimeHoldHeadIgnoreJudgeWait())
            {
                BreakEffectSprite.color = new Color(1f, 1f, 1f, 0f);
            }
            else
            {
                BreakEffectSprite.color = new Color(1f, 1f, 1f, Mathf.Sin(GameManager.GetGameFrame() * 0.19999501f) * 0.5f);
            }
        }

        private bool CheckSupportSoflan()
        {
            switch (NoteKind.getBaseType())
            {
                case NotesTypeID.BaseDef.Hold:
                    return true;
                default:
                    return false;
            }
        }
    }
}
