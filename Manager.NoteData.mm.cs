#pragma warning disable CS0626
// patch_Manager.NoteData — 对应 head commit 2a7a4a4 中 Manager/NoteData.cs 的改动.
// - 新增字段 isFixedSoflanToUnifiedSpeed / fixedSoflanUnifiedSpeed
using SoflanSupport;

namespace Manager
{
    public class patch_NoteData : NoteData
    {
        public bool isFixedSoflanToUnifiedSpeed;
        public float fixedSoflanUnifiedSpeed;

        public extern void orig_clear();

        public void clear()
        {
            orig_clear();
            isFixedSoflanToUnifiedSpeed = false;
            fixedSoflanUnifiedSpeed = FixedSoflan.DefaultUnifiedSpeed;
        }
    }
}
