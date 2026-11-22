#if SNSR_DIAGNOSTICS
using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace SNSRModLoader.Runtime
{
    public sealed partial class LoaderHost
    {
        private bool captured;
        private IEnumerator CaptureDiagnostic()
        {
            yield return new WaitForSeconds(3f);
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(Path.GetDirectoryName(typeof(LoaderRuntime).Assembly.Location), "menu-smoke.png");
            if (LoaderRuntime.Game == GameVersion.March2023 || LoaderRuntime.Game == GameVersion.October2025)
            {
                CaptureOverlayDiagnostic(path);
                yield break;
            }
            var capture = LoaderRuntime.Game == GameVersion.September2018 ? typeof(Application).GetMethod("CaptureScreenshot", new[] { typeof(string) }) : null;
            if (capture == null)
            {
                var type = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
                if (type != null) capture = type.GetMethod("CaptureScreenshot", new[] { typeof(string), typeof(int) });
            }
            // TMP's dynamic atlas can leave its small render texture active.
            // Capture the actual back buffer rather than that font atlas.
            RenderTexture.active = null;
            if (capture != null) { capture.Invoke(null, capture.GetParameters().Length == 1 ? new object[] { path } : new object[] { path, 1 }); LoaderRuntime.Diagnostic("Smoke screenshot requested: " + path); }
        }
        private void CaptureOverlayDiagnostic(string path)
        {
            // Newer players stop presenting their back buffer in a hidden test
            // window. Render a copy of the actual overlay to inspect its pixels.
            var copy = Instantiate(overlay);
            var cameraObject = new GameObject("SNSR diagnostic camera");
            var camera = cameraObject.AddComponent<Camera>();
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                foreach (var item in copy.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 31;
                camera.orthographic = true; camera.orthographicSize = Screen.height / 2f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.08f, 0.12f, 0.18f);
                camera.cullingMask = 1 << 31; camera.targetTexture = target;
                var canvas = copy.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target;
                pixels = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); pixels.Apply();
                var conversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var bytes = (byte[])conversion.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) }).Invoke(null, new object[] { pixels });
                File.WriteAllBytes(path, bytes);
                LoaderRuntime.Diagnostic("Smoke overlay rendered offscreen using the live menu font: " + path);
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null; RenderTexture.ReleaseTemporary(target);
                if (pixels != null) Destroy(pixels); Destroy(copy); Destroy(cameraObject);
            }
        }
    }
}
#endif
