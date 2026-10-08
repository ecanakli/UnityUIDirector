using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Writes one "[PROBE <id>] key=value ..." line per observation.
    internal static class ProbeLog
    {
        public static void Write(string id, params object[] keyValues)
        {
            var builder = new StringBuilder();
            builder.Append("[PROBE ").Append(id).Append(']');
            for (var i = 0; i + 1 < keyValues.Length; i += 2)
                builder.Append(' ').Append(keyValues[i]).Append('=').Append(Format(keyValues[i + 1]));
            Debug.Log(builder.ToString());
        }

        public static string Ms(double milliseconds)
        {
            return milliseconds.ToString("F3", CultureInfo.InvariantCulture);
        }

        public static string Format(object value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case bool flag:
                    return flag ? "true" : "false";
                case float single:
                    return single.ToString("0.###", CultureInfo.InvariantCulture);
                case double number:
                    return number.ToString("0.###", CultureInfo.InvariantCulture);
                case Vector2 vector:
                    return vector.x.ToString("0.#", CultureInfo.InvariantCulture) + "," + vector.y.ToString("0.#", CultureInfo.InvariantCulture);
                case Object unityObject:
                    return unityObject ? unityObject.name : "null";
                case string text:
                    return text.IndexOfAny(new[] { ' ', '=' }) >= 0 ? "\"" + text + "\"" : text;
                case IEnumerable sequence:
                {
                    var parts = new List<string>();
                    foreach (var item in sequence)
                        parts.Add(Format(item));
                    return parts.Count == 0 ? "-" : string.Join(",", parts);
                }
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }

    // Destroys everything a probe created, also when the probe failed.
    internal sealed class ProbeCleanup
    {
        readonly List<Object> m_Objects = new List<Object>();

        public T Track<T>(T obj) where T : Object
        {
            m_Objects.Add(obj);
            return obj;
        }

        public void DestroyAll()
        {
            for (var i = m_Objects.Count - 1; i >= 0; i--)
            {
                var obj = m_Objects[i];
                if (obj != null)
                    Object.DestroyImmediate(obj);
            }

            m_Objects.Clear();
        }
    }

    // Lets expected error logs through and keeps them as an observation.
    internal sealed class ProbeLogCapture : IDisposable
    {
        readonly bool m_PreviousIgnore;
        readonly List<string> m_Messages = new List<string>();

        public ProbeLogCapture()
        {
            m_PreviousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLog;
        }

        public int Count => m_Messages.Count;

        public string First => m_Messages.Count == 0 ? "none" : m_Messages[0];

        public void Dispose()
        {
            Application.logMessageReceived -= OnLog;
            LogAssert.ignoreFailingMessages = m_PreviousIgnore;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            m_Messages.Add(type + ":" + condition);
        }
    }

    internal static class ProbeStats
    {
        public static double Median(List<double> values)
        {
            if (values.Count == 0)
                return 0;

            var sorted = new List<double>(values);
            sorted.Sort();
            var middle = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) * 0.5;
        }
    }

    // Renders a Screen Space - Camera root into a small RenderTexture and reads it back; screen pixels are never read.
    internal static class ProbeRender
    {
        public const int TargetSize = 64;

        public sealed class RenderRig
        {
            public Camera Camera;
            public RenderTexture Target;
            public Texture2D Readback;
            public Canvas Root;
            public CanvasGroup RootGroup;
            public Canvas View;
            public Canvas Inner;
            public CanvasGroup ViewGroup;
        }

        public sealed class Sample
        {
            public Color Left;
            public Color Right;
        }

        public static RenderRig BuildRenderRig(ProbeCleanup cleanup, bool withRootGroup = false)
        {
            var rig = new RenderRig();
            rig.Target = cleanup.Track(new RenderTexture(TargetSize, TargetSize, 24, RenderTextureFormat.ARGB32));
            rig.Readback = cleanup.Track(new Texture2D(TargetSize, TargetSize, TextureFormat.RGBA32, false));

            var cameraObject = cleanup.Track(new GameObject("ProbeCamera"));
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            rig.Camera = cameraObject.AddComponent<Camera>();
            rig.Camera.orthographic = true;
            rig.Camera.orthographicSize = 5f;
            rig.Camera.nearClipPlane = 0.1f;
            rig.Camera.farClipPlane = 100f;
            rig.Camera.clearFlags = CameraClearFlags.SolidColor;
            rig.Camera.backgroundColor = Color.black;
            rig.Camera.allowHDR = false;
            rig.Camera.allowMSAA = false;
            rig.Camera.targetTexture = rig.Target;

            var root = ProbeUi.NewRootCanvas("ProbeRoot");
            cleanup.Track(root.gameObject);
            root.renderMode = RenderMode.ScreenSpaceCamera;
            root.worldCamera = rig.Camera;
            root.planeDistance = 5f;
            rig.Root = root;
            if (withRootGroup)
                rig.RootGroup = root.gameObject.AddComponent<CanvasGroup>();

            // The view is all red; the inner canvas inside it draws green over the left half.
            rig.View = ProbeUi.NewNestedCanvas("View", root.transform, 10, true, false);
            rig.ViewGroup = rig.View.gameObject.AddComponent<CanvasGroup>();
            ProbeUi.NewImage("ViewRed", rig.View.transform, Vector2.zero, Vector2.one).color = Color.red;
            rig.Inner = ProbeUi.NewNestedCanvas("Inner", rig.View.transform, 20, true, false);
            ProbeUi.SetAnchors((RectTransform)rig.Inner.transform, Vector2.zero, new Vector2(0.5f, 1f));
            ProbeUi.NewImage("InnerGreen", rig.Inner.transform, Vector2.zero, Vector2.one).color = Color.green;
            return rig;
        }

        // Renders the camera into the RenderTexture and reads one point per half back to the CPU.
        public static Sample ReadSample(RenderRig rig)
        {
            Canvas.ForceUpdateCanvases();
            rig.Camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = rig.Target;
            try
            {
                rig.Readback.ReadPixels(new Rect(0, 0, TargetSize, TargetSize), 0, 0, false);
                return new Sample
                {
                    Left = rig.Readback.GetPixel(TargetSize / 4, TargetSize / 2),
                    Right = rig.Readback.GetPixel(TargetSize * 3 / 4, TargetSize / 2),
                };
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        public static bool IsBaselineLook(Sample sample)
        {
            return Classify(sample.Left) == "green" && Classify(sample.Right) == "red";
        }

        public static string Classify(Color color)
        {
            if (color.r > 0.6f && color.g < 0.3f && color.b < 0.3f)
                return "red";
            if (color.g > 0.6f && color.r < 0.3f && color.b < 0.3f)
                return "green";
            if (color.r < 0.1f && color.g < 0.1f && color.b < 0.1f)
                return "black";
            return "other";
        }

        public static string Describe(Color color)
        {
            return Classify(color) + "#" + ColorUtility.ToHtmlStringRGB(color);
        }
    }

    // Builders for the small uGUI rigs the probes need.
    internal static class ProbeUi
    {
        // Batch mode gives no guaranteed Game view size; positions are derived from layout.
        public static bool ScreenIsUsable()
        {
            return Screen.width >= 64 && Screen.height >= 64;
        }

        public static IEnumerable<object> Frames(int count)
        {
            for (var i = 0; i < count; i++)
                yield return null;
        }

        public static GameObject NewObject(string name, Transform parent, bool active = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (!active)
                go.SetActive(false);
            if (parent != null)
                go.transform.SetParent(parent, false);
            return go;
        }

        public static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static void Stretch(RectTransform rect)
        {
            SetAnchors(rect, Vector2.zero, Vector2.one);
        }

        public static Canvas NewRootCanvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static Canvas NewNestedCanvas(string name, Transform parent, int sortingOrder, bool overrideSorting = true, bool raycaster = true, bool active = true)
        {
            var go = NewObject(name, parent, active);
            Stretch((RectTransform)go.transform);
            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = overrideSorting;
            if (overrideSorting)
                canvas.sortingOrder = sortingOrder;
            if (raycaster)
                go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static Image NewImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = NewObject(name, parent);
            SetAnchors((RectTransform)go.transform, anchorMin, anchorMax);
            return go.AddComponent<Image>();
        }

        public static Button NewButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = NewObject(name, parent);
            SetAnchors((RectTransform)go.transform, anchorMin, anchorMax);
            var image = go.AddComponent<Image>();
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        public static Text NewText(string name, Transform parent, string content)
        {
            var go = NewObject(name, parent);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            return text;
        }

        // A nested canvas with many graphics; layout is anchor-based so no layout group is involved.
        public static GameObject BuildHeavyView(Transform parent, int imageCount, int textCount, bool active = true)
        {
            const int columns = 20;
            var canvas = NewNestedCanvas("HeavyView", parent, 10, true, true, active);
            for (var i = 0; i < imageCount; i++)
            {
                var child = NewObject("Image" + i, canvas.transform);
                PlaceCell((RectTransform)child.transform, i, columns, 8f, 8f, 0f);
                child.AddComponent<Image>();
            }

            for (var i = 0; i < textCount; i++)
            {
                var text = NewText("Text" + i, canvas.transform, "Label " + i);
                PlaceCell((RectTransform)text.transform, i, columns, 60f, 20f, 400f);
            }

            return canvas.gameObject;
        }

        static void PlaceCell(RectTransform rect, int index, int columns, float width, float height, float topOffset)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            var column = index % columns;
            var row = index / columns;
            rect.anchoredPosition = new Vector2(column * (width + 2f), -(topOffset + row * (height + 2f)));
        }

        // An EventSystem without a module is enough for RaycastAll; the Input System module is opt-in.
        public static GameObject NewEventSystem(bool withInputModule)
        {
            var go = new GameObject("ProbeEventSystem");
            go.AddComponent<EventSystem>();
            if (withInputModule)
#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM
                go.AddComponent<InputSystemUIInputModule>();
#else
                go.AddComponent<StandaloneInputModule>();
#endif
            return go;
        }

        public static Vector2 ScreenPointOf(RectTransform rect, Camera camera = null)
        {
            return RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        }

        public static List<RaycastResult> Raycast(Vector2 screenPoint)
        {
            var results = new List<RaycastResult>();
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return results;

            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screenPoint }, results);
            return results;
        }

        public static string Describe(List<RaycastResult> results)
        {
            if (results.Count == 0)
                return "none";

            var names = new List<string>();
            foreach (var result in results)
                names.Add(result.gameObject != null ? result.gameObject.name : "null");
            return string.Join(">", names);
        }

        public static bool Hits(List<RaycastResult> results, GameObject target)
        {
            if (target == null)
                return false;

            foreach (var result in results)
            {
                if (result.gameObject == target)
                    return true;
            }

            return false;
        }

        // Which raycaster produced the first hit on the target.
        public static string ModuleOf(List<RaycastResult> results, GameObject target)
        {
            if (target == null)
                return "none";

            foreach (var result in results)
            {
                if (result.gameObject == target)
                    return result.module != null ? result.module.GetType().Name + "@" + result.module.gameObject.name : "null";
            }

            return "none";
        }

        public static bool HitsUnder(List<RaycastResult> results, Transform root)
        {
            foreach (var result in results)
            {
                if (result.gameObject != null && result.gameObject.transform.IsChildOf(root))
                    return true;
            }

            return false;
        }

        public static Transform FindDescendant(Transform root, string name)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == name)
                    return child;

                var nested = FindDescendant(child, name);
                if (nested != null)
                    return nested;
            }

            return null;
        }
    }
}
