using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using GladeAgenticAI.Core.Tools;

#if GLADE_UGUI
namespace GladeAgenticAI.Core.Tools.Implementations.UI
{
    public class RemoveUiEventTool : ITool
    {
        public string Name => "remove_ui_event";

        static bool ClearPersistentCalls(Component component, string propertyPath)
        {
            if (component == null)
            {
                return false;
            }

            var serializedObject = new SerializedObject(component);
            var eventProperty = serializedObject.FindProperty(propertyPath);
            var callsProperty = eventProperty?.FindPropertyRelative("m_PersistentCalls.m_Calls");
            if (callsProperty == null)
            {
                return false;
            }

            callsProperty.arraySize = 0;
            serializedObject.ApplyModifiedProperties();
            return true;
        }

        public string Execute(Dictionary<string, object> args)
        {
            string gameObjectPath = args.ContainsKey("gameObjectPath") ? args["gameObjectPath"].ToString() : "";
            string eventType = args.ContainsKey("eventType") ? args["eventType"].ToString() : "";
            bool removeAll = true;
            if (args.ContainsKey("removeAll"))
            {
                if (args["removeAll"] is bool ra) removeAll = ra;
                else if (bool.TryParse(args["removeAll"].ToString(), out bool rav)) removeAll = rav;
            }

            if (string.IsNullOrEmpty(gameObjectPath) || string.IsNullOrEmpty(eventType))
            {
                return ToolUtils.CreateErrorResponse("gameObjectPath and eventType are required");
            }

            UnityEngine.GameObject obj = ToolUtils.FindGameObjectByPath(gameObjectPath);
            if (obj == null)
            {
                return ToolUtils.CreateErrorResponse($"GameObject '{gameObjectPath}' not found");
            }

            bool success = false;
            string errorMsg = "";

            try
            {
                switch (eventType.ToLower())
                {
                    case "onclick":
                        if (obj.TryGetComponent<Button>(out var button))
                        {
                            if (removeAll)
                            {
                                success = ClearPersistentCalls(button, "m_OnClick");
                                if (!success) errorMsg = "Failed to clear Button.onClick";
                            }
                            else
                            {
                                string targetPath = args.ContainsKey("targetGameObjectPath") ? args["targetGameObjectPath"].ToString() : "";
                                string methodName = args.ContainsKey("methodName") ? args["methodName"].ToString() : "";
                                if (string.IsNullOrEmpty(targetPath) || string.IsNullOrEmpty(methodName))
                                {
                                    return ToolUtils.CreateErrorResponse("targetGameObjectPath and methodName are required when removeAll=false");
                                }
                                success = ClearPersistentCalls(button, "m_OnClick");
                                if (!success) errorMsg = "Failed to clear Button.onClick";
                            }
                        }
                        else
                        {
                            errorMsg = "GameObject does not have a Button component";
                        }
                        break;
                    case "onvaluechanged":
                        if (obj.TryGetComponent<Toggle>(out var toggle))
                        {
                            success = ClearPersistentCalls(toggle, "m_OnValueChanged");
                        }
                        else if (obj.TryGetComponent<Slider>(out var slider))
                        {
                            success = ClearPersistentCalls(slider, "m_OnValueChanged");
                        }
                        else if (obj.TryGetComponent<InputField>(out var inputValueField))
                        {
                            success = ClearPersistentCalls(inputValueField, "m_OnValueChanged");
                        }
                        else if (obj.GetComponent(UIHelpers.GetTmpInputFieldType()) is Component tmpInputValueField)
                        {
                            success = ClearPersistentCalls(tmpInputValueField, "m_OnValueChanged");
                        }
                        else
                        {
                            errorMsg = "GameObject does not have a Toggle, Slider, InputField, or TMP_InputField component";
                        }
                        break;
                    case "onendedit":
                        if (obj.TryGetComponent<InputField>(out var inputField))
                        {
                            success = ClearPersistentCalls(inputField, "m_OnEndEdit");
                        }
                        else if (obj.GetComponent(UIHelpers.GetTmpInputFieldType()) is Component tmpInputField)
                        {
                            success = ClearPersistentCalls(tmpInputField, "m_OnEndEdit");
                        }
                        else
                        {
                            errorMsg = "GameObject does not have an InputField or TMP_InputField component";
                        }
                        break;
                    case "onsubmit":
                        if (obj.TryGetComponent<InputField>(out var inputField2))
                        {
                            success = ClearPersistentCalls(inputField2, "m_OnSubmit");
                        }
                        else if (obj.GetComponent(UIHelpers.GetTmpInputFieldType()) is Component tmpInputField2)
                        {
                            success = ClearPersistentCalls(tmpInputField2, "m_OnSubmit");
                        }
                        else
                        {
                            errorMsg = "GameObject does not have an InputField or TMP_InputField component";
                        }
                        break;
                    case "onvaluechangedint":
                        if (obj.TryGetComponent<Dropdown>(out var dropdown))
                        {
                            success = ClearPersistentCalls(dropdown, "m_OnValueChanged");
                        }
                        else if (obj.GetComponent(UIHelpers.GetTmpDropdownType()) is Component tmpDropdown)
                        {
                            success = ClearPersistentCalls(tmpDropdown, "m_OnValueChanged");
                        }
                        else
                        {
                            errorMsg = "GameObject does not have a Dropdown or TMP_Dropdown component";
                        }
                        break;
                    default:
                        errorMsg = $"Unknown event type: {eventType}";
                        break;
                }
            }
            catch (System.Exception e)
            {
                errorMsg = e.Message;
            }

            if (success)
            {
                return ToolUtils.CreateSuccessResponse($"Removed {eventType} event handlers from '{gameObjectPath}'");
            }
            else
            {
                return ToolUtils.CreateErrorResponse(errorMsg);
            }
        }
    }
}
#endif
