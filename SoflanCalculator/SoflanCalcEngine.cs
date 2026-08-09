// SoflanCalcEngine — soflan 计算引擎.
// 复用 SimpleSoflanFramework.Core.dll 中的 TGridCalculator / BpmList / SoflanListMap,
// 与游戏运行时 (SoflanManager + patch_NoteBase.GetNoteYPosition_soflan) 完全一致的计算逻辑.
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.OngekiObjects;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;
using OngekiFumenEditor.Core.Utils;
using SoflanSupport;
using System;

namespace SoflanCalculator
{
    /// <summary>
    /// 与 NoteBase.NoteStatus 一致的枚举.
    /// </summary>
    public enum NoteStat
    {
        Init,
        Scale,
        Move,
        Check,
        End
    }

    /// <summary>
    /// 一次 soflan 计算的全部输出数据.
    /// </summary>
    public class CalcResult
    {
        // --- Parameters ---
        public float NoteSpeedValue;
        public double SpeedRatio;
        public TimeSpan DefaultTime;
        public bool MaiBugAdjustEnabled;
        public TimeSpan MaiBugAdjust;
        public double StartPos;
        public double EndPos;

        // --- Note Timing ---
        public TimeSpan AppearTime;
        public SoflanPosition NoteSoflanPosition;
        public TimeSpan CurrentTime;
        public TimeSpan RuntimeChartOffset;
        public TimeSpan RawChartCurrentTime;
        public TimeSpan MaiBugAdjustedCurrentTime;
        public SoflanPosition RawCurrentSoflanPosition;
        public SoflanPosition CurrentSoflanPosition;
        public double CurrentSoflanSpeed;

        // --- Computed Values ---
        public double DiffPosition;
        public double AbsDiffPosition;
        public double ScaleStartDistance;
        public double MoveStartDistance;
        public NoteStat NoteStat;
        public double MoveProgress;
        public double FinalScale;
        public double ObjectScaleProgress;
        public double GuideAlpha;
        public double InsideY;
        public double OutsideY;
        public double SoflanY;
        public double ClipedSoflanY;

        // --- Note Info ---
        public int LineNumber;
        public string NoteType;
        public int Bar;
        public int Grid;
        public int Pos;
        public int SoflanGroup;
        public bool ContainsSoflans;
    }

    public static class SoflanCalcEngine
    {
        /// <summary>
        /// 执行完整的 soflan 计算.
        /// </summary>
        /// <param name="data">解析后的 ma2 数据</param>
        /// <param name="note">目标 note 记录</param>
        /// <param name="currentTime">当前播放时间</param>
        /// <param name="noteSpeedValue">物件速度值 (对应 OptionNotespeedID.GetValue)</param>
        /// <param name="startPos">NoteStart Y 坐标 (从 Unity prefab 读取)</param>
        /// <param name="endPos">NoteEnd Y 坐标 (从 Unity prefab 读取)</param>
        /// <param name="enableMaiBugAdjust">是否让 MaiBug 音频偏移参与 Soflan 计算</param>
        /// <param name="runtimeChartOffset">原版 GetAdjustMSec() 加入运行时 note 时间的基础偏移</param>
        public static CalcResult Calculate(
            Ma2Data data,
            NoteRecord note,
            TimeSpan currentTime,
            float noteSpeedValue,
            float startPos,
            float endPos,
            bool enableMaiBugAdjust = true,
            TimeSpan runtimeChartOffset = default)
        {
            // --- 构建 BpmList ---
            // 与 SoflanManager.loadComposition 一致:
            //   首个 BPM (grid==0) → FirstBpm; 其余 → Add(BPMChange)
            var bpmList = new BpmList();
            bpmList.FirstBpm = (double)data.FirstBpm;
            foreach (var bpm in data.BpmChanges)
            {
                // BPM 行的 bar/grid → TGrid (Unit=bar, Grid=within-bar grid)
                // 与 TGridHelper.ToTGrid 等价: absolute_grid = bar*res + grid; Unit = abs/res; Grid = abs%res
                var tGrid = new TGrid(bpm.Bar, bpm.Grid);
                bpmList.Add(new BPMChange
                {
                    BPM = (double)bpm.Bpm,
                    TGrid = tGrid
                });
            }

            // --- 构建 SoflanListMap ---
            // 与 SoflanManager.loadComposition 一致
            var soflanMap = new SoflanListMap();
            bool containSoflans = data.Soflans.Count > 0;
            foreach (var sfl in data.Soflans)
            {
                var soflan = new Soflan
                {
                    TGrid = new TGrid(sfl.Unit, sfl.Grid),
                    Speed = sfl.Speed,
                    SoflanGroup = sfl.SoflanGroup
                };
                soflan.EndTGrid = soflan.TGrid + new GridOffset(0, sfl.Length);
                soflanMap.Add(soflan);
            }

            // --- 速度参数推导 ---
            // 与 NoteBase.Initialize + SoflanVisualTiming.GetMaiBugAdjust 一致
            double speedRatio = noteSpeedValue / MaiBugAdjust.BaseNoteSpeed;
            var defaultTime = SoflanRuntimeTime.FromMilliseconds(
                MaiBugAdjust.DefaultMsecNumerator / noteSpeedValue);
            var maiBugAdjust = MaiBugAdjust.Calculate(noteSpeedValue, enableMaiBugAdjust);

            // --- AppearMsec 计算 ---
            // bar/grid → TGrid → TGridCalculator.ConvertTGridToAudioTime → msec
            var noteTGrid = new TGrid(note.Bar, note.Grid);
            var appearTime = TGridCalculator.ConvertTGridToAudioTime(noteTGrid, bpmList);

            // --- SoflanPosition 计算 ---
            // ConvertAudioTimeToY_PreviewMode 对 AppearMsec 和 currentTime 分别求值.
            // 与游戏一致: 始终通过 SoflanListMap 索引器获取 SoflanList (缺失组自动创建空列表,
            // 空 SoflanList → speed=1.0 → soflanTime == msec, 与游戏行为完全一致).
            int soflanGroup = note.SoflanGroup;
            var soflanList = soflanMap[soflanGroup];
            var noteSoflanPosition = new SoflanPosition(
                TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                    appearTime,
                    soflanList,
                    bpmList,
                    1));
            var rawChartCurrentTime = SoflanRuntimeTime.ToRawChartAudioTime(
                currentTime,
                runtimeChartOffset,
                TimeSpan.Zero);
            var rawCurrentSoflanPosition = new SoflanPosition(
                TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                    rawChartCurrentTime,
                    soflanList,
                    bpmList,
                    1));
            var maiBugAdjustedCurrentTime = SoflanRuntimeTime.ToRawChartAudioTime(
                currentTime,
                runtimeChartOffset,
                maiBugAdjust);
            var currentSoflanPosition = new SoflanPosition(
                TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                    maiBugAdjustedCurrentTime,
                    soflanList,
                    bpmList,
                    1));

            // --- 当前变速速度 ---
            // 与 SoflanManager.GetCurrentSpeed 一致: currentTime → TGrid → SoflanList.CalculateSpeed.
            // 无 SFL 或无该组时, SoflanList 为空 → CalculateSpeed 返回 1.0.
            var currentTGrid = TGridCalculator.ConvertAudioTimeToTGrid(
                rawChartCurrentTime,
                bpmList);
            double currentSoflanSpeed = soflanList.CalculateSpeed(bpmList, currentTGrid);

            // --- GetNoteYPosition_soflan 逻辑 ---
            // 与 patch_NoteBase.GetNoteYPosition_soflan 完全一致
            double diffPosition = noteSoflanPosition.DeltaTo(currentSoflanPosition);
            double absDiffPosition = Math.Abs(diffPosition);

            double moveStartDistance = defaultTime.TotalMilliseconds;
            double scaleStartDistance = 2d * moveStartDistance;

            // MaiBug 音频偏移已经随 currentMsec 一起映射进 Soflan Y，
            // 因而无需再叠加独立的坐标偏移。
            double guideScaleAdj = 0d;

            double insideY = startPos;
            double outsideY = endPos + (endPos - startPos);

            double soflanY = SoflanVisualMath.MapValue(
                diffPosition,
                -moveStartDistance,
                moveStartDistance,
                outsideY,
                insideY);
            double adjustedSoflanY = soflanY;

            double clipedSoflanY = Math.Max(120d, Math.Min(680d, adjustedSoflanY));

            double moveProgress = (clipedSoflanY - startPos) / (endPos - startPos);
            moveProgress = Math.Max(0d, moveProgress);

            double guideScale = 0.75d * moveProgress;
            double adjustedGuideScale = guideScale + guideScaleAdj;
            double finalScale = 0.25d + adjustedGuideScale;

            NoteStat noteStat = NoteStat.Init;
            double guideAlpha;

            if (absDiffPosition > scaleStartDistance)
            {
                // 不修改 NoteStat (保持 Init, Guide 隐藏)
                guideAlpha = 0f;
            }
            else if (absDiffPosition > moveStartDistance)
            {
                noteStat = NoteStat.Scale;
                guideAlpha = SoflanVisualMath.MapValue(
                    absDiffPosition,
                    scaleStartDistance,
                    moveStartDistance,
                    0d,
                    1d);
            }
            else
            {
                noteStat = NoteStat.Move;
                guideAlpha = 1f;
            }

            double objectScaleProgress = Math.Max(
                0d,
                Math.Min(1d, (scaleStartDistance - absDiffPosition) / moveStartDistance));

            return new CalcResult
            {
                NoteSpeedValue = noteSpeedValue,
                SpeedRatio = speedRatio,
                DefaultTime = defaultTime,
                MaiBugAdjustEnabled = enableMaiBugAdjust,
                MaiBugAdjust = maiBugAdjust,
                StartPos = startPos,
                EndPos = endPos,
                AppearTime = appearTime,
                NoteSoflanPosition = noteSoflanPosition,
                CurrentTime = currentTime,
                RuntimeChartOffset = runtimeChartOffset,
                RawChartCurrentTime = rawChartCurrentTime,
                MaiBugAdjustedCurrentTime = maiBugAdjustedCurrentTime,
                RawCurrentSoflanPosition = rawCurrentSoflanPosition,
                CurrentSoflanPosition = currentSoflanPosition,
                CurrentSoflanSpeed = currentSoflanSpeed,
                DiffPosition = diffPosition,
                AbsDiffPosition = absDiffPosition,
                ScaleStartDistance = scaleStartDistance,
                MoveStartDistance = moveStartDistance,
                NoteStat = noteStat,
                MoveProgress = moveProgress,
                FinalScale = finalScale,
                ObjectScaleProgress = objectScaleProgress,
                GuideAlpha = guideAlpha,
                InsideY = insideY,
                OutsideY = outsideY,
                SoflanY = soflanY,
                ClipedSoflanY = clipedSoflanY,
                LineNumber = note.LineNumber,
                NoteType = note.Type,
                Bar = note.Bar,
                Grid = note.Grid,
                Pos = note.Pos,
                SoflanGroup = soflanGroup,
                ContainsSoflans = containSoflans,
            };
        }
    }
}
