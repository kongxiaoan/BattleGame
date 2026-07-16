namespace BattleGame.Online;

public sealed class LastChanceDefinition
{
    public LastChanceDefinition(
        string prompt,
        string expectedText,
        int requiredCount,
        TimeSpan duration,
        int restoredHealth)
    {
        if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(expectedText))
        {
            throw new ArgumentException();
        }

        if (requiredCount is < 1 or > 100
            || duration < TimeSpan.FromSeconds(5)
            || duration > TimeSpan.FromMinutes(2)
            || restoredHealth is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException();
        }

        Prompt = prompt;
        ExpectedText = expectedText;
        RequiredCount = requiredCount;
        Duration = duration;
        RestoredHealth = restoredHealth;
    }

    public string Prompt { get; }
    public string ExpectedText { get; }
    public int RequiredCount { get; }
    public TimeSpan Duration { get; }
    public int RestoredHealth { get; }
}

public interface ILastChanceProvider
{
    LastChanceDefinition Create();
}
