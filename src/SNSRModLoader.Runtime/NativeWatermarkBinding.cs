using HarmonyLib;
using UnityEngine;

namespace SNSRModLoader.Runtime
{
    // Start is shared by all four builds. The game's own coroutine decides when
    // loading has finished and deactivates this object; never duplicate its waits.
    [HarmonyPatch(typeof(uGUI_BuildWatermark), "Start")]
    internal static class NativeWatermarkStart
    {
        private static void Prefix(uGUI_BuildWatermark __instance)
        { if (LoaderHost.Instance != null) LoaderHost.Instance.Observe(__instance); }
    }
    [HarmonyPatch(typeof(uGUI_BuildWatermark), "UpdateText")]
    internal static class NativeWatermarkText
    {
        private static void Postfix(uGUI_BuildWatermark __instance)
        { if (LoaderHost.Instance != null) LoaderHost.Instance.Observe(__instance); }
    }

    public sealed class NativeWatermarkBinding : MonoBehaviour
    {
        internal uGUI_BuildWatermark Source { get; private set; }
        internal GameFont Font { get; private set; }
        private LoaderHost host;
        private Canvas[] canvases;
        private CanvasGroup[] groups;

        internal void Bind(LoaderHost owner, uGUI_BuildWatermark source)
        {
            host = owner; Source = source;
            Font = GameFont.FromWatermark(source);
            canvases = source.GetComponentsInParent<Canvas>(true);
            groups = source.GetComponentsInParent<CanvasGroup>(true);
        }
        internal bool Visible
        {
            get
            {
                if (Source == null || !Source.gameObject.activeInHierarchy || Font == null || Font.Graphic == null || !Font.Graphic.isActiveAndEnabled || Font.Graphic.color.a <= 0) return false;
                foreach (var canvas in canvases) if (canvas == null || !canvas.enabled) return false;
                foreach (var group in groups)
                {
                    if (group == null) continue;
                    if (group.alpha <= 0) return false;
                    if (group.ignoreParentGroups) break;
                }
                return true;
            }
        }
        private void OnEnable() { if (host != null) host.RefreshWatermark(); }
        private void OnDisable() { if (host != null) host.RefreshWatermark(); }
        private void OnDestroy() { if (host != null) host.Forget(this); }
        private void OnTransformParentChanged()
        { if (host != null && Source != null) { Bind(host, Source); host.RefreshWatermark(); } }
    }
}
