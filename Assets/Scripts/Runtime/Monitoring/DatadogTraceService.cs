// <copyright file="DatadogTraceService.cs" company="NPC System">
// Copyright (c) NPC System. All rights reserved.
// </copyright>

namespace NPCSystem
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using UnityEngine;

    /// <summary>
    /// Sends APM trace spans from Unity to the Datadog Trace Agent (port 8126).
    ///
    /// Because Unity uses IL2CPP/Mono (not the standard .NET CLR), the automatic
    /// Datadog .NET Tracer cannot hook into its runtime. This service provides
    /// manual instrumentation: create spans with <c>StartSpan()</c>, dispose them
    /// when done, and they are batched and POSTed to the Trace Agent as JSON.
    ///
    /// The Trace Agent (part of dd-agent) listens on port 8126 and forwards spans
    /// to Datadog.
    ///
    /// Automatically subscribes to NPCFlowScope.OnScopeComplete so every flow
    /// scope creates a corresponding Datadog span — no manual dual-instrumentation needed.
    ///
    /// Usage:
    /// <code>
    /// DatadogTracer.Initialize();
    ///
    /// using (var span = DatadogTracer.StartSpan("dialogue.turn",
    ///     resource: "/api/chat", tags: new[] {"npc:butler"}))
    /// {
    ///     // ... do work ...
    ///     span.SetTag("tokens", 42);
    /// }
    /// </code>
    /// </summary>
    public static class DatadogTracer
    {
        private const string DefaultAgentHost = "127.0.0.1";
        private const int DefaultTracePort = 8126;
        private const int FlushIntervalMs = 5000;
        private const int MaxBatchSize = 50;

        private static HttpClient _httpClient;
        private static Timer _flushTimer;
        private static readonly List<SpanData> _pendingSpans = new List<SpanData>();
        private static readonly object _lock = new object();
        private static long _nextSpanId = 1;
        private static long _nextTraceId;
        private static bool _initialized;
        private static string _traceEndpoint;

        /// <summary>
        /// Initializes the APM tracer. Call once at server startup
        /// (alongside <c>DatadogMetricsService.Initialize()</c>).
        /// Subscribes to NPCFlowScope.OnScopeComplete to auto-create spans
        /// from flow scopes, eliminating manual dual-instrumentation.
        /// </summary>
        public static void Initialize(
            string agentHost = DefaultAgentHost,
            int tracePort = DefaultTracePort
        )
        {
            if (_initialized)
                return;

            try
            {
                _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

                _traceEndpoint = $"http://{agentHost}:{tracePort}/v0.5/traces";

                // Seed trace ID from process start ticks for uniqueness
                _nextTraceId =
                    (Stopwatch.GetTimestamp() & 0x7FFFFFFFFFFFFFFF)
                    ^ (long)(DateTime.UtcNow.Ticks & 0x7FFFFFFFFFFFFFFF);

                _flushTimer = new Timer(
                    async _ =>
                    {
                        try
                        {
                            await FlushAsync();
                        }
                        catch (Exception ex)
                        {
                            NPCFlowLogger.FindOrCreate().Log(
                                NPCFlowStage.SceneBootstrap,
                                NPCFlowStatus.Warning,
                                NPCFlowLogLevel.Warning,
                                $"[DatadogTracer] Flush error: {ex.Message}",
                                source: nameof(DatadogTracer)
                            );
                        }
                    },
                    null,
                    FlushIntervalMs,
                    FlushIntervalMs
                );

                _initialized = true;

                // Subscribe to NPCFlowScope bridge — auto-creates Datadog spans
                // for every flow scope completion, eliminating dual-instrumentation.
                NPCFlowScope.OnScopeComplete += OnFlowScopeComplete;

                NPCFlowLogger.FindOrCreate().Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    $"[DatadogTracer] Initialized — sending to {agentHost}:{tracePort}",
                    source: nameof(DatadogTracer),
                    data: new Dictionary<string, object>
                    {
                        ["agentHost"] = agentHost,
                        ["tracePort"] = tracePort,
                    }
                );
            }
            catch (Exception ex)
            {
                NPCFlowLogger.FindOrCreate().Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"[DatadogTracer] Failed to initialize: {ex.Message}",
                    source: nameof(DatadogTracer)
                );
            }
        }

        /// <summary>
        /// Shuts down the tracer, flushes pending spans, and releases resources.
        /// </summary>
        public static void Shutdown()
        {
            if (!_initialized)
                return;

            NPCFlowScope.OnScopeComplete -= OnFlowScopeComplete;

            _flushTimer?.Dispose();
            FlushAsync().GetAwaiter().GetResult();
            _httpClient?.Dispose();
            _initialized = false;

            NPCFlowLogger.FindOrCreate().Log(
                NPCFlowStage.SceneBootstrap,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                "[DatadogTracer] Shutdown complete.",
                source: nameof(DatadogTracer)
            );
        }

        /// <summary>
        /// Starts a new APM span. Wrap in a <c>using</c> block to auto-finish.
        /// </summary>
        /// <param name="operationName">The span name (e.g. "dialogue.turn", "llm.request").</param>
        /// <param name="service">Service name (default: "unity-dedicated-server").</param>
        /// <param name="resource">Resource being operated on (e.g. "/api/chat", "npc:butler").</param>
        /// <param name="type">Span type: "custom", "web", "db", "cache", etc.</param>
        /// <param name="parent">Parent span for distributed tracing, or null for a root span.</param>
        /// <param name="tags">Optional tags as "key:value" strings.</param>
        /// <returns>A disposable <see cref="Span"/> that records its duration on dispose.</returns>
        public static Span StartSpan(
            string operationName,
            string service = "unity-dedicated-server",
            string resource = null,
            string type = "custom",
            Span parent = null,
            string[] tags = null
        )
        {
            long traceId;
            long parentId = 0;

            if (parent != null)
            {
                traceId = parent.TraceId;
                parentId = parent.SpanId;
            }
            else
            {
                traceId = Interlocked.Increment(ref _nextTraceId);
            }

            long spanId = Interlocked.Increment(ref _nextSpanId);
            long start = NanosSinceEpoch();

            return new Span(
                traceId,
                spanId,
                parentId,
                start,
                operationName,
                service,
                resource ?? operationName,
                type,
                tags
            );
        }

        // ──────────────────────────────────────────────
        //  Internal
        // ──────────────────────────────────────────────

        /// <summary>Enqueue a finished span for the next flush.</summary>
        internal static void EnqueueSpan(SpanData data)
        {
            if (!_initialized)
                return;

            lock (_lock)
            {
                _pendingSpans.Add(data);

                // Flush eagerly if batch is large enough
                if (_pendingSpans.Count >= MaxBatchSize)
                {
                    var snapshot = new List<SpanData>(_pendingSpans);
                    _pendingSpans.Clear();
                    ThreadPool.QueueUserWorkItem(_ => SendTraces(snapshot));
                }
            }
        }

        /// <summary>
        /// Bridge callback from NPCFlowScope — auto-creates a Datadog span
        /// for every flow scope completion. Replaces manual dual-instrumentation.
        /// </summary>
        static void OnFlowScopeComplete(
            NPCFlowStage stage,
            NPCFlowStatus status,
            string source,
            long durationMs,
            string npcSlug,
            string requestId,
            Dictionary<string, object> data
        )
        {
            if (!_initialized)
                return;

            string operationName = NPCFlowLogger.StageToCategory(stage).ToString().ToLowerInvariant()
                + "." + stage.ToString();

            using (var span = StartSpan(
                operationName,
                resource: string.IsNullOrWhiteSpace(source) ? stage.ToString() : source,
                tags: new[]
                {
                    $"npc_slug:{npcSlug ?? "none"}",
                    $"request_id:{requestId ?? "none"}",
                    $"status:{status}",
                    $"stage:{stage}",
                }
            ))
            {
                if (status == NPCFlowStatus.Error)
                {
                    span.SetError(data != null && data.TryGetValue("exceptionMessage", out var msg)
                        ? msg?.ToString()
                        : $"{stage} failed");
                }

                span.SetTag("duration_ms", durationMs.ToString("F0"));
            }
        }

        /// <summary>Flush all pending spans to the Trace Agent.</summary>
        private static async Task FlushAsync()
        {
            List<SpanData> snapshot;
            lock (_lock)
            {
                if (_pendingSpans.Count == 0)
                    return;
                snapshot = new List<SpanData>(_pendingSpans);
                _pendingSpans.Clear();
            }

            await SendTracesAsync(snapshot);
        }

        private static void SendTraces(List<SpanData> spans)
        {
            try
            {
                byte[] data = BuildMsgpackPayload(spans);

                using var request = new HttpRequestMessage(HttpMethod.Post, _traceEndpoint)
                {
                    Content = new ByteArrayContent(data),
                };
                request.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/msgpack");

                var response = _httpClient
                    .SendAsync(request)
                    .GetAwaiter()
                    .GetResult();
                _ = response.StatusCode; // swallow — we just care it was sent
            }
            catch (Exception ex)
            {
                NPCFlowLogger.FindOrCreate().Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    $"[DatadogTracer] Send error: {ex.Message}",
                    source: nameof(DatadogTracer)
                );
            }
        }

        private static async System.Threading.Tasks.Task SendTracesAsync(List<SpanData> spans)
        {
            try
            {
                byte[] data = BuildMsgpackPayload(spans);

                using var request = new HttpRequestMessage(HttpMethod.Post, _traceEndpoint)
                {
                    Content = new ByteArrayContent(data),
                };
                request.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/msgpack");

                var response = await _httpClient.SendAsync(request);
                _ = response.StatusCode;
            }
            catch (Exception ex)
            {
                NPCFlowLogger.FindOrCreate().Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    $"[DatadogTracer] SendTracesAsync error: {ex.Message}",
                    source: nameof(DatadogTracer)
                );
            }
        }

        /// <summary>
        /// Builds the Datadog Trace Agent v0.5 payload in MessagePack format.
        /// Format: [ [ span1, span2 ], [ span3 ] ] — an array of trace arrays.
        /// Datadog v0.5 requires binary msgpack encoding, not JSON.
        /// </summary>
        private static byte[] BuildMsgpackPayload(List<SpanData> spans)
        {
            // Group spans by trace_id
            var traces = new Dictionary<long, List<Dictionary<string, object>>>();

            foreach (var span in spans)
            {
                if (!traces.TryGetValue(span.TraceId, out var traceSpans))
                {
                    traceSpans = new List<Dictionary<string, object>>();
                    traces[span.TraceId] = traceSpans;
                }

                var spanDict = new Dictionary<string, object>
                {
                    ["trace_id"] = span.TraceId,
                    ["span_id"] = span.SpanId,
                    ["parent_id"] = span.ParentId,
                    ["start"] = span.StartNanos,
                    ["duration"] = span.DurationNanos,
                    ["service"] = span.Service,
                    ["name"] = span.OperationName,
                    ["resource"] = span.Resource,
                    ["type"] = span.Type,
                };

                // Add tags if present
                if (span.Tags != null && span.Tags.Count > 0)
                    spanDict["meta"] = span.Tags;

                // Empty metrics map (required by v0.5)
                spanDict["metrics"] = new Dictionary<string, double>();

                traceSpans.Add(spanDict);
            }

            // Build outer array of trace arrays, encode as msgpack
            var payload = new List<object>();
            foreach (var traceSpans in traces.Values)
            {
                payload.Add(traceSpans);
            }

            return MsgpackEncode(payload);
        }

        /// <summary>
        /// Lightweight MessagePack encoder for Datadog v0.5 trace format.
        /// Handles: integers (int64), strings, arrays, and maps.
        /// </summary>
        private static byte[] MsgpackEncode(object obj)
        {
            var buffer = new List<byte>();
            EncodeValue(buffer, obj);
            return buffer.ToArray();
        }

        private static void EncodeValue(List<byte> buffer, object obj)
        {
            if (obj == null)
            {
                buffer.Add(0xc0); // nil
            }
            else if (obj is long longVal)
            {
                EncodeLong(buffer, longVal);
            }
            else if (obj is int intVal)
            {
                EncodeLong(buffer, (long)intVal);
            }
            else if (obj is double doubleVal)
            {
                buffer.Add(0xcb); // float 64
                buffer.AddRange(BitConverter.GetBytes(doubleVal));
            }
            else if (obj is string str)
            {
                EncodeString(buffer, str);
            }
            else if (obj is Dictionary<string, object> dict)
            {
                EncodeMap(buffer, dict);
            }
            else if (obj is Dictionary<string, double> doubleDict)
            {
                var objDict = new Dictionary<string, object>();
                foreach (var kvp in doubleDict)
                    objDict[kvp.Key] = (object)kvp.Value;
                EncodeMap(buffer, objDict);
            }
            else if (obj is Dictionary<string, string> stringDict)
            {
                var objDict = new Dictionary<string, object>();
                foreach (var kvp in stringDict)
                    objDict[kvp.Key] = (object)kvp.Value;
                EncodeMap(buffer, objDict);
            }
            else if (obj is System.Collections.IList list)
            {
                EncodeArray(buffer, list);
            }
        }

        private static void EncodeLong(List<byte> buffer, long val)
        {
            // Datadog v0.5 uses varint64 (zigzag encoding for signed, or raw for unsigned)
            // For trace IDs and span IDs, use varint encoding
            if (val >= 0 && val < 128)
            {
                buffer.Add((byte)val); // positive fixint
            }
            else if (val >= -32 && val < 0)
            {
                buffer.Add((byte)(0xe0 | (val & 0x1f))); // negative fixint
            }
            else if (val >= 0 && val <= 0xffff)
            {
                buffer.Add(0xcd); // uint 16
                buffer.AddRange(BitConverter.GetBytes((ushort)val));
            }
            else if (val >= 0 && val <= 0xffffffff)
            {
                buffer.Add(0xce); // uint 32
                buffer.AddRange(BitConverter.GetBytes((uint)val));
            }
            else
            {
                buffer.Add(0xcf); // uint 64
                buffer.AddRange(BitConverter.GetBytes((ulong)val));
            }
        }

        private static void EncodeString(List<byte> buffer, string str)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(str);
            int len = utf8.Length;

            if (len < 32)
            {
                buffer.Add((byte)(0xa0 | len)); // fixstr
            }
            else if (len < 256)
            {
                buffer.Add(0xd9); // str 8
                buffer.Add((byte)len);
            }
            else if (len < 65536)
            {
                buffer.Add(0xda); // str 16
                buffer.AddRange(BitConverter.GetBytes((ushort)len));
            }
            else
            {
                buffer.Add(0xdb); // str 32
                buffer.AddRange(BitConverter.GetBytes((uint)len));
            }

            buffer.AddRange(utf8);
        }

        private static void EncodeArray(List<byte> buffer, System.Collections.IList list)
        {
            int len = list.Count;

            if (len < 16)
            {
                buffer.Add((byte)(0x90 | len)); // fixarray
            }
            else if (len < 65536)
            {
                buffer.Add(0xdc); // array 16
                buffer.AddRange(BitConverter.GetBytes((ushort)len));
            }
            else
            {
                buffer.Add(0xdd); // array 32
                buffer.AddRange(BitConverter.GetBytes((uint)len));
            }

            foreach (var item in list)
            {
                EncodeValue(buffer, item);
            }
        }

        private static void EncodeMap(List<byte> buffer, Dictionary<string, object> dict)
        {
            int len = dict.Count;

            if (len < 16)
            {
                buffer.Add((byte)(0x80 | len)); // fixmap
            }
            else if (len < 65536)
            {
                buffer.Add(0xde); // map 16
                buffer.AddRange(BitConverter.GetBytes((ushort)len));
            }
            else
            {
                buffer.Add(0xdf); // map 32
                buffer.AddRange(BitConverter.GetBytes((uint)len));
            }

            foreach (var kvp in dict)
            {
                EncodeString(buffer, kvp.Key);
                EncodeValue(buffer, kvp.Value);
            }
        }

        private static long NanosSinceEpoch()
        {
            return (DateTime.UtcNow.Ticks - 621355968000000000L) * 100;
        }
    }

    // ──────────────────────────────────────────────
    //  Span data record
    // ──────────────────────────────────────────────

    /// <summary>Immutable snapshot of a completed span, ready for serialization.</summary>
    internal readonly struct SpanData
    {
        public long TraceId { get; }
        public long SpanId { get; }
        public long ParentId { get; }
        public long StartNanos { get; }
        public long DurationNanos { get; }
        public string OperationName { get; }
        public string Service { get; }
        public string Resource { get; }
        public string Type { get; }
        public Dictionary<string, string> Tags { get; }

        public SpanData(
            long traceId,
            long spanId,
            long parentId,
            long startNanos,
            long durationNanos,
            string operationName,
            string service,
            string resource,
            string type,
            Dictionary<string, string> tags
        )
        {
            TraceId = traceId;
            SpanId = spanId;
            ParentId = parentId;
            StartNanos = startNanos;
            DurationNanos = durationNanos;
            OperationName = operationName;
            Service = service;
            Resource = resource;
            Type = type;
            Tags = tags;
        }
    }

    // ──────────────────────────────────────────────
    //  Active span (use with `using` block)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Represents an in-flight APM span. Record tags during execution, then
    /// dispose to record duration and enqueue for submission.
    ///
    /// <code>
    /// using (var span = DatadogTracer.StartSpan("my.operation"))
    /// {
    ///     span.SetTag("key", "value");
    ///     // ... work ...
    /// }
    /// </code>
    /// </summary>
    public sealed class Span : IDisposable
    {
        private readonly long _traceId;
        private readonly long _spanId;
        private readonly long _parentId;
        private readonly long _startNanos;
        private readonly string _operationName;
        private readonly string _service;
        private readonly string _resource;
        private readonly string _type;
        private readonly Dictionary<string, string> _tags;
        private volatile bool _finished;

        internal Span(
            long traceId,
            long spanId,
            long parentId,
            long startNanos,
            string operationName,
            string service,
            string resource,
            string type,
            string[] tags
        )
        {
            _traceId = traceId;
            _spanId = spanId;
            _parentId = parentId;
            _startNanos = startNanos;
            _operationName = operationName;
            _service = service;
            _resource = resource;
            _type = type;
            _tags = new Dictionary<string, string>();

            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    int colon = tag.IndexOf(':');
                    if (colon > 0)
                    {
                        _tags[tag.Substring(0, colon)] = tag.Substring(colon + 1);
                    }
                }
            }
        }

        /// <summary>This span's trace ID (for parent/child linking).</summary>
        public long TraceId => _traceId;

        /// <summary>This span's unique ID.</summary>
        public long SpanId => _spanId;

        /// <summary>Set a tag on this span (thread-safe before dispose).</summary>
        public void SetTag(string key, string value)
        {
            if (!_finished)
            {
                lock (_tags)
                {
                    _tags[key] = value;
                }
            }
        }

        /// <summary>Set an error tag + message to mark this span as failed.</summary>
        public void SetError(string errorMessage = null)
        {
            if (!_finished)
            {
                lock (_tags)
                {
                    _tags["error"] = "true";
                    if (errorMessage != null)
                        _tags["error.message"] = errorMessage;
                }
            }
        }

        /// <summary>Finishes the span and enqueues it for submission.</summary>
        public void Dispose()
        {
            if (_finished)
                return;
            _finished = true;

            long durationNanos = NanosSinceEpoch() - _startNanos;
            if (durationNanos < 1)
                durationNanos = 1;

            var data = new SpanData(
                _traceId,
                _spanId,
                _parentId,
                _startNanos,
                durationNanos,
                _operationName,
                _service,
                _resource,
                _type,
                _tags
            );

            DatadogTracer.EnqueueSpan(data);
        }

        private static long NanosSinceEpoch()
        {
            return (DateTime.UtcNow.Ticks - 621355968000000000L) * 100;
        }
    }
}
