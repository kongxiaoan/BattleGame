namespace BattleGame.Online;

public enum QuestionCategory
{
    Language = 1,
    Math = 2,
    English = 3,
    History = 4,
    Geography = 5,
    Biology = 6,
    CommonSense = 7,
    Logic = 8,
    BrainTeaser = 9
}

/// <summary>
/// 服务端完整题目，包含客户端永远不应提前收到的标准答案。
/// </summary>
public sealed class KnowledgeQuestion
{
    public KnowledgeQuestion(
        string questionId,
        QuestionCategory category,
        string prompt,
        IReadOnlyList<string> options,
        int correctOptionIndex,
        string explanation)
    {
        if (string.IsNullOrWhiteSpace(questionId)
            || string.IsNullOrWhiteSpace(prompt)
            || string.IsNullOrWhiteSpace(explanation))
        {
            throw new ArgumentException();
        }

        if (options == null || options.Count is < 2 or > 6
            || options.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(nameof(options));
        }

        if (correctOptionIndex < 0 || correctOptionIndex >= options.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(correctOptionIndex));
        }

        QuestionId = questionId;
        Category = category;
        Prompt = prompt;
        Options = options.ToArray();
        CorrectOptionIndex = correctOptionIndex;
        Explanation = explanation;
    }

    public string QuestionId { get; }
    public QuestionCategory Category { get; }
    public string Prompt { get; }
    public IReadOnlyList<string> Options { get; }
    public int CorrectOptionIndex { get; }
    public string Explanation { get; }

    public QuestionSnapshot CreatePublicSnapshot(DateTimeOffset deadlineUtc)
    {
        return new QuestionSnapshot(QuestionId, Category, Prompt, Options, deadlineUtc);
    }
}

/// <summary>
/// 公开题目刻意没有 CorrectOptionIndex 和 Explanation，避免客户端反序列化后直接作弊。
/// </summary>
public sealed record QuestionSnapshot(
    string QuestionId,
    QuestionCategory Category,
    string Prompt,
    IReadOnlyList<string> Options,
    DateTimeOffset DeadlineUtc);

public interface IQuestionProvider
{
    KnowledgeQuestion GetNext(int roundNumber);
}
