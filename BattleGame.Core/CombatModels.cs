using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleGame.Core
{
    public enum CombatAction
    {
        Attack = 1,
        Guard = 2,
        Break = 3,
        Heal = 4
    }

    public enum BattleSide
    {
        Human = 1,
        Computer = 2
    }

    public enum BattleOutcome
    {
        Ongoing = 0,
        HumanWin = 1,
        ComputerWin = 2,
        Draw = 3
    }

    public enum GameDifficulty
    {
        Easy = 1,
        Medium = 2,
        Hard = 3
    }

    public enum FateMode
    {
        Heroic = 1,
        Fair = 2,
        Harsh = 3
    }

    /// <summary>
    /// 战略模式参战者。状态修改只向当前程序集开放，避免界面或 AI 绕过规则引擎。
    /// </summary>
    public sealed class Combatant
    {
        public const int DefaultMaxEnergy = 3;

        public Combatant(
            string name,
            int maxHealth = 100,
            int currentHealth = 100,
            int energy = 0)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(nameof(name));
            }

            if (maxHealth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHealth));
            }

            if (currentHealth <= 0 || currentHealth > maxHealth)
            {
                throw new ArgumentOutOfRangeException(nameof(currentHealth));
            }

            if (energy < 0 || energy > DefaultMaxEnergy)
            {
                throw new ArgumentOutOfRangeException(nameof(energy));
            }

            Name = name.Trim();
            MaxHealth = maxHealth;
            Health = currentHealth;
            Energy = energy;
        }

        public string Name { get; }

        public int MaxHealth { get; }

        public int Health { get; private set; }

        public int Energy { get; private set; }

        public bool IsAlive => Health > 0;

        internal int Heal(int amount)
        {
            int actual = Math.Min(amount, MaxHealth - Health);
            Health += actual;
            return actual;
        }

        internal int TakeDamage(int amount)
        {
            int actual = Math.Min(amount, Health);
            Health -= actual;
            return actual;
        }

        internal int TakeEventDamage(int amount)
        {
            // 随机事件只负责改变局势，不能绕过战斗动作直接决定胜负。
            int actual = Math.Min(amount, Math.Max(Health - 1, 0));
            Health -= actual;
            return actual;
        }

        internal void GainEnergy(int amount)
        {
            Energy = Math.Min(Energy + amount, DefaultMaxEnergy);
        }

        internal void SpendEnergy(int amount)
        {
            if (Energy < amount)
            {
                throw new InvalidOperationException();
            }

            Energy -= amount;
        }
    }

    public sealed class RoundResult
    {
        internal RoundResult(
            CombatAction humanAction,
            CombatAction computerAction,
            int humanDamageTaken,
            int computerDamageTaken,
            int humanHealing,
            int computerHealing)
        {
            HumanAction = humanAction;
            ComputerAction = computerAction;
            HumanDamageTaken = humanDamageTaken;
            ComputerDamageTaken = computerDamageTaken;
            HumanHealing = humanHealing;
            ComputerHealing = computerHealing;
        }

        public CombatAction HumanAction { get; }
        public CombatAction ComputerAction { get; }
        public int HumanDamageTaken { get; }
        public int ComputerDamageTaken { get; }
        public int HumanHealing { get; }
        public int ComputerHealing { get; }
    }

    /// <summary>
    /// 难度由程序控制最优选择概率，避免仅靠提示词要求模型“变笨”。
    /// </summary>
    public static class DifficultyPolicy
    {
        public static CombatAction Select(
            IEnumerable<CombatAction> rankedActions,
            GameDifficulty difficulty,
            int roll,
            IEnumerable<CombatAction>? legalActions = null)
        {
            if (rankedActions == null)
            {
                throw new ArgumentNullException(nameof(rankedActions));
            }

            if (roll < 0 || roll > 99)
            {
                throw new ArgumentOutOfRangeException(nameof(roll));
            }

            HashSet<CombatAction>? legal = legalActions == null
                ? null
                : new HashSet<CombatAction>(legalActions);
            List<CombatAction> candidates = rankedActions
                .Where(action => legal == null || legal.Contains(action))
                .Distinct()
                .ToList();

            if (candidates.Count == 0)
            {
                throw new ArgumentException(null, nameof(rankedActions));
            }

            int optimalThreshold;
            switch (difficulty)
            {
                case GameDifficulty.Easy:
                    optimalThreshold = 40;
                    break;
                case GameDifficulty.Medium:
                    optimalThreshold = 70;
                    break;
                case GameDifficulty.Hard:
                    optimalThreshold = 90;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(difficulty));
            }

            return roll < optimalThreshold || candidates.Count == 1
                ? candidates[0]
                : candidates[1];
        }
    }
}
