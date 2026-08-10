#pragma warning disable CS0626
using MAI2.Util;
using Manager;
using MonoMod;
using SoflanSupport;
using System.Collections.Generic;

namespace Monitor
{
    public class patch_SlideRoot : SlideRoot
    {
#if DEBUG
        [MonoModIgnore]
        private List<SlideManager.HitArea> _hitAreaList;

        [MonoModIgnore]
        private int _hitIndex;

        [MonoModIgnore]
        private bool _hitIn;

        [MonoModIgnore]
        private int _hitSubIndex;

        public void __SoflanLogInitialize(NoteData note)
        {
            var manager = Singleton<SoflanManager>.Instance;
            var group = manager.getNoteSoflanGroup(MonitorId, note);
            var noteSoflanPosition = manager.containsSoflans(MonitorId)
                ? manager.GetNoteSoflanPosition(
                    MonitorId,
                    note.indexNote,
                    SoflanRuntimeTime.FromGameMsecBoundary(note.time.msec),
                    group)
                : new SoflanPosition(AppearMsec);
            SoflanDiagnostic.ObjectInitialized(
                MonitorId,
                note,
                SoflanRuntimeTime.FromGameMsecBoundary(AppearMsec),
                SoflanRuntimeTime.FromGameMsecBoundary(TailMsec),
                SoflanRuntimeTime.FromGameMsecBoundary(DefaultMsec),
                group,
                false,
                FixedSoflan.DefaultUnifiedSpeed,
                noteSoflanPosition,
                System.TimeSpan.Zero,
                "SlideRoot.Initialize");
        }

        public void __SoflanLogProgress()
        {
            SoflanDiagnostic.SlideProgress(
                MonitorId,
                NoteIndex,
                NotesType.getEnum(),
                _hitIndex,
                _hitAreaList?.Count ?? 0,
                _hitIn,
                _hitSubIndex,
                SoflanRuntimeTime.FromGameMsecBoundary(TailMsec),
                SoflanRuntimeTime.FromGameMsecBoundary(lastWaitTime),
                JudgeResult,
                EndFlag,
                SoflanRuntimeTime.FromGameMsecBoundary(JudgeTimingDiffMsec),
                string.Empty,
                "SlideRoot.NoteCheck");
        }
#endif
    }
}
