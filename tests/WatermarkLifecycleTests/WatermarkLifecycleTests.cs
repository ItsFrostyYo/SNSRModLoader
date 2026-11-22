using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using SNSRModLoader;
using SNSRModLoader.Runtime;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[SNSRModLoader("snsr.tests.watermark", "Watermark Lifecycle Tests", "1.0.0")]
[TargetVersions(GameVersion.September2018, GameVersion.December2021, GameVersion.March2023, GameVersion.October2025)]
public sealed class WatermarkLifecycleTests : Plugin
{
    private GameObject host;
    public override void Load(PluginContext context)
    {
        host = new GameObject("SNSR watermark lifecycle test plugin");
        Object.DontDestroyOnLoad(host); host.AddComponent<SceneCleanerPreserve>();
        host.AddComponent<WatermarkLifecycleTestRunner>().Logger = context.Logger;
    }
    public override void Unload() { if (host != null) Object.Destroy(host); }
}

public sealed class WatermarkLifecycleTestRunner : MonoBehaviour
{
    internal PluginLogger Logger;
    private IEnumerator Start()
    {
        var deadline = Time.realtimeSinceStartup + 25;
        while ((LoaderHost.Instance == null || !LoaderHost.Instance.IsWatermarkVisible) && Time.realtimeSinceStartup < deadline) yield return null;
        var host = LoaderHost.Instance;
        var originals = new List<KeyValuePair<Graphic, bool>>();
        GameObject fixture = null;
        try
        {
            Check(host != null && host.IsWatermarkVisible, "native Start hook must bind before a menu lookup");
            GameFont font = null;
            foreach (var binding in host.Bindings)
            {
                if (binding.Font == null || binding.Font.Graphic == null) continue;
                if (binding.Visible) font = binding.Font;
                originals.Add(new KeyValuePair<Graphic, bool>(binding.Font.Graphic, binding.Font.Graphic.enabled));
            }
            Check(font != null, "font must come from the real version watermark");
            foreach (var original in originals) original.Key.enabled = false;
            // A temporary, isolated native watermark fixture exercises the same
            // callbacks as scene replacement without loading or modifying a save.
            fixture = new GameObject("Watermark lifecycle fixture", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            var text = font.Create(fixture, "Native watermark", 20, Color.white, "test");
            var source = text.Rect.gameObject.AddComponent<uGUI_BuildWatermark>();
            source.enabled = false; // Its text still renders; script.enabled is not a UI visibility gate.
            var field = AccessTools.Field(typeof(uGUI_BuildWatermark), "text");
            if (field != null) field.SetValue(source, text.Rect.GetComponent(font.ComponentType));
            host.Observe(source);
            Check(host.IsWatermarkVisible, "creation is immediate without a main menu or one-second delay");
            var overlayField = AccessTools.Field(typeof(LoaderHost), "overlay");
            var overlay = (GameObject)overlayField.GetValue(host);
            LoaderRuntime.Revision++;
            host.RefreshWatermark();
            Check(overlay == (GameObject)overlayField.GetValue(host), "verification/status changes reuse the existing canvas");
            var lastLine = overlay.transform.GetChild(overlay.transform.childCount - 1).GetComponent(font.ComponentType);
            var lastText = (string)font.ComponentType.GetProperty("text").GetValue(lastLine, null);
            Check(!lastText.EndsWith(","), "the last mod has no trailing comma");
            source.gameObject.SetActive(false);
            Check(!host.IsWatermarkVisible, "native object deactivation hides the loader in the same frame");
            source.gameObject.SetActive(true);
            Check(host.IsWatermarkVisible, "native object reactivation restores the loader in the same frame");
            fixture.GetComponent<CanvasGroup>().alpha = 0;
            host.RefreshWatermark(); Check(!host.IsWatermarkVisible, "hidden loading canvas hides the loader");
            fixture.GetComponent<CanvasGroup>().alpha = 1;
            host.RefreshWatermark(); Check(host.IsWatermarkVisible, "loading canvas visibility restores the loader");
            fixture.GetComponent<Canvas>().enabled = false;
            host.RefreshWatermark(); Check(!host.IsWatermarkVisible, "disabled native canvas hides the loader");
            fixture.GetComponent<Canvas>().enabled = true;
            host.RefreshWatermark(); Check(host.IsWatermarkVisible, "enabled native canvas restores the loader");
            // DestroyImmediate is confined to this test-created scene object.
            Object.DestroyImmediate(fixture); fixture = null;
            Check(!host.IsWatermarkVisible, "destroyed native watermark detaches immediately");
            Logger.Info("Watermark lifecycle PASS: immediate creation, reactivation, loading visibility, canvas visibility, scene cleanup; no menu dependency");
        }
        catch (Exception ex) { Logger.Error("Watermark lifecycle FAIL: " + ex); }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            foreach (var original in originals) if (original.Key != null) original.Key.enabled = original.Value;
            if (host != null) host.RefreshWatermark();
        }
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
