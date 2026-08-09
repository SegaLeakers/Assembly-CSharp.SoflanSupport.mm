using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod;

namespace SoflanPatchHarness
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length < 3)
                {
                    throw new ArgumentException(
                        "usage: SoflanPatchHarness <Assembly-CSharp.dll> <patch.mm.dll> <output.dll> [dependency-dir ...]");
                }

                var inputPath = Path.GetFullPath(args[0]);
                var patchPath = Path.GetFullPath(args[1]);
                var outputPath = Path.GetFullPath(args[2]);

                using (var modder = new MonoModder
                {
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    MissingDependencyThrow = true
                })
                {
                    AddDependencyDirectory(modder, Path.GetDirectoryName(inputPath));
                    AddDependencyDirectory(modder, Path.GetDirectoryName(patchPath));
                    for (var i = 3; i < args.Length; i++)
                        AddDependencyDirectory(modder, Path.GetFullPath(args[i]));

                    modder.Read();
                    modder.ReadMod(patchPath);
                    modder.MapDependencies();
                    modder.AutoPatch();
                    ValidateNotesManagerClock(modder.Module);
                    ValidateGameClockReference(modder.Module);

                    using (var output = File.Create(outputPath))
                        modder.Write(output, outputPath);
                }

                Console.WriteLine("SoflanPatchHarness: PASS output=" + outputPath);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("SoflanPatchHarness: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void AddDependencyDirectory(MonoModder modder, string path)
        {
            if (!string.IsNullOrEmpty(path) && !modder.DependencyDirs.Contains(path))
                modder.DependencyDirs.Add(path);
        }

        private static void ValidateNotesManagerClock(ModuleDefinition module)
        {
            var notesManager = module.GetType("Manager.NotesManager")
                ?? throw new InvalidOperationException(
                    "clock validation: Manager.NotesManager was not found");
            var snapshotField = notesManager.Fields.FirstOrDefault(field =>
                field.Name == "_soflanCurrentTimeSnapshot"
                && field.FieldType.FullName == "System.TimeSpan")
                ?? throw new InvalidOperationException(
                    "clock validation: TimeSpan snapshot field was not injected");
            var snapshotGetter = notesManager.Methods.FirstOrDefault(method =>
                method.Name == "GetCurrentTimeSnapshot"
                && method.IsStatic
                && method.Parameters.Count == 0
                && method.ReturnType.FullName == "System.TimeSpan")
                ?? throw new InvalidOperationException(
                    "clock validation: GetCurrentTimeSnapshot was not injected");

            if (snapshotGetter.Body == null
                || !snapshotGetter.Body.Instructions.Any(instruction =>
                    instruction.OpCode == OpCodes.Ldsfld
                    && instruction.Operand is FieldReference field
                    && field.Name == snapshotField.Name))
            {
                throw new InvalidOperationException(
                    "clock validation: GetCurrentTimeSnapshot does not read the snapshot field");
            }

            var updateTimer = notesManager.Methods.FirstOrDefault(method =>
                method.IsStatic
                && method.Parameters.Count == 0
                && method.Body != null
                && (method.Name == "UpdateTimer"
                    || method.Name == "orig_UpdateTimer"
                    || method.Name == "patched_UpdateTimer"
                    || method.Name.StartsWith("patched_UpdateTimer_", StringComparison.Ordinal))
                && method.Body.Instructions.Any(IsStopwatchElapsedCall)
                && method.Body.Instructions.Any(instruction =>
                    instruction.OpCode == OpCodes.Stsfld
                    && instruction.Operand is FieldReference field
                    && field.Name == snapshotField.Name));
            if (updateTimer == null)
            {
                throw new InvalidOperationException(
                    "clock validation: UpdateTimer does not capture Stopwatch.Elapsed into the snapshot");
            }

            Console.WriteLine(
                "SoflanPatchHarness: clock snapshot validated in Manager.NotesManager::"
                + updateTimer.Name);
        }

        private static bool IsStopwatchElapsedCall(Instruction instruction)
        {
            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                return false;
            var method = instruction.Operand as MethodReference;
            return method != null
                && method.Name == "get_Elapsed"
                && method.DeclaringType.FullName == "System.Diagnostics.Stopwatch";
        }

        private static void ValidateGameClockReference(ModuleDefinition module)
        {
            var gameClock = module.GetType("SoflanSupport.SoflanGameClock")
                ?? throw new InvalidOperationException(
                    "clock validation: SoflanGameClock was not injected");
            var getter = gameClock.Methods.FirstOrDefault(method =>
                method.Name == "get_CurrentTime"
                && method.IsStatic
                && method.Parameters.Count == 0
                && method.ReturnType.FullName == "System.TimeSpan")
                ?? throw new InvalidOperationException(
                    "clock validation: SoflanGameClock.CurrentTime getter was not injected");

            if (getter.Body == null
                || !getter.Body.Instructions.Any(instruction =>
                    (instruction.OpCode == OpCodes.Call
                        || instruction.OpCode == OpCodes.Callvirt)
                    && instruction.Operand is MethodReference method
                    && method.Name == "GetCurrentTimeSnapshot"
                    && method.DeclaringType.FullName == "Manager.NotesManager"))
            {
                throw new InvalidOperationException(
                    "clock validation: SoflanGameClock did not relink to Manager.NotesManager");
            }

            Console.WriteLine(
                "SoflanPatchHarness: SoflanGameClock relinked to "
                + "Manager.NotesManager::GetCurrentTimeSnapshot");
        }
    }
}
