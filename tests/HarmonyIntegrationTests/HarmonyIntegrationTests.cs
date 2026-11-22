using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Reflection;
using HarmonyLib;
using SNSRModLoader;

[SNSRModLoader("snsr.tests.harmony", "Harmony Integration Tests", "1.0.0")]
[TargetVersions(GameVersion.September2018, GameVersion.December2021, GameVersion.March2023, GameVersion.October2025)]
public sealed class HarmonyIntegrationTests : Plugin
{
    public override void Load(PluginContext context)
    {
        context.Harmony.PatchAll(typeof(HarmonyIntegrationTests).Assembly);
        if (Compute(2) != 16 || ComputePatch.Finalizers != 1 || ThrowAndRecover() != 9)
            throw new InvalidOperationException("Harmony smoke test plugin produced an unexpected result.");
        var manual = AccessTools.Method(typeof(HarmonyIntegrationTests), "ManualTarget");
        context.Harmony.Patch(manual, prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyIntegrationTests), "ManualPrefix")));
        if (ManualTarget(3) != 25) throw new InvalidOperationException("Manual Harmony patch failed.");
        bool failed = false;
        try { context.Harmony.Patch(null, prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyIntegrationTests), "ManualPrefix"))); }
        catch (Exception) { failed = true; }
        if (!failed) throw new InvalidOperationException("Invalid manual target did not preserve Harmony's exception.");
        MissingTargetTestPlugin.Enabled = true; failed = false;
        try { context.Harmony.CreateClassProcessor(typeof(MissingTargetTestPlugin)).Patch(); }
        catch (Exception) { failed = true; }
        finally { MissingTargetTestPlugin.Enabled = false; }
        if (!failed) throw new InvalidOperationException("Missing class target did not preserve Harmony's exception.");
        context.Harmony.CreateReversePatcher(manual, new HarmonyMethod(AccessTools.Method(typeof(HarmonyIntegrationTests), "ReverseStub"))).Patch(HarmonyReversePatchType.Original);
        if (ReverseStub(3) != 15) throw new InvalidOperationException("Reverse patch failed.");
        context.Logger.Info("Harmony audit test plugin PASS: manual/reverse installs and expected invalid-target failures. FAILED entries for ExpectedMissingMethod are intentional test cases.");
        context.Logger.Info("Harmony prefix/postfix/transpiler/finalizer PASS");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute(int value) { return value * 2; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ThrowAndRecover() { throw new InvalidOperationException("expected test plugin exception"); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ManualTarget(int value) { return value * 5; }
    private static void ManualPrefix(ref int value) { value += 2; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReverseStub(int value) { throw new NotSupportedException("Reverse patch stub"); }
}
[HarmonyPatch(typeof(HarmonyIntegrationTests), "ExpectedMissingMethod")]
internal static class MissingTargetTestPlugin
{
    internal static bool Enabled;
    private static bool Prepare() { return Enabled; }
    private static void Prefix() { }
}
[HarmonyPatch(typeof(HarmonyIntegrationTests), "Compute")]
internal static class ComputePatch
{
    internal static int Finalizers;
    private static void Prefix(ref int value) { value += 3; }
    private static void Postfix(ref int __result) { __result += 1; }
    private static Exception Finalizer(Exception __exception) { Finalizers++; return __exception; }
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        { if (instruction.opcode == OpCodes.Ldc_I4_2) instruction.opcode = OpCodes.Ldc_I4_3; yield return instruction; }
    }
}
[HarmonyPatch(typeof(HarmonyIntegrationTests), "ThrowAndRecover")]
internal static class RecoveryPatch
{
    private static Exception Finalizer(ref int __result) { __result = 9; return null; }
}
