#pragma warning disable CS0626
// patch_Monitor.NoteBase — 对应 head commit 2a7a4a4 中 Monitor/NoteBase.cs 的改动.
// 所有被访问的 NoteBase 成员均为 protected, patch_NoteBase : NoteBase 可直接访问, 无需公开化.
// 改动:
// - 新增字段 soflanManager / isInSoflan / noteSoflanPosition (在 Initialize 中赋值)
// - Initialize() 末尾追加 soflan 初始化 (orig_ 包装)
// - NoteCheck() 末尾追加 soflan 缩放重算 (orig_ 包装)
// - EndNote() 末尾追加日志 (orig_ 包装)
// - GetNoteYPosition() 开头追加 soflan 早返回 (orig_ 包装)
// - 新增 checkSupportSoflan / GetSoflanPositionDiff / GetNoteYPosition_soflan (verbatim)
// - 放弃 DumpCurrent (依赖 GameCtrl.DumpCurrent 的 private 字段访问)
using DB;
using MAI2.Util;
using Manager;
using OngekiFumenEditor.Core.Utils;
using SoflanSupport;
using System;
using UnityEngine;

namespace Monitor
{
    public abstract class patch_NoteBase : NoteBase
    {
        private SoflanManager soflanManager;
        private bool isInSoflan;
        private int noteSoflanGroup;
        private SoflanPosition noteSoflanPosition;
        private bool isFixedSoflanToUnifiedSpeed;
        private float fixedSoflanUnifiedSpeed;
        private double visualDefaultDistance;
        private TimeSpan maiBugAdjust;

#if DEBUG
        // --- 调试面板选中 (右键点击 Tap) ---
        // 选中状态由 SoflanPanelBehaviour._selectedNote 集中维护 (避免 patch 新增字段跨类访问的编译期鸿沟);
        // 本类通过 SoflanPanelBehaviour.IsNoteSelected(this) 查询。
        private Color _origSpriteColor;         // 选中前的原 sprite color, 取消选中时恢复
        private bool _colorSaved;
        private SoflanPosition _rawCurrentSoflanPosition;
        private SoflanPosition _adjustedCurrentSoflanPosition;
#endif

        public extern void orig_Initialize(NoteData note);

        public void Initialize(NoteData note)
        {
            orig_Initialize(note);

#if DEBUG
            _colorSaved = false;
            if (Setting.EnableSoflanDebugPanel)
            {
                // 池化复用时清除旧选中，并为右键选择补 2D collider。
                SoflanPanelBehaviour.OnNoteReinitialized(this);
                if (NoteObj != null && NoteObj.GetComponent<Collider2D>() == null)
                    NoteObj.AddComponent<BoxCollider2D>();
            }
#endif

            //Soflan Support
            soflanManager = Singleton<SoflanManager>.Instance;
            isInSoflan = soflanManager.containsSoflans(MonitorId);
            if (isInSoflan)
            {
                noteSoflanGroup = soflanManager.getNoteSoflanGroup(MonitorId, NoteIndex);
                noteSoflanPosition = soflanManager.GetNoteSoflanPosition(
                    MonitorId,
                    NoteIndex,
                    SoflanRuntimeTime.FromGameMsecBoundary(AppearMsec),
                    noteSoflanGroup);
            }
            else
            {
                noteSoflanGroup = 0;
                noteSoflanPosition = new SoflanPosition(AppearMsec);
            }

            var fixedNote = (patch_NoteData)note;
            isFixedSoflanToUnifiedSpeed = fixedNote.isFixedSoflanToUnifiedSpeed
                && FixedSoflan.IsSupportedTapKind(note.type.getEnum());
            fixedSoflanUnifiedSpeed = fixedNote.fixedSoflanUnifiedSpeed > 0f
                ? fixedNote.fixedSoflanUnifiedSpeed
                : FixedSoflan.DefaultUnifiedSpeed;
            visualDefaultDistance = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetDefaultTime(fixedSoflanUnifiedSpeed).TotalMilliseconds
                : DefaultMsec;
            maiBugAdjust = SoflanVisualTiming.GetMaiBugAdjust(
                note.type.getEnum(),
                SoflanRuntimeTime.FromMilliseconds(2d * visualDefaultDistance));

            RestoreOriginalLaneJudgeOrder();

            SoflanDiagnostic.ObjectInitialized(
                MonitorId,
                note,
                SoflanRuntimeTime.FromGameMsecBoundary(AppearMsec),
                SoflanRuntimeTime.FromGameMsecBoundary(TailMsec),
                SoflanRuntimeTime.FromMilliseconds(visualDefaultDistance),
                noteSoflanGroup,
                isFixedSoflanToUnifiedSpeed,
                fixedSoflanUnifiedSpeed,
                noteSoflanPosition,
                maiBugAdjust,
                "NoteBase.Initialize");

        }

        private void RestoreOriginalLaneJudgeOrder()
        {
            if (!isInSoflan)
                return;

            var noteTransform = transform;
            var laneTransform = noteTransform.parent;
            if (laneTransform == null)
                return;

            // 原版按 NoteData/indexNote 顺序注册，并依赖 launcher 的 sibling 顺序
            // 决定同 lane 里哪个物件能判定。反向 Soflan 会打乱注册时间，因此在
            // 物件创建后恢复与原版 NoteIndex 一致的 sibling 顺序。
            var siblingIndex = SoflanJudgeOrder.GetSiblingIndex(
                NoteIndex,
                laneTransform.childCount,
                index =>
                {
                    var siblingTransform = laneTransform.GetChild(index);
                    if (siblingTransform == noteTransform)
                        return null;

                    var siblingNote = siblingTransform.GetComponent<NoteBase>();
                    return siblingNote != null && siblingNote.gameObject.activeSelf
                        ? siblingNote.GetNoteIndex()
                        : (int?)null;
                });
            noteTransform.SetSiblingIndex(siblingIndex);
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
                "NoteBase.NoteCheck");
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

            if (isInSoflan && checkSupportSoflan() && !EndFlag)
            {
                //recalculate scale in soflan
                /* absDiffTime数值含义:

                           scale=0       -----  2 * visualDefaultMsec
                                           |
                                           |
                                           |
                           scale=1       -----      visualDefaultMsec
                                           |
                                           |
                                           |
                           scale=1       -----      0
                */
                var absDiffTime = Math.Abs(GetSoflanPositionDiff());

                var scale = Mathf.Clamp01((float)((2d * visualDefaultDistance - absDiffTime)
                    / visualDefaultDistance));
                scale *= Singleton<GamePlayManager>.Instance.GetGameScore(MonitorId).UserOption.NoteSize.GetValue();
                NoteObj.transform.localScale = new Vector3(scale, scale, 0f);
            }

#if DEBUG
            // 调试选中视觉: 选中时高亮黄 + alpha 0.5~1 呼吸; 取消选中恢复原色 (仅恢复一次).
            if (SpriteRender != null)
            {
                if (SoflanPanelBehaviour.IsNoteSelected(this))
                {
                    if (!_colorSaved) { _origSpriteColor = SpriteRender.color; _colorSaved = true; }
                    float a = Mathf.PingPong(Time.time * 2f, 0.5f) + 0.5f;  // 0.5~1 来回呼吸
                    SpriteRender.color = new Color(1f, 1f, 0f, a);           // 高亮黄
                }
                else if (_colorSaved)
                {
                    SpriteRender.color = _origSpriteColor;
                    _colorSaved = false;
                }
            }
#endif
        }

        private double GetSoflanPositionDiff()
        {
            return GetSoflanPositionDiff(SoflanGameClock.CurrentTime);
        }

        private double GetSoflanPositionDiff(TimeSpan currentTime)
        {
            var currentSoflanPosition = soflanManager.GetCurrentSoflanPositionWithOffsetsCached(
                MonitorId,
                currentTime,
                maiBugAdjust,
                noteSoflanGroup);
#if DEBUG
            if (SoflanPanelBehaviour.IsNoteSelected(this))
            {
                _rawCurrentSoflanPosition = soflanManager.GetCurrentSoflanPositionCached(
                    MonitorId,
                    currentTime,
                    noteSoflanGroup);
                _adjustedCurrentSoflanPosition = currentSoflanPosition;
            }
#endif
            return noteSoflanPosition.DeltaTo(currentSoflanPosition);
        }

        protected extern void orig_EndNote();

        protected void EndNote()
        {
            orig_EndNote();

#if DEBUG
            // 被选中的 note 结束时: 恢复原色 + 通知面板清选中与显示数据.
            if (SoflanPanelBehaviour.IsNoteSelected(this))
            {
                if (_colorSaved && SpriteRender != null)
                {
                    SpriteRender.color = _origSpriteColor;
                    _colorSaved = false;
                }
                SoflanPanelBehaviour.OnSelectedNoteEnded();
            }
#endif
        }

        protected extern float orig_GetNoteYPosition();

        protected virtual float GetNoteYPosition()
        {
            if (isInSoflan && checkSupportSoflan())
                return GetNoteYPosition_soflan();

            return orig_GetNoteYPosition();
        }

        private bool checkSupportSoflan()
        {
            switch (NoteKind.getBaseType())
            {
                case NotesTypeID.BaseDef.Tap:
                    return true;
                default:
                    return false;
            }
        }

        protected float GetNoteYPosition_soflan()
        {
            /* diffTime数值含义:
                         guideScale=0    -----   inf
                                           |
                                           |
                                           |
                         guideScale=1    -----   scaleStartTime = 2 * visualDefaultMsec
                                           |
                                           |
                                           |
              y=120      guideScale=1    -----   moveStartTime = visualDefaultMsec
                                           |
                                           |
                                           |
              y=400       scale=1        -----      0
                                           |
                                           |
                                           |
              y=680       scale=1        -----   -moveStartTime = -visualDefaultMsec


            */
            var currentTime = SoflanGameClock.CurrentTime;
            var diffTime = GetSoflanPositionDiff(currentTime);
            var absDiffTime = Math.Abs(diffTime);

            var scaleStartTime = 2d * visualDefaultDistance;
            var moveStartTime = visualDefaultDistance;
            var fixedMotionProgress = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetMotionProgress(diffTime, fixedSoflanUnifiedSpeed)
                : 0f;
            var fixedScaleProgress = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetScaleProgress(absDiffTime, fixedSoflanUnifiedSpeed)
                : 0f;

            /*  强制重新计算Guide物件缩放
                diffTime = moveStartTime             0             -moveStartTime
                             ---|--------------------|--------------------|---
                  finalScale = 0.25                  1                   1.75
                              StartPos              EndPos      EndPos + (EndPos - StartPos)
             */

            var guideScaleAdj = 0; //(-1f / 120f) * (speedRatio - 1f) * 0.75f;

            /*  强制重新计算物件pos位置
                diffTime = moveStartTime             0             -moveStartTime
                             ---|--------------------|--------------------|---
                      soflanY = 120                  400                  680
                             StartPos              EndPos      EndPos + (EndPos - StartPos)
             */
            var insideY = (double)StartPos;
            var outsideY = EndPos + (double)(EndPos - StartPos);

            var soflanY = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetYFromMotionProgress(StartPos, EndPos, fixedMotionProgress)
                : SoflanVisualMath.MapValue(diffTime, -moveStartTime, moveStartTime, outsideY, insideY);
            // MaiBug 的音频毫秒偏移已在 GetSoflanPositionDiff 中经过 Soflan 时间轴映射；
            // 这里不再叠加独立坐标偏移，否则会重复补偿。
            var adjustedSoflanY = soflanY;

            var clipedSoflanY = Math.Max(120d, Math.Min(680d, adjustedSoflanY));

            var moveProgress = (clipedSoflanY - StartPos) / (EndPos - StartPos);
            moveProgress = Math.Max(0, moveProgress); // always >= 0

            var guideScale = 0.75f * moveProgress;
            var adjustedGuideScale = guideScale + guideScaleAdj;
            var finalScale = (float)(0.25d + adjustedGuideScale);

            if (absDiffTime > scaleStartTime)
            {
                if (NoteGuideTrans != null)
                {
                    NoteGuideTrans.localScale = new Vector3(0f, 0f, 1f);
                    GuideObj.SetAlpha(0);
                }
            }
            else if (absDiffTime > moveStartTime)
            {
                NoteStat = NoteStatus.Scale;
                if (NoteGuideTrans != null)
                {
                    var scaleProgress = isFixedSoflanToUnifiedSpeed
                        ? fixedScaleProgress
                        : (float)SoflanVisualMath.MapValue(absDiffTime, scaleStartTime, moveStartTime, 0, 1);
                    NoteGuideTrans.localScale = new Vector3(finalScale, finalScale, 1f);
                    GuideObj.SetAlpha(scaleProgress);
                }
            }
            else
            {
                NoteStat = NoteStatus.Move;
                if (NoteGuideTrans != null)
                {
                    NoteGuideTrans.localScale = new Vector3(finalScale, finalScale, 1f);
                    GuideObj.SetAlpha(1);
                }
            }

#if DEBUG
            // 调试面板: 选中本 note 时, 把所有计算变量导出到面板 (struct 值类型, 零堆分配).
            if (SoflanPanelBehaviour.IsNoteSelected(this))
            {
                SoflanPanelBehaviour.SelectedData = new SoflanPanelBehaviour.SelectedNoteData
                {
                    NoteIndex = NoteIndex,
                    DiffPosition = diffTime,
                    AbsDiffPosition = absDiffTime,
                    ScaleStartDistance = scaleStartTime,
                    MoveStartDistance = moveStartTime,
                    NoteStat = NoteStat,
                    MoveProgress = (float)moveProgress,
                    FinalScale = finalScale,
                    InsideY = (float)insideY,
                    OutsideY = (float)outsideY,
                    SoflanY = (float)soflanY,
                    ClipedSoflanY = (float)clipedSoflanY,
                    IsFixedSoflanToUnifiedSpeed = isFixedSoflanToUnifiedSpeed,
                    FixedSoflanUnifiedSpeed = fixedSoflanUnifiedSpeed,
                    FixedMotionProgress = fixedMotionProgress,
                    FixedScaleProgress = fixedScaleProgress,
                    MaiBugAdjustEnabled = Setting.EnableSoflanMaiBugAdjust,
                    MaiBugAdjust = maiBugAdjust,
                    MonitorId = MonitorId,
                    RuntimeCurrentTime = currentTime,
                    RuntimeChartOffset = soflanManager.getRuntimeChartOffset(MonitorId),
                    AdjustedRawCurrentTime = SoflanRuntimeTime.ToRawChartAudioTime(
                        currentTime,
                        soflanManager.getRuntimeChartOffset(MonitorId),
                        maiBugAdjust),
                    RawCurrentSoflanPosition = _rawCurrentSoflanPosition,
                    AdjustedCurrentSoflanPosition = _adjustedCurrentSoflanPosition,
                };
                SoflanPanelBehaviour.HasSelectedData = true;
            }
#endif

            SoflanDiagnostic.VisualSample(
                MonitorId,
                NoteIndex,
                NoteKind,
                noteSoflanGroup,
                currentTime,
                new SoflanPosition(noteSoflanPosition.Value - diffTime),
                noteSoflanPosition,
                diffTime,
                (float)clipedSoflanY,
                scaleStartTime,
                moveStartTime,
                (int)NoteStat,
                isFixedSoflanToUnifiedSpeed,
                "NoteBase.GetNoteYPosition");

            return (float)clipedSoflanY;
        }
    }
}
