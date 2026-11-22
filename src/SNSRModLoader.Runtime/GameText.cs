using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace SNSRModLoader.Runtime
{
    // Later Subnautica menus use TMP. Reflection keeps its newer CLR assembly
    // optional, so this same net35 runtime also works in September 2018.
    internal sealed class GameFont
    {
        internal Type ComponentType;
        internal object Font;
        internal bool Tmp;
        internal Graphic Graphic;
        internal string Name { get { return ((UnityEngine.Object)Font).name; } }
        internal static GameFont FromWatermark(uGUI_BuildWatermark watermark)
        {
            var text = watermark.GetComponent<Text>();
            if (text != null && text.font != null) return new GameFont { ComponentType = typeof(Text), Font = text.font, Graphic = text };
            var tmp = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            if (tmp == null) return null;
            // The newer game versions serialize the exact TMP component in this field.
            var field = watermark.GetType().GetField("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var component = field == null ? watermark.GetComponent(tmp) : field.GetValue(watermark) as Component;
            if (component != null)
            {
                var font = tmp.GetProperty("font").GetValue(component, null);
                if (font != null) return new GameFont { ComponentType = tmp, Font = font, Tmp = true, Graphic = component as Graphic };
            }
            return null;
        }
        internal GameText Create(GameObject parent, string name, int size, Color color, string value, bool wrap = false, bool center = false)
        {
            var obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(parent.transform, false);
            var component = obj.AddComponent(ComponentType);
            var text = new GameText(component);
            text.Set("font", Font); text.Set("fontSize", size); text.Set("color", color); text.Set("raycastTarget", false);
            text.Set(Tmp ? "richText" : "supportRichText", false);
            if (Tmp)
            {
                text.Set("enableAutoSizing", false); text.Set("enableWordWrapping", wrap); text.SetEnum("overflowMode", "Overflow");
                text.SetEnum("alignment", center ? "Center" : "TopLeft");
            }
            else
            {
                text.Set("horizontalOverflow", wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow);
                text.Set("verticalOverflow", VerticalWrapMode.Overflow);
                text.Set("alignment", center ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft);
            }
            text.Set("text", value);
            return text;
        }
    }
    internal sealed class GameText
    {
        private readonly Component component;
        internal RectTransform Rect { get { return (RectTransform)component.transform; } }
        internal GameText(Component component) { this.component = component; }
        internal void Set(string name, object value)
        {
            var property = component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (!property.PropertyType.IsInstanceOfType(value)) value = Convert.ChangeType(value, property.PropertyType);
            property.SetValue(component, value, null);
        }
        internal void SetEnum(string name, string value)
        {
            var property = component.GetType().GetProperty(name);
            property.SetValue(component, Enum.Parse(property.PropertyType, value), null);
        }
        internal float PreferredHeight { get { return Convert.ToSingle(component.GetType().GetProperty("preferredHeight").GetValue(component, null)); } }
    }
}
