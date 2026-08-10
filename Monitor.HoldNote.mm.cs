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
    public class patch_HoldNote : HoldNote
    {
        private SoflanManager holdSoflanManager;
        private bool holdIsInSoflan;
        private int holdSoflanGroup;
        private SoflanPosition holdHeadSoflanPosition;
        private SoflanPosition holdTailSoflanPosition;
        private TimeSpan holdMaiBugAdjust;

        public extern void orig_Initialize(NoteData note);

        public void Initialize(NoteData note)
        {
            orig_Initialize(note);

            holdSoflanManager = Singleton<SoflanManager>.Instance;
            holdIsInSoflan = holdSoflanManager.containsSoflans(MonitorId);
            if (holdIsInSoflan)
            {
                holdSoflanGroup = holdSoflanManager.getNoteSoflanGroup(MonitorId, NoteIndex);
                holdHeadSoflanPosition = holdSoflanManager.GetNoteSoflanPosition(
                    MonitorId,
                    NoteIndex,
                    SoflanRuntimeTime.FromGameMsecBoundary(AppearMsec),
                    holdSoflanGroup);
                holdTailSoflanPosition = holdSoflanManager.GetNoteEndSoflanPosition(
                    MonitorId,
                    NoteIndex,
                    SoflanRuntimeTime.FromGameMsecBoundary(TailMsec),
                    holdSoflanGroup);
            }
            else
            {
                holdSoflanGroup = 0;
                holdHeadSoflanPosition = new SoflanPosition(AppearMsec);
                holdTailSoflanPosition = new SoflanPosition(TailMsec);
            }
            holdMaiBugAdjust = SoflanVisualTiming.GetMaiBugAdjust(
                note.type.getEnum(),
                SoflanRuntimeTime.FromMilliseconds(2d * DefaultMsec));
        }

        public extern void orig_Execute();

        public void Execute()
        {
            if (holdIsInSoflan && CheckSupportSoflan())
            {
                var currentTime = SoflanGameClock.CurrentTime;
                var currentSoflanPosition = holdSoflanManager.GetCurrentSoflanPositionWithOffsetsCached(
                    MonitorId,
                    currentTime,
                    holdMaiBugAdjust,
                    holdSoflanGroup);

                var headDiffPosition = holdHeadSoflanPosition.DeltaTo(currentSoflanPosition);
                var tailDiffPosition = holdTailSoflanPosition.DeltaTo(currentSoflanPosition);

                ExecuteSoflanVisual(headDiffPosition, tailDiffPosition, currentTime);
#if DEBUG
                const string diagnosticSource = "HoldNote.ExecuteSoflan";
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
            const string diagnosticSource = "HoldNote.NoteCheck";
            var diagnosticProbe = BeginNoteCheckDiagnostics(diagnosticSource);
#endif
            orig_NoteCheck();
#if DEBUG
            EndNoteCheckDiagnostics(diagnosticProbe, diagnosticSource);
#endif

            if (holdIsInSoflan && CheckSupportSoflan())
            {
                var currentSoflanPosition = holdSoflanManager.GetCurrentSoflanPositionWithOffsetsCached(
                    MonitorId,
                    SoflanGameClock.CurrentTime,
                    holdMaiBugAdjust,
                    holdSoflanGroup);

                ApplySoflanScale(holdHeadSoflanPosition.DeltaTo(currentSoflanPosition));
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

            SoflanDiagnostic.VisualSample(
                MonitorId,
                NoteIndex,
                NoteKind,
                holdSoflanGroup,
                currentTime,
                new SoflanPosition(holdHeadSoflanPosition.Value - headDiffPosition),
                holdHeadSoflanPosition,
                headDiffPosition,
                (float)headY,
                scaleStartDistance,
                moveStartDistance,
                (int)NoteStat,
                false,
                "HoldNote.ExecuteSoflanVisual");
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
