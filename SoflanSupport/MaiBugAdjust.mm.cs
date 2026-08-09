using System;

namespace SoflanSupport
{
    /// <summary>
    /// 原版 NoteBase.GetMaiBugAdjustMSec 的纯计算版本。
    /// 返回值是音频毫秒偏移；Soflan 代码必须先把偏移应用到音频时间，
    /// 再把该时间映射到 Soflan Y，不能把它直接当作 Soflan Y 距离。
    /// </summary>
    public static class MaiBugAdjust
    {
        public const double BaseNoteSpeed = 150d;
        public const double DefaultMsecNumerator = 240000d;

        public static TimeSpan Calculate(double noteSpeed)
        {
            if (!IsPositiveFinite(noteSpeed))
                return TimeSpan.Zero;

            var speedRatio = noteSpeed / BaseNoteSpeed;
            var adjustMsec = (speedRatio - 1d) * (-0.5d / speedRatio) * 1.6d * 1000d / 60d;
            return SoflanRuntimeTime.FromMilliseconds(adjustMsec);
        }

        public static TimeSpan Calculate(double noteSpeed, bool enabled)
        {
            return enabled ? Calculate(noteSpeed) : TimeSpan.Zero;
        }

        public static TimeSpan CalculateFromDefaultTime(TimeSpan defaultTime)
        {
            if (defaultTime <= TimeSpan.Zero)
                return TimeSpan.Zero;

            return Calculate(DefaultMsecNumerator / defaultTime.TotalMilliseconds);
        }

        public static TimeSpan CalculateFromDefaultTime(TimeSpan defaultTime, bool enabled)
        {
            return enabled ? CalculateFromDefaultTime(defaultTime) : TimeSpan.Zero;
        }

        public static TimeSpan CalculateFromVisibleTime(TimeSpan visibleTime)
        {
            if (visibleTime <= TimeSpan.Zero)
                return TimeSpan.Zero;

            return CalculateFromDefaultTime(TimeSpan.FromTicks(visibleTime.Ticks / 2));
        }

        public static TimeSpan CalculateFromVisibleTime(TimeSpan visibleTime, bool enabled)
        {
            return enabled ? CalculateFromVisibleTime(visibleTime) : TimeSpan.Zero;
        }

        public static TimeSpan ApplyToAudioTime(TimeSpan audioTime, TimeSpan adjustment)
        {
            return SoflanRuntimeTime.ToRawChartAudioTime(
                audioTime,
                TimeSpan.Zero,
                adjustment);
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
