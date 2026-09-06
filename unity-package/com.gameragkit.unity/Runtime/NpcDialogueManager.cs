using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GameRagKit.Unity
{
    /// <summary>
    /// HTTP client for a GameRagKit server. Attach to a GameObject and call AskNpc /
    /// AskNpcStreaming. This package only talks to a GameRagKit server over HTTP -- it does
    /// not embed the GameRagKit RAG/LLM runtime itself, since that server-side stack
    /// (ASP.NET Core, LLamaSharp, Npgsql/Qdrant) is not viable to embed inside a Unity
    /// player build. Run `gamerag serve` separately and point this at it.
    /// </summary>
    public class NpcDialogueManager : MonoBehaviour
    {
        [Header("Server Configuration")]
        [Tooltip("URL of the GameRagKit server (e.g., http://localhost:5280)")]
        public string serverUrl = "http://localhost:5280";

        [Tooltip("API key for authentication (if the server requires one)")]
        public string apiKey = "";

        [Header("Debug")]
        public bool logResponses = true;

        private const string ProtocolVersion = "1";

        public void AskNpc(
            string npcId,
            string question,
            float importance = 0.3f,
            WorldState worldState = null,
            Action<NpcResponse> onSuccess = null,
            Action<string> onError = null)
        {
            StartCoroutine(AskNpcCoroutine(npcId, question, importance, worldState, onSuccess, onError));
        }

        private IEnumerator AskNpcCoroutine(
            string npcId,
            string question,
            float importance,
            WorldState worldState,
            Action<NpcResponse> onSuccess,
            Action<string> onError)
        {
            var jsonBody = BuildRequestJson(npcId, question, importance, worldState);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            var endpoint = $"{serverUrl}/ask";
            using var request = new UnityWebRequest(endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            ApplyCommonHeaders(request);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                NpcResponse response;
                try
                {
                    response = NpcResponseParser.ParseAskResponse(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    var parseError = $"Failed to parse response: {e.Message}";
                    Debug.LogError($"[GameRagKit] {parseError}");
                    onError?.Invoke(parseError);
                    yield break;
                }

                if (logResponses)
                {
                    Debug.Log($"[GameRagKit] NPC '{npcId}' responded: {response.Answer}");
                }

                onSuccess?.Invoke(response);
            }
            else
            {
                var error = $"Request failed: {request.error} (Code: {request.responseCode})";
                Debug.LogError($"[GameRagKit] {error}");
                onError?.Invoke(error);
            }
        }

        /// <summary>
        /// Streams a response chunk-by-chunk as the server sends it (true incremental
        /// streaming via a custom DownloadHandlerScript), suitable for a typewriter effect.
        /// </summary>
        public void AskNpcStreaming(
            string npcId,
            string question,
            float importance = 0.3f,
            WorldState worldState = null,
            Action<string> onChunk = null,
            Action<string[], ActionCall[]> onComplete = null,
            Action<string> onError = null)
        {
            StartCoroutine(AskNpcStreamingCoroutine(npcId, question, importance, worldState, onChunk, onComplete, onError));
        }

        private IEnumerator AskNpcStreamingCoroutine(
            string npcId,
            string question,
            float importance,
            WorldState worldState,
            Action<string> onChunk,
            Action<string[], ActionCall[]> onComplete,
            Action<string> onError)
        {
            var jsonBody = BuildRequestJson(npcId, question, importance, worldState);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            var endpoint = $"{serverUrl}/ask/stream";
            using var request = new UnityWebRequest(endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);

            var sources = new List<string>();
            var actions = new List<ActionCall>();
            var streamHandler = new SseDownloadHandler(line =>
            {
                if (!NpcResponseParser.TryParseStreamEvent(line, out var type, out var text, out var eventSources, out var eventActions))
                {
                    return;
                }

                switch (type)
                {
                    case "chunk" when !string.IsNullOrEmpty(text):
                        onChunk?.Invoke(text);
                        break;
                    case "end":
                        sources.AddRange(eventSources);
                        actions.AddRange(eventActions);
                        break;
                }
            });

            request.downloadHandler = streamHandler;
            ApplyCommonHeaders(request);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(sources.ToArray(), actions.ToArray());
            }
            else
            {
                var error = $"Streaming request failed: {request.error}";
                Debug.LogError($"[GameRagKit] {error}");
                onError?.Invoke(error);
            }
        }

        public void CheckHealth(Action<bool> callback)
        {
            StartCoroutine(CheckHealthCoroutine(callback));
        }

        private IEnumerator CheckHealthCoroutine(Action<bool> callback)
        {
            var endpoint = $"{serverUrl}/health";
            using var request = UnityWebRequest.Get(endpoint);
            yield return request.SendWebRequest();

            var healthy = request.result == UnityWebRequest.Result.Success;
            if (logResponses)
            {
                Debug.Log($"[GameRagKit] Server health check: {(healthy ? "OK" : "FAILED")}");
            }

            callback?.Invoke(healthy);
        }

        private void ApplyCommonHeaders(UnityWebRequest request)
        {
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-GameRAG-Protocol", ProtocolVersion);
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.SetRequestHeader("X-Api-Key", apiKey);
            }
        }

        private static string BuildRequestJson(string npcId, string question, float importance, WorldState worldState)
        {
            var request = new AskRequest
            {
                npc = npcId,
                question = question,
                options = new AskOptions
                {
                    importance = importance,
                    worldState = worldState
                }
            };

            return JsonUtility.ToJson(request);
        }

        [Serializable]
        private class AskRequest
        {
            public string npc;
            public string question;
            public AskOptions options;
        }

        [Serializable]
        private class AskOptions
        {
            public float importance;
            public WorldState worldState;
        }
    }
}
