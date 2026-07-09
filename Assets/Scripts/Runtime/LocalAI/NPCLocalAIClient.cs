using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace NPCSystem
{
    [DefaultExecutionOrder(-1)]
    public class NPCLocalAIClient : MonoBehaviour
    {
        [Header("LocalAI Chat Endpoint")]
        public string host = "127.0.0.1";
        public int port = 8080;
        public string apiKey = "";
        public string model = "default-llm";

        [Header("Generation Parameters")]
        public float temperature = 0.2f;
        public float topP = 0.9f;
        public int topK = 40;
        public float minP = 0.05f;
        public float repeatPenalty = 1.1f;
        public int maxTokens = 256;
        public int numRetries = 3;
        public int requestTimeoutSeconds = 120;

        UnityWebRequest _activeRequest;
        bool _activeRequestCanceled;

        /// <summary>
        /// Cancels the currently active LocalAI request, if any.
        /// </summary>
        public void CancelActiveRequest()
        {
            if (_activeRequest != null && !_activeRequest.isDone)
            {
                Debug.Log("[NPCLocalAIClient] Aborting active LocalAI request.");
                _activeRequestCanceled = true;
                _activeRequest.Abort();
            }

            _activeRequest = null;
        }

        /// <summary>
        /// Send a chat completion request to LocalAI with message history.
        /// Returns the assistant response text. Strips <think> blocks if present.
        /// </summary>
        public async Task<string> ChatAsync(
            NPCOpenAIMessage[] messages,
            float? temperatureOverride = null
        )
        {
            string uri = $"http://{host}:{port}/v1/chat/completions";
            string modelName = string.IsNullOrWhiteSpace(model) ? "default-llm" : model.Trim();

            var payload = new NPCOpenAIChatRequest
            {
                model = modelName,
                messages = messages ?? Array.Empty<NPCOpenAIMessage>(),
                temperature = temperatureOverride ?? temperature,
                top_p = topP,
                top_k = topK,
                max_tokens = maxTokens,
            };

            for (int attempt = 0; attempt <= numRetries; attempt++)
            {
                try
                {
                    string json = JsonUtility.ToJson(payload);
                    string responseJson = await SendChatRequestAsync(uri, json);

                    if (string.IsNullOrEmpty(responseJson))
                    {
                        if (attempt < numRetries)
                        {
                            await Task.Delay(500 * (attempt + 1));
                            continue;
                        }
                        return string.Empty;
                    }

                    var response = JsonUtility.FromJson<NPCOpenAIChatResponse>(responseJson);

                    if (
                        response?.choices != null
                        && response.choices.Length > 0
                        && response.choices[0].message != null
                    )
                    {
                        string rawContent = response.choices[0].message.content ?? string.Empty;
                        rawContent = Regex
                            .Replace(rawContent, @"<think>.*?</think>", "", RegexOptions.Singleline)
                            .Trim();
                        return rawContent;
                    }

                    Debug.LogError($"[NPCLocalAIClient] Unexpected response format from {uri}");
                    return string.Empty;
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[NPCLocalAIClient] Exception (attempt {attempt + 1}/{numRetries + 1}): {ex.Message}"
                    );
                    if (attempt < numRetries)
                    {
                        await Task.Delay(500 * (attempt + 1));
                        continue;
                    }
                }
            }

            return string.Empty;
        }

        protected virtual async Task<string> SendChatRequestAsync(string uri, string json)
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest request = new UnityWebRequest(uri, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, requestTimeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(apiKey))
                    request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

                _activeRequest = request;
                _activeRequestCanceled = false;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                    if (_activeRequestCanceled)
                    {
                        Debug.LogWarning(
                            "[NPCLocalAIClient] LocalAI request was canceled during execution."
                        );
                        return null;
                    }
                }

                _activeRequest = null;

                if (
                    request.result == UnityWebRequest.Result.ConnectionError
                    || request.result == UnityWebRequest.Result.ProtocolError
                )
                {
                    Debug.LogError(
                        $"[NPCLocalAIClient] Request failed ({request.responseCode}): {request.error}"
                    );
                    return null;
                }

                if (request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    Debug.LogError(
                        $"[NPCLocalAIClient] Response processing failed: {request.error}"
                    );
                    return null;
                }

                return request.downloadHandler.text;
            }
        }
    }

    [Serializable]
    public class NPCOpenAIMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    class NPCOpenAIChatRequest
    {
        public string model;
        public NPCOpenAIMessage[] messages;
        public float temperature;
        public float top_p;
        public int top_k;
        public int max_tokens;
    }

    [Serializable]
    class NPCOpenAIChatChoice
    {
        public int index;
        public NPCOpenAIMessage message;
    }

    [Serializable]
    class NPCOpenAIChatResponse
    {
        public NPCOpenAIChatChoice[] choices;
    }
}
