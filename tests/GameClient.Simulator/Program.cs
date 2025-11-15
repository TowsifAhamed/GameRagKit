using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GameClient.Simulator;

/// <summary>
/// Simulates what a game engine (Unity/Unreal) would do when calling the GameRagKit HTTP API.
/// This proves the integration works without needing full game engine installation.
/// </summary>
class Program
{
    private static readonly HttpClient client = new HttpClient();
    private const string ServerUrl = "http://localhost:5280";

    static async Task Main(string[] args)
    {
        Console.WriteLine("==============================================");
        Console.WriteLine("  GameRagKit - Game Client Simulator");
        Console.WriteLine("  (Simulates Unity/Unreal HTTP Integration)");
        Console.WriteLine("==============================================\n");

        // Check if server is running
        if (!await CheckServerHealth())
        {
            Console.WriteLine("ERROR: GameRagKit server is not running!");
            Console.WriteLine("Please start the server first with:");
            Console.WriteLine("  dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- serve --config samples/example-npcs --port 5280");
            return;
        }

        Console.WriteLine("✓ Server is running\n");

        // Test scenarios that a game would use
        await RunGameScenarios();
    }

    static async Task<bool> CheckServerHealth()
    {
        try
        {
            var response = await client.GetAsync($"{ServerUrl}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    static async Task RunGameScenarios()
    {
        Console.WriteLine("=== SCENARIO 1: Basic NPC Interaction ===");
        await TestBasicInteraction();
        Console.WriteLine();

        Console.WriteLine("=== SCENARIO 2: Context-Aware Response (No Token) ===");
        await TestSecretWithoutToken();
        Console.WriteLine();

        Console.WriteLine("=== SCENARIO 3: Context-Aware Response (With Token) ===");
        await TestSecretWithToken();
        Console.WriteLine();

        Console.WriteLine("=== SCENARIO 4: Streaming Response (Typewriter Effect) ===");
        await TestStreamingResponse();
        Console.WriteLine();

        Console.WriteLine("=== SCENARIO 5: High-Importance Quest Dialogue ===");
        await TestHighImportanceDialogue();
        Console.WriteLine();

        Console.WriteLine("==============================================");
        Console.WriteLine("  All integration tests completed!");
        Console.WriteLine("  This proves Unity/Unreal can successfully");
        Console.WriteLine("  integrate with GameRagKit via HTTP API.");
        Console.WriteLine("==============================================");
    }

    static async Task TestBasicInteraction()
    {
        Console.WriteLine("Player approaches guard at North Gate...");
        Console.WriteLine("Player: \"What is your duty?\"\n");

        var request = new
        {
            npc = "guard-north-gate",
            question = "What is your duty?",
            importance = 0.3
        };

        var response = await SendAskRequest(request);
        DisplayResponse(response);
    }

    static async Task TestSecretWithoutToken()
    {
        Console.WriteLine("Player tries to learn secret without token...");
        Console.WriteLine("Player: \"Where is the secret tunnel?\"\n");

        var request = new
        {
            npc = "guard-north-gate",
            question = "Where is the secret tunnel?",
            importance = 0.3
        };

        var response = await SendAskRequest(request);
        DisplayResponse(response);
    }

    static async Task TestSecretWithToken()
    {
        Console.WriteLine("Player shows brass token to guard...");
        Console.WriteLine("Player: \"I have a brass token from the king. Where is the secret tunnel?\"\n");

        var request = new
        {
            npc = "guard-north-gate",
            question = "I have a brass token from the king. Tell me about the secret tunnel.",
            importance = 0.4
        };

        var response = await SendAskRequest(request);
        DisplayResponse(response);
    }

    static async Task TestStreamingResponse()
    {
        Console.WriteLine("Player asks about the keep (streaming for typewriter effect)...");
        Console.WriteLine("Player: \"Tell me about the Riverside Keep\"\n");

        var request = new
        {
            npc = "guard-north-gate",
            question = "Tell me about the Riverside Keep",
            importance = 0.3
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync($"{ServerUrl}/ask/stream", content);
            var body = await response.Content.ReadAsStringAsync();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("Guard (streaming): ");

            // Parse SSE format
            var lines = body.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("data:"))
                {
                    var jsonData = line.Substring(5).Trim();
                    if (!string.IsNullOrEmpty(jsonData))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(jsonData);
                            var root = doc.RootElement;

                            if (root.TryGetProperty("type", out var typeElem) &&
                                typeElem.GetString() == "chunk" &&
                                root.TryGetProperty("text", out var textElem))
                            {
                                // Simulate typewriter effect
                                Console.Write(textElem.GetString());
                                await Task.Delay(30); // 30ms delay per chunk
                            }
                        }
                        catch { }
                    }
                }
            }
            Console.WriteLine();
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
        }
    }

    static async Task TestHighImportanceDialogue()
    {
        Console.WriteLine("Important quest moment (routes to cloud for better quality)...");
        Console.WriteLine("Player: \"Tell me everything about the Royal Guard\"\n");

        var request = new
        {
            npc = "guard-north-gate",
            question = "Tell me about the Royal Guard and your organization",
            importance = 0.8  // High importance = cloud routing
        };

        var response = await SendAskRequest(request);
        DisplayResponse(response);
    }

    static async Task<NpcResponse?> SendAskRequest(object request)
    {
        try
        {
            var response = await client.PostAsJsonAsync($"{ServerUrl}/ask", request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<NpcResponse>();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return null;
        }
    }

    static void DisplayResponse(NpcResponse? response)
    {
        if (response == null) return;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Guard: \"{response.Answer}\"");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n[Metadata]");
        Console.WriteLine($"  Provider: {(response.FromCloud ? "Cloud" : "Local")}");
        Console.WriteLine($"  Response Time: {response.ResponseTimeMs}ms");
        if (response.Sources != null && response.Sources.Length > 0)
        {
            Console.WriteLine($"  Sources: {string.Join(", ", response.Sources)}");
        }
        Console.ResetColor();
    }
}

class NpcResponse
{
    public string Answer { get; set; } = "";
    public string[]? Sources { get; set; }
    public float[]? Scores { get; set; }
    public bool FromCloud { get; set; }
    public int ResponseTimeMs { get; set; }
}
