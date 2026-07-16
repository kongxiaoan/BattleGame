using System.Text.Json;
using BattleGame.Core;

namespace BattleGame.Cli.Ai;

/// <summary>
/// 把不可信模型输出转换成领域对象；任何未知字段或越界数值都不能进入规则引擎。
/// </summary>
public static class AiResponseParser
{
    public static IReadOnlyList<CombatAction>? ParseRankedActions(
        string json,
        IReadOnlyList<CombatAction> legalActions)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement values = document.RootElement.GetProperty("rankedActions");
            var result = new List<CombatAction>();
            foreach (JsonElement item in values.EnumerateArray())
            {
                if (Enum.TryParse(item.GetString(), ignoreCase: true, out CombatAction action) &&
                    legalActions.Contains(action) &&
                    !result.Contains(action))
                {
                    result.Add(action);
                }
            }

            // 模型遗漏动作时补齐合法集合，保证难度策略始终有完整候选项。
            foreach (CombatAction action in legalActions)
            {
                if (!result.Contains(action))
                {
                    result.Add(action);
                }
            }

            return result.Count == 0 ? null : result;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    public static BattleEvent? ParseEvent(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!Enum.TryParse(root.GetProperty("type").GetString(), true, out BattleEventType type) ||
                !Enum.TryParse(root.GetProperty("target").GetString(), true, out BattleSide target) ||
                !Enum.IsDefined(type) ||
                !Enum.IsDefined(target))
            {
                return null;
            }

            int magnitude = root.GetProperty("magnitude").GetInt32();
            string narrative = root.GetProperty("narrative").GetString() ?? string.Empty;
            if (narrative.Length > 80)
            {
                narrative = narrative.Substring(0, 80);
            }
            return type switch
            {
                BattleEventType.RestoreHealth =>
                    BattleEvent.CreateHealthRestore(target, magnitude, narrative),
                BattleEventType.LoseHealth =>
                    BattleEvent.CreateHealthLoss(target, magnitude, narrative),
                BattleEventType.GainEnergy when magnitude == 1 =>
                    BattleEvent.CreateEnergyGain(target, narrative),
                _ => null
            };
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or
                KeyNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    public static AppealVerdict? ParseVerdict(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string? decisionValue = root.GetProperty("decision").GetString();
            AppealDecision decision = decisionValue?.ToLowerInvariant() switch
            {
                "uphold" => AppealDecision.Uphold,
                "revoke" => AppealDecision.Revoke,
                _ => throw new InvalidOperationException()
            };

            return new AppealVerdict(
                decision,
                root.GetProperty("reasonCode").GetString() ?? "UNSPECIFIED",
                root.GetProperty("explanation").GetString() ?? string.Empty);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }
}
