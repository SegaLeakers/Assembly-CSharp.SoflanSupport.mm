using Manager;
using System;
using UnityEngine;

namespace SoflanSupport
{
    public static class FixedSoflan
    {
        public const float DefaultUnifiedSpeed = 600f;

        public static bool IsSupportedTapKind(NotesTypeID.Def noteKind)
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
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsEnabledForNote(NoteData note)
        {
            if (note == null)
                return false;

            var fixedNote = (patch_NoteData)note;
            return fixedNote.isFixedSoflanToUnifiedSpeed
                && fixedNote.fixedSoflanUnifiedSpeed > 0f
                && IsSupportedTapKind(note.type.getEnum());
        }

        public static float GetUnifiedSpeed(NoteData note)
        {
            var speed = ((patch_NoteData)note).fixedSoflanUnifiedSpeed;
            return speed > 0f ? speed : DefaultUnifiedSpeed;
        }

        public static TimeSpan GetDefaultTime(float unifiedSpeed)
        {
            if (unifiedSpeed <= 0f || float.IsNaN(unifiedSpeed) || float.IsInfinity(unifiedSpeed))
                return TimeSpan.Zero;

            var ticks = MaiBugAdjust.DefaultMsecNumerator
                / unifiedSpeed
                * TimeSpan.TicksPerMillisecond;
            return TimeSpan.FromTicks((long)System.Math.Round(
                ticks,
                MidpointRounding.AwayFromZero));
        }

        public static TimeSpan GetMaiBugAdjust(float unifiedSpeed)
        {
            return MaiBugAdjust.Calculate(unifiedSpeed, Setting.EnableSoflanMaiBugAdjust);
        }

        public static double GetMoveStartDistance(float unifiedSpeed)
        {
            // MaiBug 已通过 currentAudioMsec + adjustMsec 映射进 Soflan 时间轴，
            // 这里的门槛保持为纯 Soflan Y 距离，避免重复应用补偿。
            return GetDefaultTime(unifiedSpeed).TotalMilliseconds;
        }

        public static double GetScaleStartDistance(float unifiedSpeed)
        {
            return GetMoveStartDistance(unifiedSpeed) * 2d;
        }

        public static TimeSpan GetVisibleTime(float unifiedSpeed)
        {
            var defaultTime = GetDefaultTime(unifiedSpeed);
            return TimeSpan.FromTicks(checked(defaultTime.Ticks * 2));
        }

        public static float GetMotionProgress(double diffPosition, float unifiedSpeed)
        {
            var moveStartDistance = GetMoveStartDistance(unifiedSpeed);
            return Mathf.Clamp01((float)((moveStartDistance - diffPosition) / (2d * moveStartDistance)));
        }

        public static float GetScaleProgress(double absDiffPosition, float unifiedSpeed)
        {
            return Mathf.Clamp01((float)((GetScaleStartDistance(unifiedSpeed) - absDiffPosition)
                / GetMoveStartDistance(unifiedSpeed)));
        }

        public static float GetYFromMotionProgress(float startPos, float endPos, float motionProgress)
        {
            float outsideY = endPos + (endPos - startPos);
            return Mathf.Lerp(startPos, outsideY, motionProgress);
        }
    }
}
