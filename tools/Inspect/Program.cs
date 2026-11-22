using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
class Program
{
    static System.Collections.Generic.IEnumerable<TypeDefinition> Types(TypeDefinition type)
    { yield return type; foreach (var nested in type.NestedTypes) foreach (var item in Types(nested)) yield return item; }
    static void Main(string[] args)
    {
        bool watermarks = args.Contains("--watermarks");
        bool patches = args.Contains("--patches");
        bool harmony = args.Contains("--harmony");
        bool addressables = args.Contains("--addressables");
        bool release = args.Contains("--release");
        foreach (var path in args)
        {
        if (path.StartsWith("--")) continue;
        using (var a = AssemblyDefinition.ReadAssembly(path))
        {
            Console.WriteLine(path + " | " + a.Name + " | " + a.MainModule.RuntimeVersion);
            if (release)
            {
                if (a.CustomAttributes.Any(x => x.AttributeType.Name == "InternalsVisibleToAttribute")) throw new Exception("Release has test access attributes");
                if (a.MainModule.Types.SelectMany(Types).Any(t => t.Name.Contains("Probe") || t.Methods.Any(m => m.Name == "CaptureDiagnostic" || m.Name == "CaptureOverlayDiagnostic"))) throw new Exception("Release has test-only code");
                Console.WriteLine("PASS production identity, no test access or screenshot code"); continue;
            }
            if (addressables) foreach (var reference in a.MainModule.GetTypeReferences().Where(t => t.Name == "AddressablesUtility")) Console.WriteLine("EXTERNAL " + reference.FullName + " | " + reference.Scope);
            if (addressables) foreach (var reference in a.MainModule.ExportedTypes.Where(t => t.Name == "AddressablesUtility")) Console.WriteLine("FORWARDED " + reference.FullName + " | " + reference.Scope);
            foreach (var t in a.MainModule.Types.SelectMany(Types).Where(t => addressables ? t.FullName.StartsWith("AddressablesUtility") : harmony ? new[] { "Harmony", "PatchProcessor", "PatchClassProcessor", "PatchFunctions", "PatchInfo", "Patch", "AttributePatch" }.Contains(t.Name) : patches ? (t.Name == "Base" || t.FullName.StartsWith("AssetBundleManager") || t.FullName.StartsWith("Base/<InitializeAsync>") || t.FullName.StartsWith("Base/<RegisterPiecesAsync>") || t.FullName == "Base/PieceDef") : watermarks ? (t.FullName.IndexOf("watermark", StringComparison.OrdinalIgnoreCase) >= 0 || t.Name.IndexOf("buildnumber", StringComparison.OrdinalIgnoreCase) >= 0) : new[] { "uGUI_MainMenu", "SceneCleanerPreserve", "Base", "AssetBundleManager", "Version", "GameObject", "ScreenCapture", "Application" }.Contains(t.Name)))
            {
                Console.WriteLine("  " + t.FullName + " : " + t.BaseType);
                foreach (var m in t.Methods.Where(m => new[] { "Awake", "Start", "Deinitialize", ".cctor", "CaptureScreenshot" }.Contains(m.Name))) Console.WriteLine("    " + m);
                foreach (var f in t.Fields.Where(f => new[] { "pieces", "prefab", "loadedBundles", "main" }.Contains(f.Name))) Console.WriteLine("    " + f);
                if (watermarks || patches || harmony || addressables)
                {
                    foreach (var f in t.Fields) Console.WriteLine("    FIELD " + f);
                    foreach (var m in t.Methods)
                    {
                        Console.WriteLine("    METHOD " + m);
                        if (m.HasBody && (addressables || watermarks || (patches && (new[] { "Deinitialize", "Initialize", "InitializeAsync", "RegisterPieces", "RegisterPiecesAsync", "KickoffAssetBundleLoadRequest", "LoadBundleAsync", "UnloadBundle", "Update" }.Contains(m.Name) || t.FullName.Contains("<InitializeAsync>") || t.FullName.Contains("<RegisterPiecesAsync>") || t.Name == "LoadedAssetBundle")) || (harmony && new[] { "Patch", "UpdateWrapper", "PatchAll", "ProcessPatchJob", "ReportException" }.Contains(m.Name)))) foreach (var il in m.Body.Instructions) Console.WriteLine("      " + il);
                    }
                }
            }
            if (patches)
                foreach (var t in a.MainModule.Types.SelectMany(Types))
                    foreach (var m in t.Methods.Where(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand != null && (i.Operand.ToString().Contains("Base::Deinitialize") || i.Operand.ToString().Contains("AssetBundleManager::Deinitialize")))))
                    { Console.WriteLine("CLEANUP CALLER " + m); foreach (var il in m.Body.Instructions) Console.WriteLine("  " + il); }
            if (watermarks)
                foreach (var t in a.MainModule.Types.SelectMany(Types))
                    foreach (var m in t.Methods.Where(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand != null && i.Operand.ToString().IndexOf("watermark", StringComparison.OrdinalIgnoreCase) >= 0)))
                        if (t.FullName.IndexOf("watermark", StringComparison.OrdinalIgnoreCase) < 0)
                        { Console.WriteLine("REFERENCE " + m); foreach (var il in m.Body.Instructions) Console.WriteLine("  " + il); }
        }
        }
    }
}
