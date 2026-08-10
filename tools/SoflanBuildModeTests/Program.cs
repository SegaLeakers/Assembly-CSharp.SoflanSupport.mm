using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private const string ConditionalAttributeName = "System.Diagnostics.ConditionalAttribute";
    private const string DebugSymbol = "DEBUG";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
                throw new ArgumentException(
                    "usage: SoflanBuildModeTests <release-patch.dll> <debug-patch.dll>");

            using (var release = ModuleDefinition.ReadModule(args[0]))
            using (var debug = ModuleDefinition.ReadModule(args[1]))
            {
                VerifyConditionalLogMethods(release);
                VerifyReleaseHasNoDiagnosticCalls(release);
                VerifyReleaseLoggerHasNoWorker(release);
                VerifyReleaseHasNoDebugPanelCalls(release);
                VerifyReleaseSettingSkipsDebugKeys(release);
                VerifyDebugPanelConfiguration(debug);
            }

            Console.WriteLine("SoflanBuildModeTests: PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SoflanBuildModeTests: FAIL");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyConditionalLogMethods(ModuleDefinition module)
    {
        var patchLog = RequiredType(module, "SoflanSupport.PatchLog");
        RequireConditional(RequiredMethod(patchLog, "WriteLine"));
        RequireConditional(RequiredMethod(patchLog, "Diagnostic"));

        var diagnostic = RequiredType(module, "SoflanSupport.SoflanDiagnostic");
        foreach (var method in diagnostic.Methods.Where(method =>
                     method.IsPublic
                     && method.IsStatic
                     && method.ReturnType.MetadataType == MetadataType.Void))
        {
            RequireConditional(method);
        }
    }

    private static void VerifyReleaseHasNoDiagnosticCalls(ModuleDefinition module)
    {
        var forbidden = new List<string>();
        foreach (var type in module.GetTypes())
        {
            if (type.FullName == "SoflanSupport.SoflanDiagnostic"
                || type.FullName == "SoflanSupport.PatchLog")
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    if (!(instruction.Operand is MethodReference called))
                        continue;

                    var declaringType = called.DeclaringType.FullName;
                    var forbiddenCall = declaringType == "SoflanSupport.SoflanDiagnostic"
                        || (declaringType == "SoflanSupport.PatchLog"
                            && (called.Name == "WriteLine" || called.Name == "Diagnostic"));
                    if (forbiddenCall)
                        forbidden.Add(method.FullName + " -> " + called.FullName);
                }
            }
        }

        if (forbidden.Count != 0)
            throw new InvalidOperationException(
                "Release retains diagnostic calls:\n" + string.Join("\n", forbidden));
    }

    private static void VerifyReleaseSettingSkipsDebugKeys(ModuleDefinition module)
    {
        var setting = RequiredType(module, "SoflanSupport.Setting");
        var staticConstructor = setting.Methods.Single(method => method.Name == ".cctor");
        var strings = staticConstructor.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Ldstr)
            .Select(instruction => (string)instruction.Operand)
            .ToArray();

        Require(!strings.Contains("EnablePatchLog"),
            "Release Setting reads EnablePatchLog");
        Require(!strings.Contains("EnableSoflanDiagnosticLog"),
            "Release Setting reads EnableSoflanDiagnosticLog");
        Require(!strings.Contains("EnableSoflanDebugPanel"),
            "Release Setting reads EnableSoflanDebugPanel");
    }

    private static void VerifyReleaseLoggerHasNoWorker(ModuleDefinition module)
    {
        var patchLog = RequiredType(module, "SoflanSupport.PatchLog");
        Require(patchLog.Fields.All(field =>
                field.FieldType.FullName != "System.Threading.Thread"
                && field.FieldType.FullName != "System.Threading.AutoResetEvent"),
            "Release PatchLog retains a background worker field");
        Require(patchLog.Methods.All(method =>
                method.Name != "WorkerLoop"
                && method.Name != "DrainQueue"
                && method.Name != "FlushBatch"),
            "Release PatchLog retains background worker methods");
    }

    private static void VerifyReleaseHasNoDebugPanelCalls(ModuleDefinition module)
    {
        var calls = new List<string>();
        foreach (var type in module.GetTypes())
        {
            if (type.FullName == "SoflanSupport.SoflanPanelBehaviour")
                continue;

            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MethodReference called
                        && called.DeclaringType.FullName == "SoflanSupport.SoflanPanelBehaviour")
                        calls.Add(method.FullName + " -> " + called.FullName);
                }
            }
        }

        Require(calls.Count == 0,
            "Release retains Debug panel calls:\n" + string.Join("\n", calls));

        var references = module.AssemblyReferences.Select(reference => reference.Name).ToArray();
        Require(!references.Contains("UnityEngine.IMGUIModule"),
            "Release references UnityEngine.IMGUIModule");
        Require(!references.Contains("UnityEngine.InputModule"),
            "Release references UnityEngine.InputModule");
        Require(!references.Contains("UnityEngine.Physics2DModule"),
            "Release references UnityEngine.Physics2DModule");
    }

    private static void VerifyDebugPanelConfiguration(ModuleDefinition module)
    {
        var setting = RequiredType(module, "SoflanSupport.Setting");
        var staticConstructor = setting.Methods.Single(method => method.Name == ".cctor");
        Require(staticConstructor.Body.Instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Ldstr
                && (string)instruction.Operand == "EnableSoflanDebugPanel"),
            "Debug Setting does not read EnableSoflanDebugPanel");

        var controller = RequiredType(module, "SoflanSupport.GamePlayFumenController");
        var mount = RequiredMethod(controller, "MountPanelIfNeeded");
        Require(mount.Body.Instructions.Any(instruction =>
                instruction.Operand is MethodReference called
                && called.DeclaringType.FullName == "SoflanSupport.Setting"
                && called.Name == "get_EnableSoflanDebugPanel"),
            "Debug panel mount does not check EnableSoflanDebugPanel");
    }

    private static void RequireConditional(MethodDefinition method)
    {
        var attribute = method.CustomAttributes.SingleOrDefault(candidate =>
            candidate.AttributeType.FullName == ConditionalAttributeName
            && candidate.ConstructorArguments.Count == 1
            && (string)candidate.ConstructorArguments[0].Value == DebugSymbol);
        Require(attribute != null, method.FullName + " is not conditional on DEBUG");
    }

    private static TypeDefinition RequiredType(ModuleDefinition module, string fullName)
    {
        return module.GetType(fullName)
            ?? throw new InvalidOperationException("type not found: " + fullName);
    }

    private static MethodDefinition RequiredMethod(TypeDefinition type, string name)
    {
        return type.Methods.SingleOrDefault(method => method.Name == name)
            ?? throw new InvalidOperationException(
                "method not found: " + type.FullName + "::" + name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
