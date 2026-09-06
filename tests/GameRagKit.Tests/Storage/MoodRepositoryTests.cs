using FluentAssertions;
using GameRagKit.Mood;
using GameRagKit.Storage;

namespace GameRagKit.Tests.Storage;

public sealed class MoodRepositoryTests : IDisposable
{
    private readonly string _root;
    private readonly MoodRepository _repository;

    public MoodRepositoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "gamerag-moodtest-" + Guid.NewGuid().ToString("N"));
        _repository = new MoodRepository(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_Returns_Null_When_No_Mood_Saved_Yet()
    {
        var mood = await _repository.LoadAsync("guard-north-gate", CancellationToken.None);

        mood.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_Then_LoadAsync_Round_Trips_Mood()
    {
        var now = DateTimeOffset.UtcNow;
        var mood = new MoodState("wary", 0.6, now);

        await _repository.SaveAsync("guard-north-gate", mood, CancellationToken.None);
        var loaded = await _repository.LoadAsync("guard-north-gate", CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Value.Should().Be("wary");
        loaded.Intensity.Should().Be(0.6);
        loaded.UpdatedAt.Should().Be(now);
    }

    [Fact]
    public async Task SaveAsync_Overwrites_Previous_Mood()
    {
        await _repository.SaveAsync("guard-north-gate", new MoodState("wary", 0.6, DateTimeOffset.UtcNow), CancellationToken.None);
        await _repository.SaveAsync("guard-north-gate", new MoodState("hostile", 0.9, DateTimeOffset.UtcNow), CancellationToken.None);

        var loaded = await _repository.LoadAsync("guard-north-gate", CancellationToken.None);

        loaded!.Value.Should().Be("hostile");
    }

    [Fact]
    public async Task Mood_Is_Isolated_Per_Npc()
    {
        await _repository.SaveAsync("guard-north-gate", new MoodState("wary", 0.6, DateTimeOffset.UtcNow), CancellationToken.None);
        await _repository.SaveAsync("merchant", new MoodState("delighted", 0.8, DateTimeOffset.UtcNow), CancellationToken.None);

        var guardMood = await _repository.LoadAsync("guard-north-gate", CancellationToken.None);
        var merchantMood = await _repository.LoadAsync("merchant", CancellationToken.None);

        guardMood!.Value.Should().Be("wary");
        merchantMood!.Value.Should().Be("delighted");
    }
}
