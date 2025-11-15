using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace GameRagKit.Unity
{
    /// <summary>
    /// Unity component for integrating GameRagKit NPCs into your game.
    /// Attach this to a GameObject in your scene.
    /// </summary>
    public class NpcDialogueManager : MonoBehaviour
    {
        [Header("Server Configuration")]
        [Tooltip("URL of the GameRagKit server (e.g., http://localhost:5280)")]
        public string serverUrl = "http://localhost:5280";

        [Tooltip("API key for authentication (if SERVER_API_KEY is set)")]
        public string apiKey = "";

        [Header("Debug")]
        public bool logResponses = true;

        /// <summary>
        /// Ask a question to an NPC and receive a complete response.
        /// </summary>
        /// <param name="npcId">The NPC identifier (e.g., "guard-north-gate")</param>
        /// <param name="question">The player's question</param>
        /// <param name="importance">Importance level 0.0-1.0 (affects local vs cloud routing)</param>
        /// <param name="onSuccess">Callback with the NPC's response</param>
        /// <param name="onError">Callback with error message</param>
        public void AskNpc(
            string npcId,
            string question,
            float importance = 0.3f,
            Action<NpcResponse> onSuccess = null,
            Action<string> onError = null)
        {
            StartCoroutine(AskNpcCoroutine(npcId, question, importance, onSuccess, onError));
        }

        private IEnumerator AskNpcCoroutine(
            string npcId,
            string question,
            float importance,
            Action<NpcResponse> onSuccess,
            Action<string> onError)
        {
            // Prepare request body
            var requestData = new AskRequest
            {
                npc = npcId,
                question = question,
                importance = importance
            };

            string jsonBody = JsonUtility.ToJson(requestData);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            // Create web request
            string endpoint = $"{serverUrl}/ask";
            using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.SetRequestHeader("X-API-Key", apiKey);
                }

                // Send request
                yield return request.SendWebRequest();

                // Handle response
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        NpcResponse response = JsonUtility.FromJson<NpcResponse>(request.downloadHandler.text);

                        if (logResponses)
                        {
                            Debug.Log($"[GameRagKit] NPC '{npcId}' responded: {response.answer}");
                            Debug.Log($"[GameRagKit] Sources: {string.Join(", ", response.sources ?? new string[0])}");
                            Debug.Log($"[GameRagKit] From Cloud: {response.fromCloud}");
                        }

                        onSuccess?.Invoke(response);
                    }
                    catch (Exception e)
                    {
                        string error = $"Failed to parse response: {e.Message}";
                        Debug.LogError($"[GameRagKit] {error}");
                        onError?.Invoke(error);
                    }
                }
                else
                {
                    string error = $"Request failed: {request.error} (Code: {request.responseCode})";
                    Debug.LogError($"[GameRagKit] {error}");
                    onError?.Invoke(error);
                }
            }
        }

        /// <summary>
        /// Ask a question with streaming response (for typewriter effects).
        /// </summary>
        /// <param name="npcId">The NPC identifier</param>
        /// <param name="question">The player's question</param>
        /// <param name="importance">Importance level 0.0-1.0</param>
        /// <param name="onChunk">Callback for each text chunk</param>
        /// <param name="onComplete">Callback when streaming completes</param>
        /// <param name="onError">Callback with error message</param>
        public void AskNpcStreaming(
            string npcId,
            string question,
            float importance = 0.3f,
            Action<string> onChunk = null,
            Action<string[]> onComplete = null,
            Action<string> onError = null)
        {
            StartCoroutine(AskNpcStreamingCoroutine(npcId, question, importance, onChunk, onComplete, onError));
        }

        private IEnumerator AskNpcStreamingCoroutine(
            string npcId,
            string question,
            float importance,
            Action<string> onChunk,
            Action<string[]> onComplete,
            Action<string> onError)
        {
            var requestData = new AskRequest
            {
                npc = npcId,
                question = question,
                importance = importance
            };

            string jsonBody = JsonUtility.ToJson(requestData);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            string endpoint = $"{serverUrl}/ask/stream";
            using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.SetRequestHeader("X-API-Key", apiKey);
                }

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    List<string> sources = new List<string>();

                    // Parse SSE format (data: {...}\n\n)
                    string[] lines = responseText.Split('\n');
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("data:"))
                        {
                            string jsonData = line.Substring(5).Trim();
                            if (!string.IsNullOrEmpty(jsonData))
                            {
                                try
                                {
                                    StreamEvent evt = JsonUtility.FromJson<StreamEvent>(jsonData);

                                    if (evt.type == "chunk" && !string.IsNullOrEmpty(evt.text))
                                    {
                                        onChunk?.Invoke(evt.text);
                                    }
                                    else if (evt.type == "end")
                                    {
                                        sources = evt.sources != null ? new List<string>(evt.sources) : new List<string>();
                                    }
                                }
                                catch (Exception e)
                                {
                                    Debug.LogWarning($"[GameRagKit] Failed to parse SSE event: {e.Message}");
                                }
                            }
                        }
                    }

                    onComplete?.Invoke(sources.ToArray());
                }
                else
                {
                    string error = $"Streaming request failed: {request.error}";
                    Debug.LogError($"[GameRagKit] {error}");
                    onError?.Invoke(error);
                }
            }
        }

        /// <summary>
        /// Check if the GameRagKit server is healthy.
        /// </summary>
        public void CheckHealth(Action<bool> callback)
        {
            StartCoroutine(CheckHealthCoroutine(callback));
        }

        private IEnumerator CheckHealthCoroutine(Action<bool> callback)
        {
            string endpoint = $"{serverUrl}/health";
            using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
            {
                yield return request.SendWebRequest();

                bool healthy = request.result == UnityWebRequest.Result.Success;

                if (logResponses)
                {
                    Debug.Log($"[GameRagKit] Server health check: {(healthy ? "OK" : "FAILED")}");
                }

                callback?.Invoke(healthy);
            }
        }

        // Data structures matching the GameRagKit API
        [Serializable]
        private class AskRequest
        {
            public string npc;
            public string question;
            public float importance;
        }

        [Serializable]
        public class NpcResponse
        {
            public string answer;
            public string[] sources;
            public float[] scores;
            public bool fromCloud;
            public int responseTimeMs;
        }

        [Serializable]
        private class StreamEvent
        {
            public string type;
            public string text;
            public string[] sources;
            public bool fromCloud;
        }
    }
}
