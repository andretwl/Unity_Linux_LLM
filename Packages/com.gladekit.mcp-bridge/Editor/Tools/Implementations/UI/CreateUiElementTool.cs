using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using GladeAgenticAI.Core.Tools;

#if GLADE_UGUI
namespace GladeAgenticAI.Core.Tools.Implementations.UI
{
    public class CreateUiElementTool : ITool
    {
        public string Name => "create_ui_element";

        public string Execute(Dictionary<string, object> args)
        {
            string elementType = args.ContainsKey("elementType") ? args["elementType"].ToString() : "Panel";
            string name = args.ContainsKey("name") ? args["name"].ToString() : elementType;
            string parentPath = args.ContainsKey("parentPath") ? args["parentPath"].ToString() : "";

            UnityEngine.GameObject parentObj = null;
            if (!string.IsNullOrEmpty(parentPath))
            {
                parentObj = ToolUtils.FindGameObjectByPath(parentPath);
            }
            if (parentObj == null)
            {
                var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
                parentObj = canvas != null ? canvas.gameObject : null;
            }

            UnityEngine.GameObject obj;

            switch (elementType.ToLower())
            {
                case "panel":
                    obj = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    var panelImage = obj.GetComponent<Image>();
                    panelImage.color = args.ContainsKey("color") ? ToolUtils.ParseColor(args["color"].ToString()) : new Color(1f, 1f, 1f, 0.3921569f);
                    panelImage.type = Image.Type.Simple;
                    break;
                case "text":
                case "tmp":
                case "tmp_text":
                case "textmeshpro":
                case "textmeshprougui":
                    if (!EnsureTmpAvailable(out string tmpError))
                    {
                        return ToolUtils.CreateErrorResponse(tmpError);
                    }

                    obj = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
                    if (!UIHelpers.TryAddTmpText(obj, args))
                    {
                        return ToolUtils.CreateErrorResponse("Failed to create TextMeshPro component. TextMeshPro is required for all UI actions in this agent and is the only text rendering system supported. If installation was just started, please wait for Unity to finish and manually click 'Import TMP Essentials' when prompted.");
                    }
                    break;
                case "image":
                    obj = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    var image = obj.GetComponent<Image>();
                    image.color = args.ContainsKey("color") ? ToolUtils.ParseColor(args["color"].ToString()) : Color.white;
                    image.type = Image.Type.Simple;
                    break;
                case "button":
                    obj = DefaultControls.CreateButton(new DefaultControls.Resources());
                    obj.name = name;
                    ApplyButtonArgs(obj, args);
                    break;
                case "slider":
                    obj = DefaultControls.CreateSlider(new DefaultControls.Resources());
                    obj.name = name;
                    break;
                case "toggle":
                    obj = DefaultControls.CreateToggle(new DefaultControls.Resources());
                    obj.name = name;
                    break;
                case "dropdown":
                    obj = DefaultControls.CreateDropdown(new DefaultControls.Resources());
                    obj.name = name;
                    break;
                case "tmp_dropdown":
                    if (!EnsureTmpAvailable(out string tmpDropdownError))
                    {
                        return ToolUtils.CreateErrorResponse(tmpDropdownError);
                    }

                    obj = CreateTmpDropdown(name, args);
                    break;
                case "inputfield":
                    obj = DefaultControls.CreateInputField(new DefaultControls.Resources());
                    obj.name = name;
                    break;
                case "tmp_inputfield":
                    if (!EnsureTmpAvailable(out string tmpInputFieldError))
                    {
                        return ToolUtils.CreateErrorResponse(tmpInputFieldError);
                    }

                    obj = CreateTmpInputField(name, args);
                    break;
                case "scrollview":
                case "scrollrect":
                    obj = DefaultControls.CreateScrollView(new DefaultControls.Resources());
                    obj.name = name;
                    break;
                case "scrollbar":
                    obj = DefaultControls.CreateScrollbar(new DefaultControls.Resources());
                    obj.name = name;
                    break;
                case "rawimage":
                    obj = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                    var rawImage = obj.GetComponent<RawImage>();
                    if (args.ContainsKey("texturePath") && !string.IsNullOrEmpty(args["texturePath"].ToString()))
                    {
                        string texturePath = args["texturePath"].ToString();
                        if (!texturePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                            texturePath = "Assets/" + texturePath;
                        var texture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
                        if (texture != null) rawImage.texture = texture;
                    }
                    if (args.ContainsKey("color")) rawImage.color = ToolUtils.ParseColor(args["color"].ToString());
                    break;
                case "canvasgroup":
                    obj = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
                    var canvasGroup = obj.GetComponent<CanvasGroup>();
                    if (args.ContainsKey("alpha") && float.TryParse(args["alpha"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float alpha))
                        canvasGroup.alpha = alpha;
                    else
                        canvasGroup.alpha = 1f;
                    if (args.ContainsKey("interactable") && bool.TryParse(args["interactable"].ToString(), out bool interactable))
                        canvasGroup.interactable = interactable;
                    else
                        canvasGroup.interactable = true;
                    if (args.ContainsKey("blocksRaycasts") && bool.TryParse(args["blocksRaycasts"].ToString(), out bool blocks))
                        canvasGroup.blocksRaycasts = blocks;
                    else
                        canvasGroup.blocksRaycasts = true;
                    break;
                case "horizontallayoutgroup":
                    obj = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
                    var hLayout = obj.GetComponent<HorizontalLayoutGroup>();
                    if (args.ContainsKey("spacing") && float.TryParse(args["spacing"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float hSpacing))
                        hLayout.spacing = hSpacing;
                    if (args.ContainsKey("padding") && !string.IsNullOrEmpty(args["padding"].ToString()))
                    {
                        var paddingParts = args["padding"].ToString().Split(',');
                        if (paddingParts.Length >= 4)
                        {
                            hLayout.padding = new RectOffset(
                                int.Parse(paddingParts[0].Trim()),
                                int.Parse(paddingParts[1].Trim()),
                                int.Parse(paddingParts[2].Trim()),
                                int.Parse(paddingParts[3].Trim())
                            );
                        }
                    }
                    break;
                case "verticallayoutgroup":
                    obj = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
                    var vLayout = obj.GetComponent<VerticalLayoutGroup>();
                    if (args.ContainsKey("spacing") && float.TryParse(args["spacing"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float vSpacing))
                        vLayout.spacing = vSpacing;
                    if (args.ContainsKey("padding") && !string.IsNullOrEmpty(args["padding"].ToString()))
                    {
                        var paddingParts = args["padding"].ToString().Split(',');
                        if (paddingParts.Length >= 4)
                        {
                            vLayout.padding = new RectOffset(
                                int.Parse(paddingParts[0].Trim()),
                                int.Parse(paddingParts[1].Trim()),
                                int.Parse(paddingParts[2].Trim()),
                                int.Parse(paddingParts[3].Trim())
                            );
                        }
                    }
                    break;
                case "gridlayoutgroup":
                    obj = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(GridLayoutGroup));
                    var gridLayout = obj.GetComponent<GridLayoutGroup>();
                    if (args.ContainsKey("spacing") && !string.IsNullOrEmpty(args["spacing"].ToString()))
                    {
                        var spacingParts = args["spacing"].ToString().Split(',');
                        if (spacingParts.Length >= 2)
                        {
                            gridLayout.spacing = new Vector2(
                                float.Parse(spacingParts[0].Trim(), CultureInfo.InvariantCulture),
                                float.Parse(spacingParts[1].Trim(), CultureInfo.InvariantCulture)
                            );
                        }
                    }
                    if (args.ContainsKey("cellSize") && !string.IsNullOrEmpty(args["cellSize"].ToString()))
                    {
                        var cellParts = args["cellSize"].ToString().Split(',');
                        if (cellParts.Length >= 2)
                        {
                            gridLayout.cellSize = new Vector2(
                                float.Parse(cellParts[0].Trim(), CultureInfo.InvariantCulture),
                                float.Parse(cellParts[1].Trim(), CultureInfo.InvariantCulture)
                            );
                        }
                    }
                    if (args.ContainsKey("padding") && !string.IsNullOrEmpty(args["padding"].ToString()))
                    {
                        var paddingParts = args["padding"].ToString().Split(',');
                        if (paddingParts.Length >= 4)
                        {
                            gridLayout.padding = new RectOffset(
                                int.Parse(paddingParts[0].Trim()),
                                int.Parse(paddingParts[1].Trim()),
                                int.Parse(paddingParts[2].Trim()),
                                int.Parse(paddingParts[3].Trim())
                            );
                        }
                    }
                    break;
                case "mask":
                    obj = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
                    break;
                case "rectmask2d":
                    obj = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(RectMask2D));
                    break;
                default:
                    obj = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    var defaultImage = obj.GetComponent<Image>();
                    if (args.ContainsKey("color"))
                    {
                        defaultImage.color = ToolUtils.ParseColor(args["color"].ToString());
                    }
                    defaultImage.type = Image.Type.Simple;
                    break;
            }

            if (parentObj != null)
            {
                obj.transform.SetParent(parentObj.transform, false);
            }

            var rect = obj.GetComponent<RectTransform>();
            ApplyDefaultRect(elementType, rect);
            if (args.ContainsKey("size"))
            {
                rect.sizeDelta = ToolUtils.ParseVector2(args["size"].ToString());
            }
            if (args.ContainsKey("anchoredPosition"))
            {
                rect.anchoredPosition = ToolUtils.ParseVector2(args["anchoredPosition"].ToString());
            }

            Undo.RegisterCreatedObjectUndo(obj, $"Create UI Element: {name}");
            var extras = new Dictionary<string, object>
            {
                { "gameObjectPath", ToolUtils.GetGameObjectPath(obj) }
            };
            return ToolUtils.CreateSuccessResponse($"Created UI element '{name}'", extras);
        }

        static bool EnsureTmpAvailable(out string error)
        {
            var tmpCheck = UIHelpers.EnsureTMPForUIActions();
            error = tmpCheck.IsAvailable ? null : tmpCheck.Message;
            return tmpCheck.IsAvailable;
        }

        static void ApplyDefaultRect(string elementType, RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            switch (elementType.ToLower())
            {
                case "button":
                case "inputfield":
                case "tmp_inputfield":
                case "dropdown":
                case "tmp_dropdown":
                case "scrollbar":
                    rect.sizeDelta = new Vector2(160f, 30f);
                    break;
                case "toggle":
                    rect.sizeDelta = new Vector2(160f, 20f);
                    break;
                case "slider":
                    rect.sizeDelta = new Vector2(160f, 20f);
                    break;
                case "scrollview":
                case "scrollrect":
                    rect.sizeDelta = new Vector2(200f, 200f);
                    break;
                case "text":
                case "tmp":
                case "tmp_text":
                case "textmeshpro":
                case "textmeshprougui":
                    rect.sizeDelta = new Vector2(160f, 30f);
                    break;
            }
        }

        static void ApplyButtonArgs(UnityEngine.GameObject obj, Dictionary<string, object> args)
        {
            if (obj.TryGetComponent<Image>(out var image) && args.ContainsKey("color"))
            {
                image.color = ToolUtils.ParseColor(args["color"].ToString());
            }

            var textChild = obj.transform.Find("Text");
            if (textChild != null)
            {
                var text = textChild.GetComponent<Text>();
                if (text != null)
                {
                    text.text = args.ContainsKey("text") ? args["text"].ToString() : obj.name;
                    if (args.ContainsKey("fontSize") && int.TryParse(args["fontSize"].ToString(), out int fontSize))
                    {
                        text.fontSize = fontSize;
                    }

                    if (args.ContainsKey("color"))
                    {
                        text.color = Color.black;
                    }
                }
            }
        }

        static UnityEngine.GameObject CreateTmpInputField(string name, Dictionary<string, object> args)
        {
            var tmpInputFieldType = UIHelpers.GetTmpInputFieldType();
            var root = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var inputField = root.AddComponent(tmpInputFieldType);
            var image = root.GetComponent<Image>();
            image.type = Image.Type.Simple;
            image.color = args.ContainsKey("color") ? ToolUtils.ParseColor(args["color"].ToString()) : Color.white;

            var textArea = UIHelpers.CreateChild("Text Area", root.transform, typeof(RectMask2D));
            UIHelpers.Stretch(textArea.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(10f, 6f), new Vector2(-10f, -7f));

            var placeholder = UIHelpers.CreateChild("Placeholder", textArea.transform, typeof(CanvasRenderer));
            var placeholderRect = placeholder.GetComponent<RectTransform>();
            UIHelpers.Stretch(placeholderRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var placeholderText = UIHelpers.AddTmpTextComponent(placeholder, args.ContainsKey("placeholder") ? args["placeholder"].ToString() : "Enter text...", new Color(0.3235294f, 0.3235294f, 0.3235294f, 0.5f), 18f, "Left");

            var text = UIHelpers.CreateChild("Text", textArea.transform, typeof(CanvasRenderer));
            var textRect = text.GetComponent<RectTransform>();
            UIHelpers.Stretch(textRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var textComponent = UIHelpers.AddTmpTextComponent(text, string.Empty, new Color(0.1960784f, 0.1960784f, 0.1960784f, 1f), 18f, "Left");

            tmpInputFieldType.GetProperty("textViewport")?.SetValue(inputField, textArea.GetComponent<RectTransform>(), null);
            tmpInputFieldType.GetProperty("textComponent")?.SetValue(inputField, textComponent, null);
            tmpInputFieldType.GetProperty("placeholder")?.SetValue(inputField, placeholderText, null);

            if (args.ContainsKey("contentType"))
            {
                var contentTypeProp = tmpInputFieldType.GetProperty("contentType");
                var contentTypeEnum = Enum.Parse(contentTypeProp.PropertyType, args["contentType"].ToString(), true);
                contentTypeProp?.SetValue(inputField, contentTypeEnum, null);
            }

            if (args.ContainsKey("characterLimit") && int.TryParse(args["characterLimit"].ToString(), out int charLimit))
            {
                tmpInputFieldType.GetProperty("characterLimit")?.SetValue(inputField, charLimit, null);
            }

            if (args.ContainsKey("lineType"))
            {
                var lineTypeProp = tmpInputFieldType.GetProperty("lineType");
                var lineTypeEnum = Enum.Parse(lineTypeProp.PropertyType, args["lineType"].ToString(), true);
                lineTypeProp?.SetValue(inputField, lineTypeEnum, null);
            }

            return root;
        }

        static UnityEngine.GameObject CreateTmpDropdown(string name, Dictionary<string, object> args)
        {
            var tmpDropdownType = UIHelpers.GetTmpDropdownType();
            var root = new UnityEngine.GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var dropdown = root.AddComponent(tmpDropdownType);
            var image = root.GetComponent<Image>();
            image.type = Image.Type.Simple;
            image.color = args.ContainsKey("color") ? ToolUtils.ParseColor(args["color"].ToString()) : Color.white;

            var label = UIHelpers.CreateChild("Label", root.transform, typeof(CanvasRenderer));
            var labelRect = label.GetComponent<RectTransform>();
            UIHelpers.Stretch(labelRect, Vector2.zero, Vector2.one, new Vector2(10f, 6f), new Vector2(-25f, -7f));
            var captionText = UIHelpers.AddTmpTextComponent(label, string.Empty, new Color(0.1960784f, 0.1960784f, 0.1960784f, 1f), 18f, "Left");

            var arrow = UIHelpers.CreateChild("Arrow", root.transform, typeof(CanvasRenderer), typeof(Image));
            var arrowRect = arrow.GetComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1f, 0.5f);
            arrowRect.anchorMax = new Vector2(1f, 0.5f);
            arrowRect.pivot = new Vector2(0.5f, 0.5f);
            arrowRect.sizeDelta = new Vector2(20f, 20f);
            arrowRect.anchoredPosition = new Vector2(-15f, 0f);
            arrow.GetComponent<Image>().color = new Color(0.1960784f, 0.1960784f, 0.1960784f, 1f);

            var template = UIHelpers.CreateChild("Template", root.transform, typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            var templateRect = template.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, 2f);
            templateRect.sizeDelta = new Vector2(0f, 150f);
            template.GetComponent<Image>().color = new Color(0.9607843f, 0.9607843f, 0.9607843f, 1f);

            var scrollRect = template.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = UIHelpers.CreateChild("Viewport", template.transform, typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            UIHelpers.Stretch(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(0f, 0f), new Vector2(-18f, 0f));
            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.003921569f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = UIHelpers.CreateChild("Content", viewport.transform);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 28f);
            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = contentRect;

            var item = UIHelpers.CreateChild("Item", content.transform, typeof(CanvasRenderer), typeof(Toggle));
            var itemRect = item.GetComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0f, 0.5f);
            itemRect.anchorMax = new Vector2(1f, 0.5f);
            itemRect.pivot = new Vector2(0.5f, 0.5f);
            itemRect.sizeDelta = new Vector2(0f, 20f);

            var itemBackground = UIHelpers.CreateChild("Item Background", item.transform, typeof(CanvasRenderer), typeof(Image));
            UIHelpers.Stretch(itemBackground.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            itemBackground.GetComponent<Image>().color = new Color(0.9607843f, 0.9607843f, 0.9607843f, 1f);

            var itemCheckmark = UIHelpers.CreateChild("Item Checkmark", item.transform, typeof(CanvasRenderer), typeof(Image));
            var checkmarkRect = itemCheckmark.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0f, 0.5f);
            checkmarkRect.anchorMax = new Vector2(0f, 0.5f);
            checkmarkRect.pivot = new Vector2(0.5f, 0.5f);
            checkmarkRect.sizeDelta = new Vector2(20f, 20f);
            checkmarkRect.anchoredPosition = new Vector2(10f, 0f);
            itemCheckmark.GetComponent<Image>().color = new Color(0.1960784f, 0.5882353f, 0.9803922f, 1f);

            var itemLabel = UIHelpers.CreateChild("Item Label", item.transform, typeof(CanvasRenderer));
            UIHelpers.Stretch(itemLabel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(20f, 1f), new Vector2(-10f, -2f));
            var itemLabelText = UIHelpers.AddTmpTextComponent(itemLabel, "Option A", new Color(0.1960784f, 0.1960784f, 0.1960784f, 1f), 18f, "Left");

            var itemToggle = item.GetComponent<Toggle>();
            itemToggle.targetGraphic = itemBackground.GetComponent<Image>();
            itemToggle.graphic = itemCheckmark.GetComponent<Image>();
            itemToggle.isOn = true;

            var scrollbar = DefaultControls.CreateScrollbar(new DefaultControls.Resources());
            scrollbar.name = "Scrollbar";
            scrollbar.transform.SetParent(template.transform, false);
            var scrollbarRect = scrollbar.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 1f);
            scrollbarRect.sizeDelta = new Vector2(20f, 0f);
            scrollbarRect.anchoredPosition = Vector2.zero;
            scrollRect.verticalScrollbar = scrollbar.GetComponent<Scrollbar>();
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            tmpDropdownType.GetProperty("template")?.SetValue(dropdown, templateRect, null);
            tmpDropdownType.GetProperty("captionText")?.SetValue(dropdown, captionText, null);
            tmpDropdownType.GetProperty("itemText")?.SetValue(dropdown, itemLabelText, null);
            template.SetActive(false);

            if (args.ContainsKey("options") && args["options"] is System.Collections.IList options)
            {
                UIHelpers.SetTmpDropdownOptions(dropdown, options);
            }
            else
            {
                UIHelpers.SetTmpDropdownOptions(dropdown, new List<string> { "Option A", "Option B", "Option C" });
            }

            return root;
        }
    }
}
#endif
