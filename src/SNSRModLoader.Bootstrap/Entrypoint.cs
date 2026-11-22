using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;

namespace SNSRModLoader.Bootstrap
{
    public static class Entrypoint
    {
        private static string loader;
        private static string managed;
        private static bool started;
        public static void Main()
        {
            if (started) return;
            started = true;
            loader = Path.GetDirectoryName(typeof(Entrypoint).Assembly.Location);
            try
            {
                managed = Path.Combine(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(loader)), "Subnautica_Data"), "Managed");
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                Initialize();
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(loader, "bootstrap.log"), DateTime.UtcNow.ToString("o") + " " + ex + Environment.NewLine); } catch { }
#if SNSR_DIAGNOSTICS
                if (Environment.GetEnvironmentVariable("SNSR_SMOKE_TEST") != "1")
#endif
                    MessageBoxW(IntPtr.Zero, "SNSRModLoader could not start: " + ex.Message + "\n\nFull details: Mods/Loader/bootstrap.log", "SNSRModLoader startup error", 0x30);
            }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr window, string message, string caption, uint flags);
        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (name != Path.GetFileName(name)) return null;
            foreach (var directory in new[] { loader, managed })
            {
                if (directory == null) continue;
                var path = Path.Combine(directory, name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Initialize()
        {
            var configPath = Path.Combine(Path.Combine(loader, "Config"), "loader.ini");
            if (!new SettingsFile(configPath).Get("enabled", true)) return;
            var gameDll = Path.Combine(managed, "Assembly-CSharp.dll");
            var game = Registry.Detect(gameDll);
            if (game == GameVersion.Unknown) throw new NotSupportedException("Unknown Assembly-CSharp SHA-256 " + Registry.Hash(gameDll) + ". Plugins were not loaded. Register this exact game build in a loader release.");
            var unityPath = Path.Combine(managed, game == GameVersion.September2018 ? "UnityEngine.dll" : "UnityEngine.CoreModule.dll");
            using (var unity = AssemblyDefinition.ReadAssembly(unityPath))
            {
                var module = unity.MainModule;
                var type = module.GetType("UnityEngine.GameObject");
                var cctor = type.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic);
                if (cctor == null)
                {
                    cctor = new MethodDefinition(".cctor", Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
                    type.Methods.Add(cctor); cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                }
                var reference = new AssemblyNameReference("SNSRModLoader.Runtime", typeof(Plugin).Assembly.GetName().Version);
                module.AssemblyReferences.Add(reference);
                var entryType = new TypeReference("SNSRModLoader.Runtime", "LoaderRuntime", module, reference);
                var method = new MethodReference("Start", module.TypeSystem.Void, entryType) { HasThis = false };
                cctor.Body.GetILProcessor().InsertBefore(cctor.Body.Instructions[0], Instruction.Create(OpCodes.Call, method));
                using (var bytes = new MemoryStream()) { unity.Write(bytes); Assembly.Load(bytes.ToArray()); }
            }
            File.AppendAllText(Path.Combine(loader, "bootstrap.log"), DateTime.UtcNow.ToString("o") + " Armed " + Registry.DisplayName(game) + " runtime (in-memory Unity hook)." + Environment.NewLine);
        }
    }
}
