using UnityEngine;
using TMPro;

namespace GameRagKit.Unity.Examples
{
    /// <summary>
    /// Example script showing how to use NpcDialogueManager in a Unity game.
    /// Attach this to a GameObject with UI elements for player input and NPC response display.
    /// </summary>
    public class ExampleNpcInteraction : MonoBehaviour
    {
        [Header("UI References")]
        public TMP_InputField playerInputField;
        public TextMeshProUGUI npcResponseText;
        public TextMeshProUGUI npcNameText;

        [Header("NPC Configuration")]
        public string npcId = "guard-north-gate";
        public string npcDisplayName = "Jake the Guard";

        [Header("Dialogue Settings")]
        [Range(0f, 1f)]
        public float defaultImportance = 0.3f;
        public bool useStreaming = true;
        public float typingSpeed = 0.05f; // Seconds per character

        private NpcDialogueManager dialogueManager;
        private bool isProcessing = false;

        void Start()
        {
            // Get or create the dialogue manager
            dialogueManager = FindObjectOfType<NpcDialogueManager>();
            if (dialogueManager == null)
            {
                GameObject managerObj = new GameObject("NpcDialogueManager");
                dialogueManager = managerObj.AddComponent<NpcDialogueManager>();
            }

            // Set NPC name in UI
            if (npcNameText != null)
            {
                npcNameText.text = npcDisplayName;
            }

            // Check server health on start
            dialogueManager.CheckHealth(healthy =>
            {
                if (!healthy)
                {
                    Debug.LogWarning("GameRagKit server is not responding. Make sure it's running!");
                    if (npcResponseText != null)
                    {
                        npcResponseText.text = "[Server Offline - Start GameRagKit server]";
                    }
                }
                else
                {
                    if (npcResponseText != null)
                    {
                        npcResponseText.text = "Speak thy business, traveler...";
                    }
                }
            });
        }

        void Update()
        {
            // Check for Enter key to submit question
            if (playerInputField != null && Input.GetKeyDown(KeyCode.Return) && !isProcessing)
            {
                string question = playerInputField.text.Trim();
                if (!string.IsNullOrEmpty(question))
                {
                    AskQuestion(question);
                    playerInputField.text = "";
                }
            }
        }

        /// <summary>
        /// Called by UI button to ask a question
        /// </summary>
        public void OnAskButtonClicked()
        {
            if (isProcessing) return;

            string question = playerInputField?.text.Trim();
            if (string.IsNullOrEmpty(question)) return;

            AskQuestion(question);
            playerInputField.text = "";
        }

        private void AskQuestion(string question)
        {
            isProcessing = true;

            if (npcResponseText != null)
            {
                npcResponseText.text = "...";
            }

            if (useStreaming)
            {
                AskWithStreaming(question);
            }
            else
            {
                AskWithoutStreaming(question);
            }
        }

        private void AskWithoutStreaming(string question)
        {
            dialogueManager.AskNpc(
                npcId: npcId,
                question: question,
                importance: defaultImportance,
                onSuccess: response =>
                {
                    if (npcResponseText != null)
                    {
                        // Show response immediately
                        npcResponseText.text = response.answer;
                    }
                    isProcessing = false;

                    // Log metadata
                    Debug.Log($"Response from {(response.fromCloud ? "cloud" : "local")} in {response.responseTimeMs}ms");
                },
                onError: error =>
                {
                    if (npcResponseText != null)
                    {
                        npcResponseText.text = $"[Error: {error}]";
                    }
                    isProcessing = false;
                }
            );
        }

        private void AskWithStreaming(string question)
        {
            string fullResponse = "";

            dialogueManager.AskNpcStreaming(
                npcId: npcId,
                question: question,
                importance: defaultImportance,
                onChunk: chunk =>
                {
                    fullResponse += chunk;
                    // Update UI with typewriter effect
                    if (npcResponseText != null)
                    {
                        npcResponseText.text = fullResponse;
                    }
                },
                onComplete: sources =>
                {
                    Debug.Log($"Streaming complete. Sources: {string.Join(", ", sources)}");
                    isProcessing = false;
                },
                onError: error =>
                {
                    if (npcResponseText != null)
                    {
                        npcResponseText.text = $"[Error: {error}]";
                    }
                    isProcessing = false;
                }
            );
        }

        /// <summary>
        /// Example: Trigger dialogue when player enters trigger zone
        /// </summary>
        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                // Auto-greet the player
                AskQuestion("Hello");
            }
        }
    }
}
