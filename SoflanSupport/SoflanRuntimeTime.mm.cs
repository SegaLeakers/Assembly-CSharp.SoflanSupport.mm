using System;

namespace SoflanSupport
{
    /// <summary>
    /// 原版运行时毫秒轴与 MA2 原始毫秒轴之间的统一换算。
    /// Soflan 的 BPM/SFL/TGrid 都位于 MA2 原始时间轴，因此运行时当前时间进入
    /// Soflan 积分前必须先移除 GetAdjustMSec()，再应用可选的视觉音频偏移。
    /// </summary>
    public static class SoflanRuntimeTime
    {
        internal static TimeSpan FromGameMsecBoundary(float gameMsec)
        {
            if (float.IsNaN(gameMsec) || float.IsInfinity(gameMsec))
                return TimeSpan.Zero;

            return FromMilliseconds(gameMsec);
        }

        internal static TimeSpan FromMilliseconds(double milliseconds)
        {
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds))
                return TimeSpan.Zero;

            var ticks = milliseconds * TimeSpan.TicksPerMillisecond;
            if (ticks >= TimeSpan.MaxValue.Ticks)
                return TimeSpan.MaxValue;
            if (ticks <= TimeSpan.MinValue.Ticks)
                return TimeSpan.MinValue;

            return TimeSpan.FromTicks((long)Math.Round(
                ticks,
                MidpointRounding.AwayFromZero));
        }

        public static TimeSpan ToRawChartAudioTime(
            TimeSpan runtimeAudioTime,
            TimeSpan runtimeChartOffset,
            TimeSpan visualAudioOffset)
        {
            long rawTicks;
            try
            {
                rawTicks = checked(
                    runtimeAudioTime.Ticks
                    - runtimeChartOffset.Ticks
                    + visualAudioOffset.Ticks);
            }
            catch (OverflowException)
            {
                var exactTicks = (decimal)runtimeAudioTime.Ticks
                    - runtimeChartOffset.Ticks
                    + visualAudioOffset.Ticks;
                if (exactTicks <= 0)
                    return TimeSpan.Zero;
                return exactTicks >= TimeSpan.MaxValue.Ticks
                    ? TimeSpan.MaxValue
                    : TimeSpan.FromTicks((long)exactTicks);
            }

            return rawTicks <= 0
                ? TimeSpan.Zero
                : TimeSpan.FromTicks(rawTicks);
        }
    }
}
