using UnityEngine;
using TMPro;

namespace GameRagKit.Unity.Samples
{
    /// <summary>
    /// Minimal example wiring a TextMeshPro input field + response label to an NPC via
    /// NpcDialogueManager, using real incremental streaming for a typewriter effect.
    /// </summary>
    public class ExampleNpcInteraction : MonoBehaviour
    {
        [Header("UI References")]
        public TMP_InputField playerInputField;
        public TextMeshProUGUI npcResponseText;

        [Header("NPC Configuration")]
        public string npcId = "guard-north-gate";

        [Range(0f, 1f)]
        public float defaultImportance = 0.3f;

        private NpcDialogueManager _dialogueManager;
        private string _streamedText = string.Empty;
        private bool _isProcessing;

        private void Start()
        {
            _dialogueManager = FindObjectOfType<NpcDialogueManager>();
            if (_dialogueManager == null)
            {
                var managerObj = new GameObject("NpcDialogueManager");
                _dialogueManager = managerObj.AddComponent<NpcDialogueManager>();
            }

            _dialogueManager.CheckHealth(healthy =>
            {
                if (npcResponseText != null)
                {
                    npcResponseText.text = healthy
                        ? "Speak thy business, traveler..."
                        : "[Server offline - start GameRagKit server]";
                }
            });
        }

        private void Update()
        {
            if (playerInputField == null || _isProcessing || !Input.GetKeyDown(KeyCode.Return))
            {
                return;
            }

            var question = playerInputField.text.Trim();
            if (string.IsNullOrEmpty(question))
            {
                return;
            }

            AskQuestion(question);
            playerInputField.text = string.Empty;
        }

        private void AskQuestion(string question)
        {
            _isProcessing = true;
            _streamedText = string.Empty;

            if (npcResponseText != null)
            {
                npcResponseText.text = string.Empty;
            }

            _dialogueManager.AskNpcStreaming(
                npcId,
                question,
                defaultImportance,
                worldState: null,
                onChunk: chunk =>
                {
                    _streamedText += chunk;
                    if (npcResponseText != null)
                    {
                        npcResponseText.text = _streamedText;
                    }
                },
                onComplete: (sources, actions) =>
                {
                    foreach (var action in actions)
                    {
                        Debug.Log($"[GameRagKit] NPC requested action: {action.Name}");
                        // Game code executes the action here (give item, start quest, etc).
                    }

                    _isProcessing = false;
                },
                onError: error =>
                {
                    if (npcResponseText != null)
                    {
                        npcResponseText.text = $"[Error: {error}]";
                    }

                    _isProcessing = false;
                });
        }
    }
}
