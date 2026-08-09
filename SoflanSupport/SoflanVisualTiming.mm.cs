using Manager;
using MAI2.Util;
using System;

namespace SoflanSupport
{
    /// <summary>
    /// Soflan 视觉时间轴与原版 NoteBase 视觉时序之间的共享规则。
    /// </summary>
    public static class SoflanVisualTiming
    {
        public static bool UsesMaiBugAdjustment(NotesTypeID.Def noteKind)
        {
            switch (noteKind)
            {
                case NotesTypeID.Def.Begin:
                case NotesTypeID.Def.Break:
                case NotesTypeID.Def.ExTap:
                case NotesTypeID.Def.Star:
                case NotesTypeID.Def.BreakStar:
                case NotesTypeID.Def.ExStar:
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

        public static TimeSpan GetMaiBugAdjust(NotesTypeID.Def noteKind, TimeSpan visibleTime)
        {
            return UsesMaiBugAdjustment(noteKind)
                ? MaiBugAdjust.CalculateFromVisibleTime(
                    visibleTime,
                    Setting.EnableSoflanMaiBugAdjust)
                : TimeSpan.Zero;
        }

        public static TimeSpan GetRuntimeChartOffset(int monitorId)
        {
            try
            {
                var runtimeChartOffsetValue = Singleton<GamePlayManager>.Instance
                    .GetGameScore(monitorId)
                    .UserOption
                    .GetAdjustMSec();
                if (!float.IsNaN(runtimeChartOffsetValue)
                    && !float.IsInfinity(runtimeChartOffsetValue))
                    return SoflanRuntimeTime.FromGameMsecBoundary(runtimeChartOffsetValue);

                PatchLog.Error(
                    $"invalid GetAdjustMSec for monitor {monitorId}: {runtimeChartOffsetValue}");
            }
            catch (Exception exception)
            {
                PatchLog.Error(
                    $"GetAdjustMSec failed for monitor {monitorId}: {exception.Message}");
            }

            return TimeSpan.Zero;
        }
    }
}
