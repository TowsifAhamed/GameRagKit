using FluentAssertions;
using GameRagKit.Providers;

namespace GameRagKit.Tests.Voice;

public sealed class PiperTtsClientTests
{
    [Fact]
    public async Task SynthesizeAsync_Returns_Wav_Bytes_From_Fake_Cli()
    {
        var fakeCliPath = FakeCli.WritePiperFake();
        var options = new PiperTtsOptions
        {
            VoiceModelPath = "unused-voice.onnx",
            ExecutablePath = fakeCliPath,
            Timeout = TimeSpan.FromSeconds(10)
        };
        var client = new PiperTtsClient(options);

        var wavBytes = await client.SynthesizeAsync("Hello traveler.", CancellationToken.None);

        wavBytes.Should().NotBeEmpty();
        // RIFF....WAVE header
        System.Text.Encoding.ASCII.GetString(wavBytes, 0, 4).Should().Be("RIFF");
        System.Text.Encoding.ASCII.GetString(wavBytes, 8, 4).Should().Be("WAVE");
    }

    [Fact]
    public async Task SynthesizeAsync_Throws_When_Cli_Exits_NonZero()
    {
        var fakeCliPath = FakeCli.WriteFailingCli("voice model not found");
        var options = new PiperTtsOptions
        {
            VoiceModelPath = "missing-voice.onnx",
            ExecutablePath = fakeCliPath,
            Timeout = TimeSpan.FromSeconds(10)
        };
        var client = new PiperTtsClient(options);

        var act = async () => await client.SynthesizeAsync("Hello.", CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("voice model not found");
    }

    [Fact]
    public async Task SynthesizeAsync_Throws_Timeout_When_Cli_Hangs()
    {
        var fakeCliPath = FakeCli.WriteHangingCli();
        var options = new PiperTtsOptions
        {
            VoiceModelPath = "unused-voice.onnx",
            ExecutablePath = fakeCliPath,
            Timeout = TimeSpan.FromMilliseconds(300)
        };
        var client = new PiperTtsClient(options);

        var act = async () => await client.SynthesizeAsync("Hello.", CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task SynthesizeAsync_Cleans_Up_Temp_Directory_After_Success()
    {
        var fakeCliPath = FakeCli.WritePiperFake();
        var options = new PiperTtsOptions
        {
            VoiceModelPath = "unused-voice.onnx",
            ExecutablePath = fakeCliPath
        };
        var client = new PiperTtsClient(options);
        var tempBefore = Directory.GetDirectories(Path.GetTempPath(), "gamerag-piper-*").Length;

        await client.SynthesizeAsync("Hello.", CancellationToken.None);

        var tempAfter = Directory.GetDirectories(Path.GetTempPath(), "gamerag-piper-*").Length;
        tempAfter.Should().Be(tempBefore);
    }
}
