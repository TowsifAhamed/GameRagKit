using Prometheus;

namespace GameRagKit.Http;

internal static class ApiMetrics
{
    private static readonly Counter AskRequests = Metrics.CreateCounter(
        "gameragkit_ask_requests_total",
        "Number of POST /ask calls received",
        "npc");

    private static readonly Counter AskStreamRequests = Metrics.CreateCounter(
        "gameragkit_ask_stream_requests_total",
        "Number of POST /ask/stream calls received",
        "npc");

    private static readonly Counter AskStreamTokens = Metrics.CreateCounter(
        "gameragkit_ask_stream_tokens_total",
        "Number of SSE chunks emitted for /ask/stream",
        "npc");

    private static readonly Counter HotIngestRequests = Metrics.CreateCounter(
        "gameragkit_ingest_requests_total",
        "Number of POST /ingest calls received",
        "npc");

    public static void ObserveAsk(string npc)
        => AskRequests.WithLabels(SafeNpc(npc)).Inc();

    public static void ObserveAskStream(string npc)
        => AskStreamRequests.WithLabels(SafeNpc(npc)).Inc();

    public static void ObserveAskStreamToken(string npc)
        => AskStreamTokens.WithLabels(SafeNpc(npc)).Inc();

    public static void ObserveIngest(string npc)
        => HotIngestRequests.WithLabels(SafeNpc(npc)).Inc();

    private static string SafeNpc(string npc)
        => string.IsNullOrWhiteSpace(npc) ? "unknown" : npc;
}
