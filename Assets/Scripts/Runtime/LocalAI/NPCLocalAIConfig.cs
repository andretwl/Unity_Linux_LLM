namespace NPCSystem
{
    /// <summary>
    /// Central configuration constants for LocalAI connectivity.
    ///
    /// \u2500\u2500 Layout \u2500\u2500
    ///   LocalAI (upstream)                \u2192 port 8080  (direct, no proxy)
    ///   localai-proxy (observability)     \u2192 port 8090  (token/latency tracking)
    ///
    /// All gameplay dialogue goes directly to port <see cref="LocalAIDirectPort"/>.
    /// The proxy on port <see cref="LocalAIProxyPort"/> exists for CLI tools,
    /// codebase-watchdog, and ad-hoc debugging \u2014 it is NOT in the runtime path.
    /// </summary>
    public static class NPCLocalAIConfig
    {
        /// <summary>Upstream LocalAI server port (direct, no observability proxy).</summary>
        public const int LocalAIDirectPort = 8080;

        /// <summary>localai-proxy observability layer port (not in gameplay path).</summary>
        public const int LocalAIProxyPort = 8090;

        /// <summary>Default LocalAI host address.</summary>
        public const string DefaultHost = "localhost";
    }
}
