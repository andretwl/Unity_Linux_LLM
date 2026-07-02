using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using GladeAgenticAI.Core.Tools;

#if GLADE_UGUI
namespace GladeAgenticAI.Core.Tools.Implementations.UI
{
    public class GetUiEventHandlersTool : ITool
    {
        public string Name => "get_ui_event_handlers";

        static void AddHandlers(List<Dictionary<string, object>> handlers, UnityEventBase unityEvent, string eventType)
        {
            if (unityEvent == null)
            {
                return;
            }

            int count = unityEvent.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                handlers.Add(new Dictionary<string, object>
                {
                    {"eventType", eventType},
                    {"targetGameObject", unityEvent.GetPersistentTarget(i)?.ToString() ?? ""},
                    {"methodName", unityEvent.GetPersistentMethodName(i) ?? ""}
                });
            }
        }

        public string Execute(Dictionary<string, object> args)
        {
            string gameObjectPath = args.ContainsKey("gameObjectPath") ? args["gameObjectPath"].ToString() : "";
            if (string.IsNullOrEmpty(gameObjectPath))
            {
                return ToolUtils.CreateErrorResponse("gameObjectPath is required");
            }

            UnityEngine.GameObject obj = ToolUtils.FindGameObjectByPath(gameObjectPath);
            if (obj == null)
            {
                return ToolUtils.CreateErrorResponse($"GameObject '{gameObjectPath}' not found");
            }

            var handlers = new List<Dictionary<string, object>>();

            if (obj.TryGetComponent<Button>(out var button))
            {
                AddHandlers(handlers, button.onClick, "onClick");
            }
            if (obj.TryGetComponent<Toggle>(out var toggle))
            {
                AddHandlers(handlers, toggle.onValueChanged, "onValueChanged");
            }
            if (obj.TryGetComponent<Slider>(out var slider))
            {
                AddHandlers(handlers, slider.onValueChanged, "onValueChanged");
            }
            if (obj.TryGetComponent<InputField>(out var inputField))
            {
                AddHandlers(handlers, inputField.onEndEdit, "onEndEdit");
                AddHandlers(handlers, inputField.onSubmit, "onSubmit");
                AddHandlers(handlers, inputField.onValueChanged, "onValueChanged");
            }
            if (obj.TryGetComponent<Dropdown>(out var dropdown))
            {
                AddHandlers(handlers, dropdown.onValueChanged, "onValueChangedInt");
            }

            var tmpInputFieldType = UIHelpers.GetTmpInputFieldType();
            if (tmpInputFieldType != null && obj.GetComponent(tmpInputFieldType) is Component tmpInputField)
            {
                AddHandlers(handlers, tmpInputFieldType.GetProperty("onEndEdit")?.GetValue(tmpInputField) as UnityEventBase, "onEndEdit");
                AddHandlers(handlers, tmpInputFieldType.GetProperty("onSubmit")?.GetValue(tmpInputField) as UnityEventBase, "onSubmit");
                AddHandlers(handlers, tmpInputFieldType.GetProperty("onValueChanged")?.GetValue(tmpInputField) as UnityEventBase, "onValueChanged");
            }

            var tmpDropdownType = UIHelpers.GetTmpDropdownType();
            if (tmpDropdownType != null && obj.GetComponent(tmpDropdownType) is Component tmpDropdown)
            {
                AddHandlers(handlers, tmpDropdownType.GetProperty("onValueChanged")?.GetValue(tmpDropdown) as UnityEventBase, "onValueChangedInt");
            }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"gameObjectPath\":\"");
            sb.Append(ToolUtils.EscapeJsonString(gameObjectPath));
            sb.Append("\",\"handlers\":[");
            for (int i = 0; i < handlers.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"eventType\":\"{ToolUtils.EscapeJsonString(handlers[i]["eventType"].ToString())}\",");
                sb.Append($"\"targetGameObject\":\"{ToolUtils.EscapeJsonString(handlers[i]["targetGameObject"].ToString())}\",");
                sb.Append($"\"methodName\":\"{ToolUtils.EscapeJsonString(handlers[i]["methodName"].ToString())}\"");
                sb.Append("}");
            }
            sb.Append($"],\"count\":{handlers.Count}}}");
            return sb.ToString();
        }
    }
}
#endif
