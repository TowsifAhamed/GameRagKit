using FluentAssertions;
using GameRagKit.Providers;

namespace GameRagKit.Tests.Voice;

public sealed class WhisperCppClientTests
{
    [Fact]
    public async Task TranscribeAsync_Returns_Trimmed_Transcript_From_Fake_Cli()
    {
        var fakeCliPath = FakeCli.WriteWhisperCliFake("Ah, you've proven yourself.  \n");
        var options = new WhisperCppOptions
        {
            ModelPath = "unused-model.bin",
            ExecutablePath = fakeCliPath,
            Timeout = TimeSpan.FromSeconds(10)
        };
        var client = new WhisperCppClient(options);

        var transcript = await client.TranscribeAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);

        transcript.Should().Be("Ah, you've proven yourself.");
    }

    [Fact]
    public async Task TranscribeAsync_Throws_When_Cli_Exits_NonZero()
    {
        var fakeCliPath = FakeCli.WriteFailingCli("model file not found");
        var options = new WhisperCppOptions
        {
            ModelPath = "missing-model.bin",
            ExecutablePath = fakeCliPath,
            Timeout = TimeSpan.FromSeconds(10)
        };
        var client = new WhisperCppClient(options);

        var act = async () => await client.TranscribeAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("model file not found");
    }

    [Fact]
    public async Task TranscribeAsync_Throws_Timeout_When_Cli_Hangs()
    {
        var fakeCliPath = FakeCli.WriteHangingCli();
        var options = new WhisperCppOptions
        {
            ModelPath = "unused-model.bin",
            ExecutablePath = fakeCliPath,
            Timeout = TimeSpan.FromMilliseconds(300)
        };
        var client = new WhisperCppClient(options);

        var act = async () => await client.TranscribeAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task TranscribeAsync_Cleans_Up_Temp_Directory_After_Success()
    {
        var fakeCliPath = FakeCli.WriteWhisperCliFake("Hello.");
        var options = new WhisperCppOptions
        {
            ModelPath = "unused-model.bin",
            ExecutablePath = fakeCliPath
        };
        var client = new WhisperCppClient(options);
        var tempBefore = Directory.GetDirectories(Path.GetTempPath(), "gamerag-whisper-*").Length;

        await client.TranscribeAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);

        var tempAfter = Directory.GetDirectories(Path.GetTempPath(), "gamerag-whisper-*").Length;
        tempAfter.Should().Be(tempBefore);
    }
}
