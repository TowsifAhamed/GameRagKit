using System.Net;
using System.Text.Json;
using FluentAssertions;
using GameRagKit.Providers;

namespace GameRagKit.Tests.Voice;

public sealed class OpenAiSpeechClientTests
{
    [Fact]
    public async Task SynthesizeAsync_Posts_OpenAi_Speech_Request_And_Returns_Audio()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        var client = new OpenAiSpeechClient(
            new OpenAiSpeechOptions { Endpoint = "http://tts.local:8880/", Voice = "bm_george", Speed = 0.95, ApiKey = "test-key" },
            new HttpClient(handler));

        var audio = await client.SynthesizeAsync("Aye, the forge is busy.", CancellationToken.None);

        audio.Should().Equal((byte)'R', (byte)'I', (byte)'F', (byte)'F');
        handler.Request!.RequestUri!.ToString().Should().Be("http://tts.local:8880/v1/audio/speech");
        handler.Request.Headers.Authorization!.ToString().Should().Be("Bearer test-key");
        // Simple servers (scripts/kokoro_server.py included) need Content-Length, not chunked.
        handler.Request.Content!.Headers.ContentLength.Should().BeGreaterThan(0);
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("input").GetString().Should().Be("Aye, the forge is busy.");
        body.RootElement.GetProperty("voice").GetString().Should().Be("bm_george");
        body.RootElement.GetProperty("response_format").GetString().Should().Be("wav");
        body.RootElement.GetProperty("speed").GetDouble().Should().Be(0.95);
    }

    [Fact]
    public async Task SynthesizeAsync_Surfaces_Server_Errors()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "{\"error\":\"unknown voice 'nope'\"}"u8.ToArray());
        var client = new OpenAiSpeechClient(new OpenAiSpeechOptions { Voice = "nope" }, new HttpClient(handler));

        var act = async () => await client.SynthesizeAsync("Hello.", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("unknown voice");
    }

    [Theory]
    [InlineData("openai_speech", true)]
    [InlineData("Kokoro", true)]
    [InlineData("piper", false)]
    [InlineData(null, false)]
    public void IsEngine_Recognizes_Aliases(string? engine, bool expected)
    {
        OpenAiSpeechClient.IsEngine(engine).Should().Be(expected);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _response;

        public RecordingHandler(HttpStatusCode status, byte[] response)
        {
            _status = status;
            _response = response;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new ByteArrayContent(_response) };
        }
    }
}
