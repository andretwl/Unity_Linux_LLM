using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using GladeAgenticAI.Core.Tools;

#if GLADE_UGUI
namespace GladeAgenticAI.Core.Tools.Implementations.UI
{
    internal static class UIHelpers
    {
        public static Type GetTmpTextType()
        {
            return Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        }

        public static Type GetTmpInputFieldType()
        {
            return Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
        }

        public static Type GetTmpDropdownType()
        {
            return Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro");
        }

        public static bool IsTextMeshProAvailable()
        {
            return GetTmpTextType() != null;
        }

        /// <summary>
        /// Result of TMP availability check for UI actions.
        /// </summary>
        public class TMPCheckResult
        {
            public bool IsAvailable { get; set; }
            public bool InstallationStarted { get; set; }
            public string Message { get; set; }
        }

        /// <summary>
        /// Checks if TextMeshPro is available for UI actions. 
        /// If not available, opens the import dialog and returns early with instructions.
        /// This should be called at the start of all UI tool Execute methods.
        /// </summary>
        public static TMPCheckResult EnsureTMPForUIActions()
        {
            // Check if Essential Resources are actually imported (check for Assets/TextMesh Pro/Resources folder)
            // Only check Resources folder - ignore type availability (types can exist without Resources)
            string resourcesPath = System.IO.Path.Combine(Application.dataPath, "TextMesh Pro", "Resources");
            bool essentialResourcesImported = System.IO.Directory.Exists(resourcesPath);
            
            // Also verify it has the Fonts & Materials subfolder and files
            if (essentialResourcesImported)
            {
                string fontsPath = System.IO.Path.Combine(resourcesPath, "Fonts & Materials");
                bool fontsExist = System.IO.Directory.Exists(fontsPath);
                bool hasFiles = false;
                try
                {
                    var files = System.IO.Directory.GetFiles(resourcesPath, "*", System.IO.SearchOption.TopDirectoryOnly);
                    hasFiles = files.Length > 0;
                }
                catch { }
                
                if (fontsExist && hasFiles)
                {
                    return new TMPCheckResult { IsAvailable = true, InstallationStarted = false, Message = null };
                }
            }

            // TMP not available - open import dialog and return early with instructions
            UnityEngine.Debug.LogError("[GladeAI] TMP Essential Resources not found - opening import dialog");
            EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
            
            return new TMPCheckResult 
            { 
                IsAvailable = false, 
                InstallationStarted = true, 
                Message = "TextMeshPro Essential Resources are not installed. " +
                         "I've opened the import dialog in Unity Editor. " +
                         "Please click the 'Import' button in the dialog, wait for Unity to finish importing, " +
                         "then retry your UI action request. This is a one-time setup." 
            };
        }

        /// <summary>
        /// Adds TMP text component to a GameObject. Uses EnsureTMPForUIActions to ensure TMP is available.
        /// Returns true if TMP was added, false if TMP is not available.
        /// </summary>
        public static bool TryAddTmpText(UnityEngine.GameObject obj, Dictionary<string, object> args)
        {
            // Ensure TMP is available (this will auto-import if package is installed)
            var tmpCheck = EnsureTMPForUIActions();
            if (!tmpCheck.IsAvailable)
            {
                UnityEngine.Debug.LogError($"[GladeAI] TMP not available: {tmpCheck.Message}");
                return false;
            }

            var tmpType = GetTmpTextType();
            if (tmpType == null)
            {
                UnityEngine.Debug.LogError("[GladeAI] TMP type not found even though TMP check passed.");
                return false;
            }

            var tmpComponent = obj.AddComponent(tmpType);
            TrySetTmpTextProperties(tmpComponent, args);
            return true; // TMP was successfully added
        }

        public static void TrySetTmpTextProperties(Component tmpComponent, Dictionary<string, object> args)
        {
            if (tmpComponent == null || args == null)
            {
                return;
            }

            var tmpType = tmpComponent.GetType();
            if (args.ContainsKey("text"))
            {
                var textProp = tmpType.GetProperty("text");
                textProp?.SetValue(tmpComponent, args["text"].ToString(), null);
            }
            if (args.ContainsKey("color"))
            {
                var colorProp = tmpType.GetProperty("color");
                colorProp?.SetValue(tmpComponent, ToolUtils.ParseColor(args["color"].ToString()), null);
            }
            if (args.ContainsKey("fontSize") && float.TryParse(args["fontSize"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float size))
            {
                var sizeProp = tmpType.GetProperty("fontSize");
                sizeProp?.SetValue(tmpComponent, size, null);
            }
            if (args.ContainsKey("alignment"))
            {
                var alignmentProp = tmpType.GetProperty("alignment");
                if (alignmentProp != null)
                {
                    var alignmentType = alignmentProp.PropertyType;
                    try
                    {
                        var alignmentValue = System.Enum.Parse(alignmentType, args["alignment"].ToString(), true);
                        alignmentProp.SetValue(tmpComponent, alignmentValue, null);
                    }
                    catch
                    {
                        // Ignore invalid alignment values.
                    }
                }
            }
        }

        public static void TrySetLegacyTextProperties(Text textComponent, Dictionary<string, object> args)
        {
            if (textComponent == null || args == null)
            {
                return;
            }

            if (args.ContainsKey("text"))
            {
                textComponent.text = args["text"].ToString();
            }

            if (args.ContainsKey("color"))
            {
                textComponent.color = ToolUtils.ParseColor(args["color"].ToString());
            }

            if (args.ContainsKey("fontSize") && int.TryParse(args["fontSize"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int size))
            {
                textComponent.fontSize = size;
            }

            if (args.ContainsKey("alignment") && Enum.TryParse(args["alignment"].ToString(), true, out TextAnchor alignment))
            {
                textComponent.alignment = alignment;
            }
        }

        public static UnityEngine.GameObject CreateChild(string name, UnityEngine.Transform parent, params Type[] componentTypes)
        {
            var types = new Type[(componentTypes?.Length ?? 0) + 1];
            types[0] = typeof(RectTransform);
            for (int i = 0; i < componentTypes?.Length; i++)
            {
                types[i + 1] = componentTypes[i];
            }

            var child = new UnityEngine.GameObject(name, types);
            child.transform.SetParent(parent, false);
            return child;
        }

        public static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        public static Component AddTmpTextComponent(UnityEngine.GameObject obj, string text, Color color, float fontSize, string alignment = null)
        {
            var tmpType = GetTmpTextType();
            if (tmpType == null)
            {
                return null;
            }

            var tmpComponent = obj.AddComponent(tmpType);
            var props = new Dictionary<string, object>
            {
                ["text"] = text,
                ["color"] = $"{color.r.ToString(CultureInfo.InvariantCulture)},{color.g.ToString(CultureInfo.InvariantCulture)},{color.b.ToString(CultureInfo.InvariantCulture)},{color.a.ToString(CultureInfo.InvariantCulture)}",
                ["fontSize"] = fontSize.ToString(CultureInfo.InvariantCulture)
            };

            if (!string.IsNullOrEmpty(alignment))
            {
                props["alignment"] = alignment;
            }

            TrySetTmpTextProperties(tmpComponent, props);
            return tmpComponent;
        }

        public static Component GetTextLikeComponent(UnityEngine.GameObject obj)
        {
            if (obj == null)
            {
                return null;
            }

            var tmpType = GetTmpTextType();
            if (tmpType != null)
            {
                var tmpComponent = obj.GetComponent(tmpType);
                if (tmpComponent != null)
                {
                    return tmpComponent;
                }
            }

            return obj.GetComponent<Text>();
        }

        public static void SetTextLikeComponent(Component component, string text)
        {
            if (component == null)
            {
                return;
            }

            if (component is Text legacyText)
            {
                legacyText.text = text;
                return;
            }

            var textProp = component.GetType().GetProperty("text");
            textProp?.SetValue(component, text, null);
        }

        public static string GetTextLikeComponentValue(Component component)
        {
            if (component == null)
            {
                return string.Empty;
            }

            if (component is Text legacyText)
            {
                return legacyText.text;
            }

            var textProp = component.GetType().GetProperty("text");
            return textProp?.GetValue(component)?.ToString() ?? string.Empty;
        }

        public static bool HasObjectReference(Component component, string propertyName)
        {
            if (component == null)
            {
                return false;
            }

            var prop = component.GetType().GetProperty(propertyName);
            if (prop != null)
            {
                return prop.GetValue(component) != null;
            }

            var field = component.GetType().GetField(propertyName);
            return field != null && field.GetValue(component) != null;
        }

        public static int GetListCount(Component component, string propertyName)
        {
            if (component == null)
            {
                return 0;
            }

            var prop = component.GetType().GetProperty(propertyName);
            var list = prop?.GetValue(component) as System.Collections.IList;
            return list?.Count ?? 0;
        }

        public static void SetTmpDropdownOptions(Component tmpDropdown, System.Collections.IList options)
        {
            if (tmpDropdown == null || options == null)
            {
                return;
            }

            var dropdownType = tmpDropdown.GetType();
            var optionsProp = dropdownType.GetProperty("options");
            var optionsList = optionsProp?.GetValue(tmpDropdown);
            if (optionsList == null)
            {
                return;
            }

            var clearMethod = optionsList.GetType().GetMethod("Clear");
            var addMethod = optionsList.GetType().GetMethod("Add");
            var optionType = optionsList.GetType().GetGenericArguments()[0];
            clearMethod?.Invoke(optionsList, null);

            foreach (var option in options)
            {
                var optionData = Activator.CreateInstance(optionType);
                optionType.GetProperty("text")?.SetValue(optionData, option?.ToString() ?? string.Empty, null);
                addMethod?.Invoke(optionsList, new[] { optionData });
            }

            dropdownType.GetMethod("RefreshShownValue")?.Invoke(tmpDropdown, null);
        }

        public static List<string> GetDropdownOptions(Component dropdown)
        {
            var result = new List<string>();
            if (dropdown == null)
            {
                return result;
            }

            if (dropdown is Dropdown legacyDropdown)
            {
                foreach (var option in legacyDropdown.options)
                {
                    result.Add(option.text);
                }

                return result;
            }

            var optionsProp = dropdown.GetType().GetProperty("options");
            var optionsList = optionsProp?.GetValue(dropdown) as System.Collections.IEnumerable;
            if (optionsList == null)
            {
                return result;
            }

            foreach (var option in optionsList)
            {
                var textProp = option.GetType().GetProperty("text");
                result.Add(textProp?.GetValue(option)?.ToString() ?? string.Empty);
            }

            return result;
        }

        public static void CollectUiChildrenJson(UnityEngine.Transform parent, StringBuilder sb, int depth)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.GetComponent<RectTransform>() == null) continue;

                if (i > 0 || depth > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"name\":\"{ToolUtils.EscapeJsonString(child.name)}\",");
                sb.Append($"\"path\":\"{ToolUtils.EscapeJsonString(ToolUtils.GetGameObjectPath(child.gameObject))}\",");
                sb.Append($"\"active\":{(child.gameObject.activeSelf ? "true" : "false")},");
                sb.Append("\"children\":[");
                CollectUiChildrenJson(child, sb, depth + 1);
                sb.Append("]}");
            }
        }
    }
}
#endif
