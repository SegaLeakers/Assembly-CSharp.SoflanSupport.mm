using System;

namespace SoflanSupport
{
    /// <summary>
    /// Soflan 提前可见与原版注册窗口之间的共享决策规则。
    /// </summary>
    public static class SoflanVisibilityPolicy
    {
        public static bool IsNormallyDue(
            TimeSpan runtimeTime,
            TimeSpan runtimeNoteTime,
            TimeSpan normalVisibleTime)
        {
            return runtimeTime >= runtimeNoteTime - normalVisibleTime;
        }

        public static bool ShouldRegisterNote(
            bool soflanVisible,
            TimeSpan runtimeTime,
            TimeSpan runtimeNoteTime,
            TimeSpan normalVisibleTime)
        {
            return soflanVisible
                || IsNormallyDue(runtimeTime, runtimeNoteTime, normalVisibleTime);
        }
    }
}
