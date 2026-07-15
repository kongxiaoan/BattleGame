using System;

namespace BattleGame.Core
{
    public enum BattleEventType
    {
        RestoreHealth = 1,
        LoseHealth = 2,
        GainEnergy = 3
    }

    /// <summary>
    /// 经过边界验证的事件提案。AI 只能从这些工厂方法创建合法事件。
    /// </summary>
    public sealed class BattleEvent
    {
        private BattleEvent(
            BattleEventType type,
            BattleSide target,
            int magnitude,
            string narrative)
        {
            if (!Enum.IsDefined(typeof(BattleEventType), type))
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }

            if (!Enum.IsDefined(typeof(BattleSide), target))
            {
                throw new ArgumentOutOfRangeException(nameof(target));
            }

            Type = type;
            Target = target;
            Magnitude = magnitude;
            if (string.IsNullOrWhiteSpace(narrative))
            {
                throw new ArgumentException(nameof(narrative));
            }

            Narrative = narrative.Trim();
        }

        public BattleEventType Type { get; }
        public BattleSide Target { get; }
        public int Magnitude { get; }
        public string Narrative { get; }

        // 正向事件使另一方处于不利，负向事件使目标处于不利。
        public BattleSide DisadvantagedSide => Type == BattleEventType.LoseHealth
            ? Target
            : Target == BattleSide.Human ? BattleSide.Computer : BattleSide.Human;

        public static BattleEvent CreateHealthRestore(
            BattleSide target,
            int magnitude,
            string narrative)
        {
            ValidateMagnitude(magnitude, 1, 20);
            return new BattleEvent(BattleEventType.RestoreHealth, target, magnitude, narrative);
        }

        public static BattleEvent CreateHealthLoss(
            BattleSide target,
            int magnitude,
            string narrative)
        {
            ValidateMagnitude(magnitude, 1, 15);
            return new BattleEvent(BattleEventType.LoseHealth, target, magnitude, narrative);
        }

        public static BattleEvent CreateEnergyGain(BattleSide target, string narrative)
        {
            return new BattleEvent(BattleEventType.GainEnergy, target, 1, narrative);
        }

        private static void ValidateMagnitude(int value, int minimum, int maximum)
        {
            if (value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
    }
}
