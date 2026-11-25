using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SNSRModLoader.Runtime
{
    public sealed class LoadedPlugin
    {
        public PluginInfo Info;
        public Plugin Instance;
        public PluginContext Context;
    }
    public static class LoaderRuntime
    {
        private static bool started;
        private static StreamWriter log;
        private static readonly object logGate = new object();
        public static readonly List<LoadedPlugin> Loaded = new List<LoadedPlugin>();
        public static readonly List<string> Warnings = new List<string>();
        public static readonly List<PluginInfo> Conflicted = new List<PluginInfo>();
        public static GameVersion Game { get; private set; }
        public static VerificationState VerificationStatus { get; private set; }
        public static VerificationState KnownVerificationStatus { get; private set; }
        public static bool VerificationChecksEnabled { get; private set; }
        private static int startupWarnings;
        internal static int Revision;
        private static int reportedWarnings;
        public static bool Eligible { get { return VerificationStatus == VerificationState.Available && Warnings.Count == 0 && Loaded.All(p => p.Info.Verified); } }
        public static bool UCEEligible { get { return VerificationChecksEnabled && startupWarnings == 0 && Conflicted.Count == 0 &&
            (VerificationStatus == VerificationState.Available || KnownVerificationStatus == VerificationState.Available) &&
            Loaded.All(p => p.Info.Status == PluginStatus.Verified || (p.Info.Status == PluginStatus.Known && p.Info.UCEAllowed)); } }
        public static void Start()
        {
            if (started) return;
            started = true;
            try
            {
                var mods = Path.GetDirectoryName(Path.GetDirectoryName(typeof(LoaderRuntime).Assembly.Location));
                Directory.CreateDirectory(Path.Combine(mods, "Settings"));
                var loader = Path.Combine(mods, "Loader");
                log = new StreamWriter(Path.Combine(loader, "loader.log"), false) { AutoFlush = true };
                PluginLogger.Sink = Write;
                HarmonyAudit.Install();
                Game = Registry.Detect(Path.Combine(Path.Combine(Path.Combine(Path.GetDirectoryName(mods), "Subnautica_Data"), "Managed"), "Assembly-CSharp.dll"));
                if (Game == GameVersion.Unknown) throw new NotSupportedException("Unknown game build; refusing to load plugins.");
                Write("SNSRModLoader " + typeof(Plugin).Assembly.GetName().Version + " | " + Registry.DisplayName(Game) + " | Unity " + Application.unityVersion);
                var host = new GameObject("SNSRModLoader");
                Object.DontDestroyOnLoad(host);
                host.AddComponent<SceneCleanerPreserve>();
                var behaviour = host.AddComponent<LoaderHost>();
                behaviour.InstallWatermarkHooks();
                var updateCandidates = new List<PluginInfo>();
                var infos = Discovery.Scan(mods, Game, Warn, updateCandidates.Add);
                Conflicted.AddRange(updateCandidates.Where(p => p.Status == PluginStatus.Conflicted));
                if (Conflicted.Count > 0) Revision++;
                var directories = infos.Select(p => Path.Combine(Path.Combine(mods, "Settings"), p.Id)).ToArray();
                AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
                {
                    var requested = new AssemblyName(args.Name);
                    if (requested.Name != Path.GetFileName(requested.Name)) return null;
                    foreach (var directory in directories)
                    {
                        var file = Path.Combine(directory, requested.Name + ".dll");
                        if (File.Exists(file) && AssemblyName.GetAssemblyName(file).FullName == requested.FullName) return Assembly.LoadFrom(file);
                    }
                    return null;
                };
                foreach (var info in infos)
                {
                    Plugin plugin = null;
                    PluginContext context = null;
                    try
                    {
                        if (info.Dependencies.Any(d => !Loaded.Any(p => string.Equals(p.Info.Id, d.Id, StringComparison.OrdinalIgnoreCase)))) throw new InvalidOperationException("A required dependency failed to load.");
                        context = new PluginContext(info.Id, mods, Game);
                        var assembly = Assembly.LoadFrom(info.Path);
                        HarmonyAudit.Register(context.Harmony.Id, info.Name + " [" + info.Id + "]", assembly);
                        Write("Loading " + info.Name + " " + info.Version + " from " + Path.GetFileName(info.Path));
                        var type = assembly.GetType(info.TypeName, true);
                        if (!typeof(Plugin).IsAssignableFrom(type)) throw new InvalidOperationException("Plugin must derive from SNSRModLoader.Plugin.");
                        plugin = (Plugin)Activator.CreateInstance(type);
                        plugin.Load(context);
                        HarmonyAudit.Summary(context.Harmony.Id, assembly);
                        Loaded.Add(new LoadedPlugin { Info = info, Instance = plugin, Context = context });
                        Revision++;
                        Write("Loaded " + info.Name + " " + info.Version + " | " + info.Sha256);
                    }
                    catch (Exception ex)
                    {
                        Warn(Path.GetFileName(info.Path) + " (" + info.Name + ") failed to load: " + ex);
                        try { if (plugin != null) plugin.Unload(); } catch (Exception cleanup) { Write(cleanup.ToString()); }
                        try { if (context != null) context.Harmony.UnpatchSelf(); } catch (Exception cleanup) { Write(cleanup.ToString()); }
                    }
                }
                Write("Startup complete. Loaded " + Loaded.Count + " plugin(s). Registry eligibility: " + Eligible);
                ShowWarnings();
                var settings = new SettingsFile(Path.Combine(Path.Combine(loader, "Config"), "loader.ini"));
                startupWarnings = Warnings.Count;
                VerificationChecksEnabled = settings.Get("checkVerifiedMods", true);
                var verificationUrl = settings.Get("verifiedModsUrl", "");
                var knownUrl = settings.Get("knownModsUrl", "");
                behaviour.BeginVerification(verificationUrl.Length > 0 ? verificationUrl : OnlineSources.Get("verifiedModsUrl"),
                    knownUrl.Length > 0 ? knownUrl : OnlineSources.Get("knownModsUrl"), mods, VerificationChecksEnabled);
                LoaderUpdates.Begin(loader, settings, Game, updateCandidates);
            }
            catch (Exception ex) { Warn("Loader startup failed: " + ex); ShowWarnings(); }
        }
        private static void Write(string message)
        {
            try { lock (logGate) { if (log != null) log.WriteLine(DateTime.UtcNow.ToString("o") + " " + message); } } catch { }
            Debug.Log("[SNSRModLoader] " + message);
        }
        internal static void Diagnostic(string message) { Write(message); }
        private static void Warn(string message) { Warnings.Add(message); Revision++; Write("WARNING: " + message); }
        private static void ShowWarnings()
        {
            if (reportedWarnings >= Warnings.Count) return;
            var pending = Warnings.Skip(reportedWarnings).Select(w => w.Split(new[] { '\r', '\n' })[0]).ToArray();
            reportedWarnings = Warnings.Count;
#if SNSR_DIAGNOSTICS
            if (Environment.GetEnvironmentVariable("SNSR_SMOKE_TEST") == "1") { Write("Diagnostic: external Windows warning dialog suppressed for automated smoke test."); return; }
#endif
            ExternalDialogs.Show(pending);
        }
        internal static void ApplyVerification(ModCatalogResult result)
        {
            VerificationStatus = result.Verified.State;
            KnownVerificationStatus = result.Known.State;
            Revision++;
            if (VerificationStatus == VerificationState.Disabled) Write(result.Verified.Message);
            else {
                if (VerificationStatus == VerificationState.Unavailable) Warn(result.Verified.Message); else Write(result.Verified.Message);
                if (KnownVerificationStatus == VerificationState.Unavailable) Warn(result.Known.Message);
                else if (!string.IsNullOrEmpty(result.Known.Message)) Write(result.Known.Message);
            }
            foreach (var plugin in Loaded)
            {
                var info = plugin.Info;
                ModApproval.Classify(info, result, Game);
                var warning = ModApproval.Warning(info);
                if (VerificationChecksEnabled && warning != null) Warn(warning);
            }
            Write("Registry eligibility: " + Eligible);
            Write("Unofficial Category Extensions eligibility: " + UCEEligible);
            ShowWarnings();
        }
        internal static void Shutdown()
        {
            for (int i = Loaded.Count - 1; i >= 0; i--)
            {
                try { Loaded[i].Instance.Unload(); } catch (Exception ex) { Write(ex.ToString()); }
                try { Loaded[i].Context.Harmony.UnpatchSelf(); } catch (Exception ex) { Write(ex.ToString()); }
            }
            Loaded.Clear(); Conflicted.Clear(); PluginLogger.Sink = null;
            if (log != null) { log.Dispose(); log = null; }
        }
    }

    public sealed partial class LoaderHost : MonoBehaviour
    {
        internal static LoaderHost Instance { get; private set; }
        private GameObject overlay;
        private readonly List<GameText> modLabels = new List<GameText>();
        private readonly List<PluginInfo> displayPlugins = new List<PluginInfo>();
        private readonly List<NativeWatermarkBinding> watermarks = new List<NativeWatermarkBinding>();
        private NativeWatermarkBinding current;
        private object fontAsset;
        private Harmony watermarkHarmony;
        private int revision = -1;
        private volatile bool verificationFinished;
        private ModCatalogResult verificationResult;
        private int DisplayCount { get { return LoaderRuntime.Loaded.Count + LoaderRuntime.Conflicted.Count; } }
        internal bool IsWatermarkVisible { get { return overlay != null && overlay.activeInHierarchy; } }
        internal NativeWatermarkBinding[] Bindings { get { return watermarks.ToArray(); } }
        private void Awake() { Instance = this; }
        internal void InstallWatermarkHooks()
        {
            watermarkHarmony = new Harmony("SNSRModLoader.watermark");
            watermarkHarmony.PatchAll(typeof(NativeWatermarkStart).Assembly);
            // Covers initialization by another bootstrap before our hooks existed.
            foreach (var source in Object.FindObjectsOfType<uGUI_BuildWatermark>()) Observe(source);
        }
        internal void Observe(uGUI_BuildWatermark source)
        {
            var binding = source.GetComponent<NativeWatermarkBinding>();
            if (binding == null) binding = source.gameObject.AddComponent<NativeWatermarkBinding>();
            binding.Bind(this, source);
            if (!watermarks.Contains(binding)) watermarks.Add(binding);
            RefreshWatermark();
        }
        internal void Forget(NativeWatermarkBinding binding)
        { watermarks.Remove(binding); RefreshWatermark(); }
        public void BeginVerification(string url, string mods)
        { BeginVerification(url, OnlineSources.Get("knownModsUrl"), mods, LoaderRuntime.VerificationChecksEnabled); }
        public void BeginVerification(string url, string knownUrl, string mods, bool enabled)
        {
            var plugins = LoaderRuntime.Loaded.Select(p => p.Info).ToArray();
            if (enabled) foreach (var info in plugins) {
                var data = Path.Combine(Path.Combine(mods, "Settings"), info.Id);
                info.HasAdditionalAssemblies = Directory.Exists(data) && Directory.GetFiles(data, "*.dll", SearchOption.AllDirectories).Length > 0;
            }
            if (!enabled) { LoaderRuntime.ApplyVerification(ModApproval.Fetch(false, url, knownUrl, plugins, LoaderRuntime.Game)); return; }
            verificationFinished = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                verificationResult = ModApproval.Fetch(true, url, knownUrl, plugins, LoaderRuntime.Game);
                verificationFinished = true;
            });
            StartCoroutine(CompleteVerification(mods));
        }
        private IEnumerator CompleteVerification(string mods)
        {
            while (!verificationFinished) yield return null;
            LoaderRuntime.ApplyVerification(verificationResult);
        }
        private void LateUpdate() { RefreshWatermark(); }
        internal void RefreshWatermark()
        {
            NativeWatermarkBinding selected = null;
            for (int i = watermarks.Count - 1; i >= 0; i--)
                if (watermarks[i] != null && watermarks[i].Visible) { selected = watermarks[i]; break; }
            if (selected == null) { if (overlay != null && overlay.activeSelf) overlay.SetActive(false); return; }
            var template = selected.Font;
            if (revision != LoaderRuntime.Revision || displayPlugins.Count != DisplayCount) RefreshDisplayOrder();
            if (overlay != null && !overlay.activeSelf) overlay.SetActive(true);
            if (selected == current && template.Font == fontAsset && overlay != null && modLabels.Count == DisplayCount)
            {
                if (revision != LoaderRuntime.Revision) { UpdateModLabels(); revision = LoaderRuntime.Revision; }
                return;
            }
            current = selected; fontAsset = template.Font;
            revision = LoaderRuntime.Revision;
            if (overlay != null) { overlay.SetActive(false); Destroy(overlay); }
            modLabels.Clear();
            overlay = new GameObject("SNSRModLoader watermark", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            overlay.transform.SetParent(transform, false);
            var canvas = overlay.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
            var scaler = overlay.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            var nativeCanvas = selected.Source.GetComponentInParent<Canvas>();
            var nativeScaler = nativeCanvas == null ? null : nativeCanvas.rootCanvas.GetComponent<CanvasScaler>();
            if (nativeScaler != null)
            {
                scaler.uiScaleMode = nativeScaler.uiScaleMode; scaler.referenceResolution = nativeScaler.referenceResolution;
                scaler.screenMatchMode = nativeScaler.screenMatchMode; scaler.matchWidthOrHeight = nativeScaler.matchWidthOrHeight;
                scaler.referencePixelsPerUnit = nativeScaler.referencePixelsPerUnit;
            }
            AddLine("SNSRModLoader", Color.white, template, 0);
            for (int i = 0; i < displayPlugins.Count; i++)
                modLabels.Add(AddLine(ModLine(i), ModColor(displayPlugins[i]), template, i + 1));
            LoaderRuntime.Diagnostic("Native-lifecycle watermark created with game font '" + template.Name + "', " + LoaderRuntime.Loaded.Count + " loaded plugin(s), " + LoaderRuntime.Conflicted.Count + " conflicted plugin(s).");
#if SNSR_DIAGNOSTICS
            if (!captured && Environment.GetEnvironmentVariable("SNSR_SMOKE_TEST") == "1") { captured = true; StartCoroutine(CaptureDiagnostic()); }
#endif
        }
        private void RefreshDisplayOrder()
        {
            // Stable partition for display only; dependency/load/unload order stays intact.
            displayPlugins.Clear();
            foreach (var plugin in LoaderRuntime.Loaded) if (plugin.Info.Status == PluginStatus.Verified) displayPlugins.Add(plugin.Info);
            foreach (var plugin in LoaderRuntime.Loaded) if (plugin.Info.Status == PluginStatus.Known && plugin.Info.UCEAllowed) displayPlugins.Add(plugin.Info);
            foreach (var plugin in LoaderRuntime.Loaded) if (plugin.Info.Status == PluginStatus.Known && !plugin.Info.UCEAllowed) displayPlugins.Add(plugin.Info);
            foreach (var plugin in LoaderRuntime.Loaded) if (plugin.Info.Status == PluginStatus.Unknown) displayPlugins.Add(plugin.Info);
            displayPlugins.AddRange(LoaderRuntime.Conflicted);
        }
        private string ModLine(int index)
        {
            bool conflict = displayPlugins[index].Status == PluginStatus.Conflicted;
            bool firstConflict = conflict && (index == 0 || displayPlugins[index - 1].Status != PluginStatus.Conflicted);
            string prefix = firstConflict ? "Conflicts (not loaded): " : index == 0 ? "Using " : "";
            bool comma = index + 1 < displayPlugins.Count && (conflict || displayPlugins[index + 1].Status != PluginStatus.Conflicted);
            return prefix + displayPlugins[index].Name + (comma ? "," : "");
        }
        private static Color ModColor(PluginInfo info)
        {
            switch (ModApproval.GetColor(info, LoaderRuntime.VerificationChecksEnabled)) {
                case PluginColor.White: return Color.white;
                case PluginColor.Yellow: return Color.yellow;
                case PluginColor.Orange: return new Color(1f, 0.5f, 0f, 1f);
                default: return Color.red;
            }
        }
        private void UpdateModLabels()
        {
            for (int i = 0; i < modLabels.Count; i++)
            {
                modLabels[i].Set("text", ModLine(i));
                modLabels[i].Set("color", ModColor(displayPlugins[i]));
            }
        }
        private GameText AddLine(string value, Color color, GameFont template, int line)
        {
            var text = template.Create(overlay, "Line", 20, color, value);
            var rect = text.Rect; rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(12, -12 - line * 24); rect.sizeDelta = new Vector2(1800, 24);
            return text;
        }
        private void OnApplicationQuit()
        { if (watermarkHarmony != null) watermarkHarmony.UnpatchSelf(); LoaderRuntime.Shutdown(); }
    }
}
