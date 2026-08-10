// SoflanSupport.Setting — 新增类型, 基于 head commit 2a7a4a4 并扩展 Soflan 配置项.
// MonoMod 将该类型整体复制进目标程序集 Assembly-CSharp.
using MAI2System;
namespace SoflanSupport
{
    public static class Setting
    {
        public static bool EnablePatchLog { set; get; }
        public static bool EnableSoflanDiagnosticLog { private set; get; }
        public static bool EnableSoflanDebugPanel { private set; get; }
        public static bool EnableSoflanMaiBugAdjust { private set; get; } = true;

        private static bool init = false;

        static Setting()
        {
            if (init)
                return;
            init = true;

            using (var iniFile = new IniFile("mai2.ini"))
            {
#if DEBUG
                EnablePatchLog = iniFile.getValue("Patches", "EnablePatchLog", true);
                EnableSoflanDiagnosticLog = iniFile.getValue(
                    "Patches",
                    "EnableSoflanDiagnosticLog",
                    true);
                EnableSoflanDebugPanel = iniFile.getValue(
                    "Patches",
                    "EnableSoflanDebugPanel",
                    true);
#endif
                EnableSoflanMaiBugAdjust = iniFile.getValue(
                    "Patches",
                    "EnableSoflanMaiBugAdjust",
                    true);
            }

            PatchLog.WriteLine($"---------DpPatches.Setting------------");
            PatchLog.WriteLine($"EnablePatchLog = {EnablePatchLog}");
            PatchLog.WriteLine($"EnableSoflanDiagnosticLog = {EnableSoflanDiagnosticLog}");
            PatchLog.WriteLine($"EnableSoflanDebugPanel = {EnableSoflanDebugPanel}");
            PatchLog.WriteLine($"EnableSoflanMaiBugAdjust = {EnableSoflanMaiBugAdjust}");
            PatchLog.WriteLine($"----------");
            PatchLog.WriteLine($"--------------------------------------");
        }
    }
}
