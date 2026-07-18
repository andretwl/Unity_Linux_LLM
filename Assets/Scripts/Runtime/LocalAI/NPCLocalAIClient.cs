using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;

namespace NPCSystem
{
    [DefaultExecutionOrder(-1)]
    public class NPCLocalAIClient : MonoBehaviour
    {
        [Header("LocalAI Chat Endpoint")]
        [FormerlySerializedAs("host")]
        [SerializeField]
        string _host = "127.0.0.1";
        public string Host { get => _host; set => _host = value; }

        [FormerlySerializedAs("port")]
        [SerializeField]
        int _port = NPCLocalAIConfig.LocalAIDirectPort;
        public int Port { get => _port; set => _port = value; }

        [FormerlySerializedAs("apiKey")]
        [SerializeField]
        string _apiKey = "";
        public string ApiKey => _apiKey;

        [FormerlySerializedAs("model")]
        [SerializeField]
        string _model = "llama-3.2-3b-instruct:q8_0";
        public string Model => _model;

        [Header("Generation Parameters")]
        [FormerlySerializedAs("temperature")]
        [SerializeField]
        float _temperature = 0.2f;
        public float Temperature => _temperature;

        [FormerlySerializedAs("topP")]
        [SerializeField]
        float _topP = 0.9f;
        public float TopP => _topP;

        [FormerlySerializedAs("topK")]
        [SerializeField]
        int _topK = 40;
        public int TopK => _topK;

        [FormerlySerializedAs("minP")]
        [SerializeField]
        float _minP = 0.05f;
        public float MinP => _minP;

        [FormerlySerializedAs("repeatPenalty")]
        [SerializeField]
        float _repeatPenalty = 1.1f;
        public float RepeatPenalty => _repeatPenalty;

        [FormerlySerializedAs("maxTokens")]
        [SerializeField]
        int _maxTokens = 256;
        public int MaxTokens => _maxTokens;

        [FormerlySerializedAs("numRetries")]
        [SerializeField]
        int _numRetries = 3;
        public int NumRetries { get => _numRetries; set => _numRetries = value; }

        [FormerlySerializedAs("requestTimeoutSeconds")]
        [SerializeField]
        int _requestTimeoutSeconds = 120;
        public int RequestTimeoutSeconds => _requestTimeoutSeconds;

        UnityWebRequest _activeRequest;
        bool _activeRequestCanceled;

        NPCFlowLogger _logger;

        void Awake()
        {
            _logger = NPCFlowLogger.FindOrCreate();
        }

        /// <summary>
        /// Cancels the currently active LocalAI request, if any.
        /// </summary>
        public void CancelActiveRequest()
        {
            if (_activeRequest != null && !_activeRequest.isDone)
            {
                _logger?.Log(
                    NPCFlowStage.LLMChat,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    "Aborting active LocalAI request.",
                    source: nameof(NPCLocalAIClient)
                );
                _activeRequestCanceled = true;
                _activeRequest.Abort();
            }

            _activeRequest = null;
        }

        /// <summary>
        /// Send a chat completion request to LocalAI with message history.
        /// Returns the assistant response text. Strips &lt;think&gt; blocks if present.
        /// </summary>
        public async Task<string> ChatAsync(
            NPCOpenAIMessage[] messages,
            float? temperatureOverride = null,
            string modelOverride = null,
            string requestId = null
        )
        {
            string uri = $"http://{_host}:{_port}/v1/chat/completions";
            string reqId = requestId ?? _logger?.NextRequestId() ?? "req-unknown";

            // Use modelOverride if provided; fall back to this.Model (for standalone use).
            // modelOverride is the primary path — callers like NPCDialogueSessionService pass
            // the model string directly, eliminating sync dependencies.
            string modelName =
                !string.IsNullOrWhiteSpace(modelOverride) ? modelOverride.Trim()
                : !string.IsNullOrWhiteSpace(_model) ? _model.Trim()
                : "";
            if (string.IsNullOrWhiteSpace(modelName))
            {
                _logger?.Log(
                    NPCFlowStage.LLMChat,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    "ChatAsync: no model specified. Pass modelOverride or set the Model field.",
                    source: nameof(NPCLocalAIClient),
                    requestId: reqId
                );
                return string.Empty;
            }

            _logger?.Log(
                NPCFlowStage.LLMChat,
                NPCFlowStatus.Start,
                NPCFlowLogLevel.Info,
                $"Sending chat request — model='{modelName}' messages={messages?.Length ?? 0}",
                source: nameof(NPCLocalAIClient),
                requestId: reqId,
                data: new Dictionary<string, object>
                {
                    ["modelName"] = modelName,
                    ["messageCount"] = messages?.Length ?? 0,
                    ["uri"] = uri,
                }
            );

            var payload = new NPCOpenAIChatRequest
            {
                model = modelName,
                messages = messages ?? Array.Empty<NPCOpenAIMessage>(),
                temperature = temperatureOverride ?? _temperature,
                top_p = _topP,
                top_k = _topK,
                max_tokens = _maxTokens,
            };

            for (int attempt = 0; attempt <= _numRetries; attempt++)
            {
                using var llmSpan = DatadogTracer.StartSpan(
                    "llm.chat",
                    service: "unity-dedicated-server",
                    resource: $"LocalAI/{modelName}",
                    type: "llm",
                    tags: new[] { $"model:{modelName}", $"attempt:{attempt}", $"request_id:{reqId}" }
                );

                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    string json = JsonUtility.ToJson(payload);
                    string responseJson = await SendChatRequestAsync(uri, json);

                    if (string.IsNullOrEmpty(responseJson))
                    {
                        if (attempt < _numRetries)
                        {
                            llmSpan.Dispose();
                            _logger?.Log(
                                NPCFlowStage.LLMChat,
                                NPCFlowStatus.Warning,
                                NPCFlowLogLevel.Warning,
                                $"Empty response (attempt {attempt + 1}/{_numRetries + 1}), retrying...",
                                source: nameof(NPCLocalAIClient),
                                requestId: reqId,
                                data: new Dictionary<string, object>
                                {
                                    ["attempt"] = attempt,
                                    ["maxAttempts"] = _numRetries,
                                }
                            );
                            await Task.Delay(500 * (attempt + 1));
                            continue;
                        }

                        // All retries exhausted
                        sw.Stop();
                        llmSpan.SetError("Empty response after all retries");
                        DatadogMetricsService.Increment(
                            "llm.request.error",
                            tags: new[] { $"model:{modelName}", $"reason:empty_response" }
                        );
                        _logger?.Log(
                            NPCFlowStage.LLMChat,
                            NPCFlowStatus.Error,
                            NPCFlowLogLevel.Error,
                            $"All retries exhausted — empty response from {uri}",
                            source: nameof(NPCLocalAIClient),
                            requestId: reqId,
                            durationMs: sw.ElapsedMilliseconds,
                            data: new Dictionary<string, object>
                            {
                                ["attempts"] = _numRetries + 1,
                            }
                        );
                        return string.Empty;
                    }

                    var response = JsonUtility.FromJson<NPCOpenAIChatResponse>(responseJson);

                    if (
                        response?.choices != null
                        && response.choices.Length > 0
                        && response.choices[0].message != null
                    )
                    {
                        sw.Stop();
                        llmSpan.SetTag("status", "success");
                        DatadogMetricsService.Timer(
                            "llm.request.duration",
                            sw.ElapsedMilliseconds,
                            tags: new[] { $"model:{modelName}", $"attempt:{attempt}" }
                        );
                        DatadogMetricsService.Increment(
                            "llm.request.count",
                            tags: new[] { $"model:{modelName}", $"status:success" }
                        );

                        string rawContent = response.choices[0].message.content ?? string.Empty;
                        rawContent = Regex
                            .Replace(rawContent, @"<think>.*?</think>", "", RegexOptions.Singleline)
                            .Trim();

                        _logger?.Log(
                            NPCFlowStage.LLMChat,
                            NPCFlowStatus.Success,
                            NPCFlowLogLevel.Info,
                            $"Chat response received ({rawContent.Length} chars, attempt {attempt}).",
                            source: nameof(NPCLocalAIClient),
                            requestId: reqId,
                            durationMs: sw.ElapsedMilliseconds,
                            npcSlug: null,
                            data: _logger?.SummarizeText("responsePreview", rawContent)
                        );

                        return rawContent;
                    }

                    sw.Stop();
                    llmSpan.SetTag("status", "unexpected_format");
                    DatadogMetricsService.Increment(
                        "llm.request.error",
                        tags: new[] { $"model:{modelName}", $"reason:unexpected_format" }
                    );
                    _logger?.Log(
                        NPCFlowStage.LLMChat,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        $"Unexpected response format from {uri}",
                        source: nameof(NPCLocalAIClient),
                        requestId: reqId
                    );
                    return string.Empty;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    llmSpan.SetError(ex.Message);
                    DatadogMetricsService.Increment(
                        "llm.request.error",
                        tags: new[]
                        {
                            $"model:{modelName}",
                            $"reason:exception",
                            $"attempt:{attempt}",
                        }
                    );

                    _logger?.Log(
                        NPCFlowStage.LLMChat,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        $"Exception on attempt {attempt + 1}/{_numRetries + 1}: {ex.Message}",
                        source: nameof(NPCLocalAIClient),
                        requestId: reqId,
                        durationMs: sw.ElapsedMilliseconds,
                        data: new Dictionary<string, object>
                        {
                            ["exceptionType"] = ex.GetType().Name,
                            ["attempt"] = attempt,
                        }
                    );

                    if (attempt < _numRetries)
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
                request.timeout = Mathf.Max(1, _requestTimeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(_apiKey))
                    request.SetRequestHeader("Authorization", $"Bearer {_apiKey}");

                _activeRequest = request;
                _activeRequestCanceled = false;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                    if (_activeRequestCanceled)
                    {
                        _logger?.Log(
                            NPCFlowStage.LLMChat,
                            NPCFlowStatus.Warning,
                            NPCFlowLogLevel.Warning,
                            "LocalAI request was canceled during execution.",
                            source: nameof(NPCLocalAIClient)
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
                    _logger?.Log(
                        NPCFlowStage.LLMChat,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        $"Request failed ({request.responseCode}): {request.error}",
                        source: nameof(NPCLocalAIClient),
                        data: new Dictionary<string, object>
                        {
                            ["responseCode"] = (int)request.responseCode,
                            ["uri"] = uri,
                        }
                    );
                    return null;
                }

                if (request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    _logger?.Log(
                        NPCFlowStage.LLMChat,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        $"Response processing failed: {request.error}",
                        source: nameof(NPCLocalAIClient)
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
