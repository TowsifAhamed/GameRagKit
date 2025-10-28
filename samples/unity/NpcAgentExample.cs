using System.Threading.Tasks;
using GameRagKit;
using UnityEngine;

public sealed class NpcAgentExample : MonoBehaviour
{
    [SerializeField] private string configPath = "Assets/NPCs/guard-north-gate.yaml";

    private NpcAgent? _agent;

    private async void Start()
    {
        _agent = await GameRAGKit.Load(configPath);
        _agent.UseEnv();
        await _agent.EnsureIndexAsync();
    }

    public async Task<string> AskAsync(string playerLine)
    {
        if (_agent is null)
        {
            return "...";
        }

        var reply = await _agent.AskAsync(playerLine);
        return reply.Text;
    }
}
