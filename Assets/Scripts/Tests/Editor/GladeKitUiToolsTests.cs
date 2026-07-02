using System;
using System.Collections;
using System.Collections.Generic;
using GladeAgenticAI.Core.Tools;
using GladeAgenticAI.Core.Tools.Implementations.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NPCSystem.Tests
{
    public class GladeKitUiToolsTests
    {
        string _originalScenePath;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _originalScenePath = SceneManager.GetActiveScene().path;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (!string.IsNullOrEmpty(_originalScenePath))
            {
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
            }
        }

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void CreateEventSystemTool_UsesInputSystemUiInputModule()
        {
            var response = new CreateEventSystemTool().Execute(new Dictionary<string, object>());

            Assert.That(response, Does.Contain("\"success\":true"));

            var eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            Assert.That(eventSystem, Is.Not.Null);
            Assert.That(eventSystem.GetComponent<InputSystemUIInputModule>(), Is.Not.Null);
            Assert.That(eventSystem.GetComponent<StandaloneInputModule>(), Is.Null);
        }

        [Test]
        public void CreateUiElementTool_CreatesCompleteLegacyControls()
        {
            CreateCanvas();

            Execute(new CreateUiElementTool(), new Dictionary<string, object>
            {
                ["elementType"] = "Toggle",
                ["name"] = "RememberToggle",
                ["parentPath"] = "Canvas"
            });

            Execute(new CreateUiElementTool(), new Dictionary<string, object>
            {
                ["elementType"] = "Slider",
                ["name"] = "VolumeSlider",
                ["parentPath"] = "Canvas"
            });

            Execute(new CreateUiElementTool(), new Dictionary<string, object>
            {
                ["elementType"] = "Dropdown",
                ["name"] = "RoleDropdown",
                ["parentPath"] = "Canvas",
                ["options"] = new ArrayList { "Butler", "Maid", "Chef" }
            });

            Execute(new CreateUiElementTool(), new Dictionary<string, object>
            {
                ["elementType"] = "InputField",
                ["name"] = "LegacyInput",
                ["parentPath"] = "Canvas",
                ["placeholder"] = "Say something"
            });

            var toggle = GameObject.Find("RememberToggle").GetComponent<Toggle>();
            var slider = GameObject.Find("VolumeSlider").GetComponent<Slider>();
            var dropdown = GameObject.Find("RoleDropdown").GetComponent<Dropdown>();
            var inputField = GameObject.Find("LegacyInput").GetComponent<InputField>();

            Assert.That(toggle.graphic, Is.Not.Null);
            Assert.That(toggle.targetGraphic, Is.Not.Null);
            Assert.That(slider.fillRect, Is.Not.Null);
            Assert.That(slider.handleRect, Is.Not.Null);
            Assert.That(dropdown.template, Is.Not.Null);
            Assert.That(dropdown.captionText, Is.Not.Null);
            Assert.That(dropdown.itemText, Is.Not.Null);
            Assert.That(dropdown.options.Count, Is.EqualTo(3));
            Assert.That(inputField.textComponent, Is.Not.Null);
            Assert.That(inputField.placeholder, Is.Not.Null);
        }

        [Test]
        public void CreateUiElementTool_CreatesCompleteTmpControls()
        {
            CreateCanvas();

            Execute(new CreateUiElementTool(), new Dictionary<string, object>
            {
                ["elementType"] = "TMP_InputField",
                ["name"] = "TmpInput",
                ["parentPath"] = "Canvas",
                ["placeholder"] = "Enter NPC prompt"
            });

            Execute(new CreateUiElementTool(), new Dictionary<string, object>
            {
                ["elementType"] = "TMP_Dropdown",
                ["name"] = "TmpDropdown",
                ["parentPath"] = "Canvas",
                ["options"] = new ArrayList { "One", "Two" }
            });

            var tmpInputType = ResolveType("TMPro.TMP_InputField, Unity.TextMeshPro");
            var tmpDropdownType = ResolveType("TMPro.TMP_Dropdown, Unity.TextMeshPro");
            var tmpInput = GameObject.Find("TmpInput").GetComponent(tmpInputType);
            var tmpDropdown = GameObject.Find("TmpDropdown").GetComponent(tmpDropdownType);

            Assert.That(GetPropertyValue(tmpInput, "textViewport"), Is.Not.Null);
            Assert.That(GetPropertyValue(tmpInput, "textComponent"), Is.Not.Null);
            Assert.That(GetPropertyValue(tmpInput, "placeholder"), Is.Not.Null);
            Assert.That(GetPropertyValue(tmpDropdown, "template"), Is.Not.Null);
            Assert.That(GetPropertyValue(tmpDropdown, "captionText"), Is.Not.Null);
            Assert.That(GetPropertyValue(tmpDropdown, "itemText"), Is.Not.Null);
            Assert.That(((IList)GetPropertyValue(tmpDropdown, "options")).Count, Is.EqualTo(2));
        }

        [Test]
        public void SetUiPropertiesTool_UpdatesLegacyAndTmpWidgets()
        {
            CreateCanvas();

            Execute(new CreateUiElementTool(), new Dictionary<string, object>
            {
                ["elementType"] = "Dropdown",
                ["name"] = "RoleDropdown",
                ["parentPath"] = "Canvas"
            });

            Execute(new CreateUiElementTool(), new Dictionary<string, object>
            {
                ["elementType"] = "TMP_InputField",
                ["name"] = "TmpInput",
                ["parentPath"] = "Canvas"
            });

            Execute(new SetUiPropertiesTool(), new Dictionary<string, object>
            {
                ["gameObjectPath"] = "Canvas/RoleDropdown",
                ["options"] = new ArrayList { "Butler", "Maid", "Chef" },
                ["value"] = "1"
            });

            Execute(new SetUiPropertiesTool(), new Dictionary<string, object>
            {
                ["gameObjectPath"] = "Canvas/TmpInput",
                ["placeholder"] = "Ask about evidence",
                ["text"] = "Hello",
                ["lineType"] = "MultiLineNewline"
            });

            var dropdown = GameObject.Find("RoleDropdown").GetComponent<Dropdown>();
            var tmpInputType = ResolveType("TMPro.TMP_InputField, Unity.TextMeshPro");
            var tmpInput = GameObject.Find("TmpInput").GetComponent(tmpInputType);
            var placeholder = GetPropertyValue(tmpInput, "placeholder") as Component;

            Assert.That(dropdown.options.Count, Is.EqualTo(3));
            Assert.That(dropdown.value, Is.EqualTo(1));
            Assert.That(GetPropertyValue(tmpInput, "text")?.ToString(), Is.EqualTo("Hello"));
            Assert.That(GetPropertyValue(tmpInput, "lineType")?.ToString(), Is.EqualTo("MultiLineNewline"));
            Assert.That(ReadTextLike(placeholder), Is.EqualTo("Ask about evidence"));
        }

        [Test]
        public void EventTools_WireInspectAndRemoveHandlers_ForLegacyAndTmpControls()
        {
            CreateCanvas();

            Execute(new CreateUiElementTool(), new Dictionary<string, object>
            {
                ["elementType"] = "Button",
                ["name"] = "SubmitButton",
                ["parentPath"] = "Canvas",
                ["text"] = "Submit"
            });

            Execute(new CreateUiElementTool(), new Dictionary<string, object>
            {
                ["elementType"] = "TMP_Dropdown",
                ["name"] = "TmpDropdown",
                ["parentPath"] = "Canvas",
                ["options"] = new ArrayList { "One", "Two" }
            });

            var receiverObject = new GameObject("Receiver");
            receiverObject.AddComponent<UiEventReceiver>();

            Execute(new SetUiEventTool(), new Dictionary<string, object>
            {
                ["gameObjectPath"] = "Canvas/SubmitButton",
                ["eventType"] = "onClick",
                ["targetGameObjectPath"] = "Receiver",
                ["methodName"] = nameof(UiEventReceiver.HandleClick)
            });

            Execute(new SetUiEventTool(), new Dictionary<string, object>
            {
                ["gameObjectPath"] = "Canvas/TmpDropdown",
                ["eventType"] = "onValueChangedInt",
                ["targetGameObjectPath"] = "Receiver",
                ["methodName"] = nameof(UiEventReceiver.HandleIndexChanged)
            });

            var buttonHandlers = new GetUiEventHandlersTool().Execute(new Dictionary<string, object>
            {
                ["gameObjectPath"] = "Canvas/SubmitButton"
            });
            Assert.That(buttonHandlers, Does.Contain("\"count\":1"));
            Assert.That(buttonHandlers, Does.Contain(nameof(UiEventReceiver.HandleClick)));

            var dropdownHandlers = new GetUiEventHandlersTool().Execute(new Dictionary<string, object>
            {
                ["gameObjectPath"] = "Canvas/TmpDropdown"
            });
            Assert.That(dropdownHandlers, Does.Contain("\"count\":1"));
            Assert.That(dropdownHandlers, Does.Contain(nameof(UiEventReceiver.HandleIndexChanged)));

            Execute(new RemoveUiEventTool(), new Dictionary<string, object>
            {
                ["gameObjectPath"] = "Canvas/SubmitButton",
                ["eventType"] = "onClick",
                ["removeAll"] = true
            });

            var button = GameObject.Find("SubmitButton").GetComponent<Button>();
            Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(0));
        }

        [Test]
        public void InspectionTools_ReportHierarchyAndCompleteness()
        {
            CreateCanvas();

            Execute(new CreateUiElementTool(), new Dictionary<string, object>
            {
                ["elementType"] = "TMP_Dropdown",
                ["name"] = "TmpDropdown",
                ["parentPath"] = "Canvas",
                ["options"] = new ArrayList { "One", "Two" }
            });

            var hierarchy = new ListUiHierarchyTool().Execute(new Dictionary<string, object>());
            var exists = new CheckUiElementExistsTool().Execute(new Dictionary<string, object>
            {
                ["elementPath"] = "Canvas/TmpDropdown"
            });
            var info = new GetUiElementInfoTool().Execute(new Dictionary<string, object>
            {
                ["gameObjectPath"] = "Canvas/TmpDropdown"
            });

            Assert.That(hierarchy, Does.Contain("TMP_Dropdown"));
            Assert.That(exists, Does.Contain("\"success\":true"));
            Assert.That(exists, Does.Contain("\"exists\":true"));
            Assert.That(info, Does.Contain("\"hasTemplate\":true"));
            Assert.That(info, Does.Contain("\"hasCaptionText\":true"));
            Assert.That(info, Does.Contain("\"optionsCount\":2"));
        }

        static string Execute(ITool tool, Dictionary<string, object> args)
        {
            var response = tool.Execute(args);
            Assert.That(response, Does.Contain("\"success\":true"), response);
            return response;
        }

        static void CreateCanvas()
        {
            Execute(new CreateCanvasTool(), new Dictionary<string, object>
            {
                ["name"] = "Canvas",
                ["renderMode"] = "ScreenSpaceOverlay"
            });
        }

        static Type ResolveType(string assemblyQualifiedName)
        {
            var type = Type.GetType(assemblyQualifiedName);
            Assert.That(type, Is.Not.Null, assemblyQualifiedName);
            return type;
        }

        static object GetPropertyValue(Component component, string propertyName)
        {
            return component.GetType().GetProperty(propertyName)?.GetValue(component, null);
        }

        static string ReadTextLike(Component component)
        {
            if (component == null)
            {
                return string.Empty;
            }

            if (component is Text legacyText)
            {
                return legacyText.text;
            }

            return component.GetType().GetProperty("text")?.GetValue(component)?.ToString() ?? string.Empty;
        }

        public class UiEventReceiver : MonoBehaviour
        {
            public void HandleClick()
            {
            }

            public void HandleIndexChanged(int index)
            {
            }
        }
    }
}
