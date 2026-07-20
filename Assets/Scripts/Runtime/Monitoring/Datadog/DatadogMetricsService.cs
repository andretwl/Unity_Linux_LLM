// <copyright file="DatadogMetricsService.cs" company="NPC System">
// Copyright (c) NPC System. All rights reserved.
// </copyright>

namespace NPCSystem.Monitoring.Datadog
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;

    /// <summary>
    /// Lightweight DogStatsD client for sending custom metrics from the Unity
    /// dedicated server to the Datadog Agent (via UDP port 8125).
    ///
    /// The Datadog Agent must be running on the same host (localhost:8125) and
    /// have <c>DD_DOGSTATSD_NON_LOCAL_TRAFFIC=true</c> set.
    ///
    /// Usage:
    /// <code>
    /// DatadogMetricsService.Initialize();
    /// DatadogMetricsService.Increment("dialogue.count", tags: new[]{"npc:butler"});
    /// DatadogMetricsService.Timer("llm.inference_time", elapsedMs, tags: new[]{"model:llama3.1-8b"});
    /// </code>
    /// </summary>
    public static class DatadogMetricsService
    {
        private const string DefaultHost = "127.0.0.1";
        private const int DefaultPort = 8125;
        private const int MaxQueueSize = 1000;
        private const int MaxPacketSize = 1400;

        private static UdpClient _udpClient;
        private static Thread _senderThread;
        private static CancellationTokenSource _cts;
        private static ConcurrentQueue<string> _metricQueue;
        private static bool _initialized;

        /// <summary>
        /// Initializes the DogStatsD client. Call once at server startup.
        /// </summary>
        public static void Initialize(string host = DefaultHost, int port = DefaultPort)
        {
            if (_initialized)
                return;

            try
            {
                _udpClient = new UdpClient(host, port);
                _udpClient.Client.SendTimeout = 100;
                _metricQueue = new ConcurrentQueue<string>();
                _cts = new CancellationTokenSource();

                _senderThread = new Thread(SenderLoop)
                {
                    Name = "DogStatsD Sender",
                    IsBackground = true,
                };
                _senderThread.Start();

                _initialized = true;

                NPCFlowLogger.FindOrCreate().Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    $"[DatadogMetrics] Initialized — sending to {host}:{port}",
                    source: nameof(DatadogMetricsService)
                );
            }
            catch (Exception ex)
            {
                NPCFlowLogger.FindOrCreate().Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"[DatadogMetrics] Failed to initialize: {ex.Message}",
                    source: nameof(DatadogMetricsService)
                );
            }
        }

        /// <summary>
        /// Shuts down the sender thread and releases the UDP socket.
        /// </summary>
        public static void Shutdown()
        {
            if (!_initialized)
                return;

            try
            {
                _cts?.Cancel();
                _senderThread?.Join(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Ignore shutdown-thread join errors
            }

            try
            {
                _udpClient?.Close();
            }
            catch
            {
                // Ignore socket close errors during shutdown
            }
            finally
            {
                _udpClient = null;
            }

            _initialized = false;

            // Logger may already be destroyed during scene teardown — skip safely
            try
            {
                NPCFlowLogger.FindOrCreate()
                    ?.Log(
                        NPCFlowStage.SceneBootstrap,
                        NPCFlowStatus.Success,
                        NPCFlowLogLevel.Info,
                        "[DatadogMetrics] Shutdown complete.",
                        source: nameof(DatadogMetricsService)
                    );
            }
            catch
            {
                // Silently ignore log failures during shutdown
            }
        }

        // ──────────────────────────────────────────────
        //  Metric primitives
        // ──────────────────────────────────────────────

        /// <summary>Increment a counter metric.</summary>
        public static void Increment(
            string metricName,
            double value = 1.0,
            double sampleRate = 1.0,
            string[] tags = null
        )
        {
            EnqueueMetric(FormatMetric(metricName, value, "c", sampleRate, tags));
        }

        /// <summary>Record a gauge (current value).</summary>
        public static void Gauge(
            string metricName,
            double value,
            double sampleRate = 1.0,
            string[] tags = null
        )
        {
            EnqueueMetric(FormatMetric(metricName, value, "g", sampleRate, tags));
        }

        /// <summary>Record a timing in milliseconds.</summary>
        public static void Timer(
            string metricName,
            double milliseconds,
            double sampleRate = 1.0,
            string[] tags = null
        )
        {
            EnqueueMetric(FormatMetric(metricName, milliseconds, "ms", sampleRate, tags));
        }

        /// <summary>Record a histogram value.</summary>
        public static void Histogram(
            string metricName,
            double value,
            double sampleRate = 1.0,
            string[] tags = null
        )
        {
            EnqueueMetric(FormatMetric(metricName, value, "h", sampleRate, tags));
        }

        // ──────────────────────────────────────────────
        //  Internal
        // ──────────────────────────────────────────────

        private static void EnqueueMetric(string payload)
        {
            if (!_initialized || payload == null)
                return;

            _metricQueue.Enqueue(payload);

            // Drop oldest if queue overflows to avoid OOM
            if (_metricQueue.Count > MaxQueueSize && _metricQueue.TryDequeue(out _))
            {
                NPCFlowLogger.FindOrCreate().Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    "[DatadogMetrics] Queue overflow — dropping oldest metric.",
                    source: nameof(DatadogMetricsService),
                    data: new Dictionary<string, object>
                    {
                        ["queueSize"] = MaxQueueSize,
                    }
                );
            }
        }

        private static string FormatMetric(
            string name,
            double value,
            string type,
            double sampleRate,
            string[] tags
        )
        {
            var sb = new StringBuilder();
            sb.Append(name);
            sb.Append(':');
            sb.Append(value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append('|');
            sb.Append(type);

            if (Math.Abs(sampleRate - 1.0) > 0.001)
            {
                sb.Append("|@");
                sb.Append(
                    sampleRate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                );
            }

            if (tags != null && tags.Length > 0)
            {
                sb.Append("|#");
                for (int i = 0; i < tags.Length; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append(tags[i]);
                }
            }

            return sb.ToString();
        }

        private static void SenderLoop()
        {
            var buffer = new List<string>(10);
            var sb = new StringBuilder();

            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    // Drain up to 10 metrics per tick
                    buffer.Clear();
                    for (int i = 0; i < 10 && _metricQueue.TryDequeue(out string m); i++)
                        buffer.Add(m);

                    if (buffer.Count == 0)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    // Build packet payload (split if too large)
                    sb.Clear();
                    for (int i = 0; i < buffer.Count; i++)
                    {
                        string separator = sb.Length > 0 ? "\n" : string.Empty;
                        string candidate = separator + buffer[i];

                        if (sb.Length + candidate.Length > MaxPacketSize && sb.Length > 0)
                        {
                            // Flush current buffer before adding
                            SendPacket(sb.ToString());
                            sb.Clear();
                            candidate = buffer[i];
                        }

                        sb.Append(candidate);
                    }

                    if (sb.Length > 0)
                        SendPacket(sb.ToString());
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    NPCFlowLogger.FindOrCreate().Log(
                        NPCFlowStage.SceneBootstrap,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        $"[DatadogMetrics] Sender error: {ex.Message}",
                        source: nameof(DatadogMetricsService)
                    );
                    Thread.Sleep(1000);
                }
            }
        }

        private static void SendPacket(string payload)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(payload);
                _udpClient?.Send(data, data.Length);
            }
            catch (ObjectDisposedException)
            {
                // Shutdown race — silently ignore
                NPCFlowLogger.FindOrCreate().Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Debug,
                    "[DatadogMetrics] Send skipped — ObjectDisposedException (shutdown race).",
                    source: nameof(DatadogMetricsService)
                );
            }
            catch (Exception ex)
            {
                NPCFlowLogger.FindOrCreate().Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"[DatadogMetrics] Send error: {ex.Message}",
                    source: nameof(DatadogMetricsService)
                );
            }
        }
    }
}
