namespace GameRagKit.Mood;

public sealed record MoodState(string Value, double Intensity, DateTimeOffset UpdatedAt)
{
    public static MoodState Neutral(DateTimeOffset now) => new("neutral", 0.0, now);
}
