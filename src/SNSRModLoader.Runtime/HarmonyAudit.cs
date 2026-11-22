using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SNSRModLoader.Runtime
{
    // Observe installation, never individual patch invocations. This covers
    // PatchAll, direct Patch/CreateProcessor, IL manipulators and reverse patches.
    internal static class HarmonyAudit
    {
        private const string AuditId = "SNSRModLoader.harmony-audit";
        private static readonly object gate = new object();
        private static readonly Dictionary<MethodBase, List<Entry>> installed = new Dictionary<MethodBase, List<Entry>>();
        private static readonly Dictionary<string, string> owners = new Dictionary<string, string>();
        private static readonly Dictionary<Assembly, string> assemblies = new Dictionary<Assembly, string>();
        private static Harmony audit;
        internal sealed class Entry
        {
            internal string Kind, Owner;
            internal MethodInfo Method;
            internal string Key
            {
                get
                {
                    try { return Kind + "|" + Owner + "|" + Method.Module.ModuleVersionId + "|" + Method.MetadataToken; }
                    catch (InvalidOperationException) { return Kind + "|" + Owner + "|dynamic|" + Method.GetHashCode(); }
                }
            }
        }
        internal sealed class Change
        {
            internal MethodBase Original;
            internal List<Entry> Current, Added, Removed;
        }
        internal sealed class ClassAttempt
        {
            internal PluginLogger Logger;
            internal Type Type;
        }
        internal static void Install()
        {
            if (audit != null) return;
            audit = new Harmony(AuditId);
            var functions = typeof(Harmony).Assembly.GetType("HarmonyLib.PatchFunctions", true);
            audit.Patch(AccessTools.Method(functions, "UpdateWrapper"), prefix: Hook("BeforeWrapper"), finalizer: Hook("AfterWrapper"));
            audit.Patch(AccessTools.Method(typeof(PatchClassProcessor), "Patch"), prefix: Hook("BeforeClass"), finalizer: Hook("AfterClass"));
            audit.Patch(AccessTools.Method(typeof(PatchProcessor), "Patch"), prefix: Hook("BeforeManual"), finalizer: Hook("AfterManual"));
            audit.Patch(AccessTools.Method(functions, "ReversePatch"), prefix: Hook("BeforeReverse"), finalizer: Hook("AfterReverse"));
            new PluginLogger("Harmony").Info("Patch installation audit enabled.");
        }
        private static HarmonyMethod Hook(string name) { return new HarmonyMethod(AccessTools.Method(typeof(HarmonyAudit), name)); }
        internal static void Register(string owner, string plugin, Assembly assembly)
        { lock (gate) { owners[owner] = plugin; assemblies[assembly] = plugin; } }
        private static PluginLogger Logger(string owner, MethodBase patch)
        {
            string name;
            lock (gate)
            {
                if (!owners.TryGetValue(owner ?? "", out name) && (patch == null || !assemblies.TryGetValue(patch.Module.Assembly, out name))) name = owner ?? "unknown";
            }
            return new PluginLogger("Harmony:" + name);
        }
        internal static string Describe(MethodBase method)
        {
            if (method == null) return "<unresolved target>";
            return (method.DeclaringType == null ? "<dynamic>" : method.DeclaringType.FullName) + "." + method.Name + "(" +
                string.Join(", ", method.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name).ToArray()) + ")";
        }
        private static List<Entry> Entries(PatchInfo info)
        {
            var result = new List<Entry>();
            Add(result, "Prefix", info.prefixes); Add(result, "Postfix", info.postfixes);
            Add(result, "Transpiler", info.transpilers); Add(result, "Finalizer", info.finalizers); Add(result, "ILManipulator", info.ilmanipulators);
            return result;
        }
        private static void Add(List<Entry> target, string kind, Patch[] patches)
        { foreach (var patch in patches) if (patch.owner != AuditId) target.Add(new Entry { Kind = kind, Owner = patch.owner, Method = patch.PatchMethod }); }
        private static void BeforeWrapper(MethodBase original, PatchInfo patchInfo, out Change __state)
        {
            __state = null;
            try
            {
                var current = Entries(patchInfo);
                List<Entry> previous;
                lock (gate) { if (!installed.TryGetValue(original, out previous)) previous = new List<Entry>(); }
                var oldKeys = new HashSet<string>(previous.Select(p => p.Key));
                var newKeys = new HashSet<string>(current.Select(p => p.Key));
                __state = new Change { Original = original, Current = current, Added = current.Where(p => !oldKeys.Contains(p.Key)).ToList(), Removed = previous.Where(p => !newKeys.Contains(p.Key)).ToList() };
                foreach (var patch in __state.Added) Logger(patch.Owner, patch.Method).Info("ATTEMPT " + patch.Kind + " " + Describe(patch.Method) + " -> " + Describe(original) + " | owner=" + patch.Owner);
            }
            catch (Exception ex) { new PluginLogger("Harmony").Error("Audit inspection failed: " + ex.Message); }
        }
        private static Exception AfterWrapper(Change __state, Exception __exception)
        {
            if (__state == null) return __exception;
            try
            {
                if (__exception == null)
                {
                    lock (gate) installed[__state.Original] = __state.Current;
                    foreach (var patch in __state.Added) Logger(patch.Owner, patch.Method).Info("INSTALLED " + patch.Kind + " " + Describe(patch.Method) + " -> " + Describe(__state.Original));
                    foreach (var patch in __state.Removed) Logger(patch.Owner, patch.Method).Info("REMOVED " + patch.Kind + " " + Describe(patch.Method) + " -> " + Describe(__state.Original));
                }
                else foreach (var patch in __state.Added) Logger(patch.Owner, patch.Method).Error("FAILED " + patch.Kind + " " + Describe(patch.Method) + " -> " + Describe(__state.Original) + ": " + __exception);
            }
            catch (Exception ex) { new PluginLogger("Harmony").Error("Audit result logging failed: " + ex.Message); }
            return __exception; // Preserve Harmony's failure behavior.
        }
        private static void BeforeClass(Harmony ___instance, Type ___containerType, HarmonyMethod ___containerAttributes, out ClassAttempt __state)
        {
            __state = null;
            if (___containerAttributes == null || ___instance.Id == AuditId) return;
            __state = new ClassAttempt { Type = ___containerType, Logger = Logger(___instance.Id, ___containerType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault()) };
            __state.Logger.Info("ATTEMPT patch class " + ___containerType.FullName + " | owner=" + ___instance.Id);
        }
        private static Exception AfterClass(ClassAttempt __state, List<MethodInfo> __result, Exception __exception)
        {
            if (__state != null)
            {
                if (__exception != null) __state.Logger.Error("FAILED patch class " + __state.Type.FullName + ": " + __exception);
                else if (__result == null || __result.Count == 0) __state.Logger.Info("SKIPPED patch class " + __state.Type.FullName + " (no replacement methods; Prepare/target selection may have skipped it)");
            }
            return __exception;
        }
        private static void BeforeManual(Harmony ___instance, MethodBase ___original, out ClassAttempt __state)
        {
            __state = null;
            if (___instance.Id == AuditId) return;
            __state = new ClassAttempt { Logger = Logger(___instance.Id, null) };
            __state.Logger.Info("REQUEST manual patch -> " + Describe(___original) + " | owner=" + ___instance.Id);
        }
        private static Exception AfterManual(ClassAttempt __state, Exception __exception)
        { if (__state != null && __exception != null) __state.Logger.Error("FAILED manual patch: " + __exception); return __exception; }
        private static void BeforeReverse(HarmonyMethod standin, MethodBase original, out ClassAttempt __state)
        {
            __state = new ClassAttempt { Logger = Logger("reverse", standin.method) };
            __state.Logger.Info("ATTEMPT ReversePatch " + Describe(standin.method) + " <- " + Describe(original));
        }
        private static Exception AfterReverse(ClassAttempt __state, Exception __exception)
        { if (__state != null) { if (__exception == null) __state.Logger.Info("INSTALLED ReversePatch"); else __state.Logger.Error("FAILED ReversePatch: " + __exception); } return __exception; }
        internal static void Summary(string owner, Assembly assembly)
        {
            Entry[] patches;
            lock (gate) patches = installed.Values.SelectMany(p => p).Where(p => p.Owner == owner || p.Method.Module.Assembly == assembly).ToArray();
            Logger(owner, null).Info("SUMMARY " + patches.Length + " installed Harmony hook(s)" + (patches.Length == 0 ? " (none)" : ": " + string.Join(", ", patches.GroupBy(p => p.Kind).Select(g => g.Count() + " " + g.Key).ToArray())));
        }
    }
}
