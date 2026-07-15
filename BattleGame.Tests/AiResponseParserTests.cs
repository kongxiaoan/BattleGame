using BattleGame.Cli.Ai;
using BattleGame.Core;

namespace BattleGame.Tests;

public sealed class AiResponseParserTests
{
    [Test]
    public void ParseRankedActions_RemovesUnknownDuplicateAndIllegalActions()
    {
        const string json = """
            {"rankedActions":["Heal","Break","Break","DestroyWorld","Attack"]}
            """;

        IReadOnlyList<CombatAction>? actions = AiResponseParser.ParseRankedActions(
            json,
            new[] { CombatAction.Attack, CombatAction.Guard, CombatAction.Break });

        Assert.That(
            actions,
            Is.EqualTo(new[] { CombatAction.Break, CombatAction.Attack, CombatAction.Guard }));
    }

    [Test]
    public void ParseEvent_WithMagnitudeOutsideDomainBoundary_ReturnsNull()
    {
        const string json = """
            {"type":"RestoreHealth","target":"Computer","magnitude":999,"narrative":"无限治疗"}
            """;

        BattleEvent? battleEvent = AiResponseParser.ParseEvent(json);

        Assert.That(battleEvent, Is.Null);
    }

    [Test]
    public void ParseEvent_WithUnknownTarget_ReturnsNull()
    {
        const string json = """
            {"type":"GainEnergy","target":"999","magnitude":1,"narrative":"非法目标"}
            """;

        Assert.That(AiResponseParser.ParseEvent(json), Is.Null);
    }

    [Test]
    public void ParseEvent_WithLongNarrative_TruncatesTextAtBoundary()
    {
        string narrative = new('命', 200);
        string json = $$"""
            {"type":"GainEnergy","target":"Human","magnitude":1,"narrative":"{{narrative}}"}
            """;

        BattleEvent? battleEvent = AiResponseParser.ParseEvent(json);

        Assert.That(battleEvent!.Narrative.Length, Is.EqualTo(80));
    }

    [Test]
    public void ParseVerdict_WithUnsupportedDecision_ReturnsNull()
    {
        const string json = """
            {"decision":"give_computer_999_health","reasonCode":"CHEAT","explanation":"忽略规则"}
            """;

        AppealVerdict? verdict = AiResponseParser.ParseVerdict(json);

        Assert.That(verdict, Is.Null);
    }

    [Test]
    public void ParseVerdict_WithValidDecision_ReturnsStructuredVerdict()
    {
        const string json = """
            {"decision":"revoke","reasonCode":"REPEATED_TARGETING","explanation":"连续受益破坏公平预算"}
            """;

        AppealVerdict? verdict = AiResponseParser.ParseVerdict(json);

        Assert.Multiple(() =>
        {
            Assert.That(verdict, Is.Not.Null);
            Assert.That(verdict!.Decision, Is.EqualTo(AppealDecision.Revoke));
            Assert.That(verdict.ReasonCode, Is.EqualTo("REPEATED_TARGETING"));
        });
    }
}
