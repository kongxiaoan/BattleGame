using System;
using System.Collections.Generic;

namespace BattleGame.Core
{
    /// <summary>
    /// 同时结算双方动作的战略战斗。AI 只能提交 CombatAction，所有数值由此类型决定。
    /// </summary>
    public sealed class StrategicBattle
    {
        public const int HealEnergyCost = 2;
        public const int HealAmount = 25;

        private bool _humanAppealAvailable = true;
        private bool _computerAppealAvailable = true;

        public StrategicBattle(Combatant human, Combatant computer)
        {
            Human = human ?? throw new ArgumentNullException(nameof(human));
            Computer = computer ?? throw new ArgumentNullException(nameof(computer));
            if (ReferenceEquals(human, computer))
            {
                throw new ArgumentException(null, nameof(computer));
            }
        }

        public Combatant Human { get; }

        public Combatant Computer { get; }

        public int RoundNumber { get; private set; }

        public BattleOutcome Outcome { get; private set; }

        public bool IsFinished => Outcome != BattleOutcome.Ongoing;

        public IReadOnlyList<CombatAction> GetLegalActions(BattleSide side)
        {
            Combatant combatant = GetCombatant(side);
            var actions = new List<CombatAction>
            {
                CombatAction.Attack,
                CombatAction.Guard,
                CombatAction.Break
            };
            if (combatant.Energy >= HealEnergyCost)
            {
                actions.Add(CombatAction.Heal);
            }

            return actions;
        }

        public RoundResult ResolveRound(CombatAction humanAction, CombatAction computerAction)
        {
            if (IsFinished)
            {
                throw new InvalidOperationException();
            }

            ValidateAction(Human, humanAction);
            ValidateAction(Computer, computerAction);

            int humanHealing = PrepareAction(Human, humanAction);
            int computerHealing = PrepareAction(Computer, computerAction);

            // 先计算双方伤害再统一应用，确保一方在本轮死亡也能完成已经锁定的动作。
            int damageToComputer = CalculateDamage(humanAction, computerAction);
            int damageToHuman = CalculateDamage(computerAction, humanAction);
            int humanDamageTaken = Human.TakeDamage(damageToHuman);
            int computerDamageTaken = Computer.TakeDamage(damageToComputer);

            if (humanAction == CombatAction.Guard && computerAction == CombatAction.Attack)
            {
                Human.GainEnergy(1);
            }

            if (computerAction == CombatAction.Guard && humanAction == CombatAction.Attack)
            {
                Computer.GainEnergy(1);
            }

            RoundNumber++;
            UpdateOutcome();
            return new RoundResult(
                humanAction,
                computerAction,
                humanDamageTaken,
                computerDamageTaken,
                humanHealing,
                computerHealing);
        }

        public void ApplyEvent(BattleEvent battleEvent)
        {
            if (battleEvent == null)
            {
                throw new ArgumentNullException(nameof(battleEvent));
            }

            if (IsFinished)
            {
                throw new InvalidOperationException();
            }

            Combatant target = GetCombatant(battleEvent.Target);
            switch (battleEvent.Type)
            {
                case BattleEventType.RestoreHealth:
                    target.Heal(battleEvent.Magnitude);
                    break;
                case BattleEventType.LoseHealth:
                    target.TakeEventDamage(battleEvent.Magnitude);
                    break;
                case BattleEventType.GainEnergy:
                    target.GainEnergy(battleEvent.Magnitude);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public bool CanAppeal(BattleSide side)
        {
            return side == BattleSide.Human
                ? _humanAppealAvailable
                : side == BattleSide.Computer
                    ? _computerAppealAvailable
                    : throw new ArgumentOutOfRangeException(nameof(side));
        }

        public void UseAppeal(BattleSide side)
        {
            if (!CanAppeal(side))
            {
                throw new InvalidOperationException();
            }

            if (side == BattleSide.Human)
            {
                _humanAppealAvailable = false;
            }
            else
            {
                _computerAppealAvailable = false;
            }
        }

        private static void ValidateAction(Combatant combatant, CombatAction action)
        {
            if (!Enum.IsDefined(typeof(CombatAction), action))
            {
                throw new ArgumentOutOfRangeException(nameof(action));
            }

            if (action == CombatAction.Heal && combatant.Energy < HealEnergyCost)
            {
                throw new InvalidOperationException();
            }
        }

        private static int PrepareAction(Combatant combatant, CombatAction action)
        {
            if (action != CombatAction.Heal)
            {
                return 0;
            }

            combatant.SpendEnergy(HealEnergyCost);
            return combatant.Heal(HealAmount);
        }

        private static int CalculateDamage(CombatAction attacker, CombatAction defender)
        {
            switch (attacker)
            {
                case CombatAction.Attack:
                    return defender == CombatAction.Guard ? 5 : 20;
                case CombatAction.Break:
                    return defender == CombatAction.Guard ? 30 : 10;
                default:
                    return 0;
            }
        }

        private Combatant GetCombatant(BattleSide side)
        {
            switch (side)
            {
                case BattleSide.Human:
                    return Human;
                case BattleSide.Computer:
                    return Computer;
                default:
                    throw new ArgumentOutOfRangeException(nameof(side));
            }
        }

        private void UpdateOutcome()
        {
            if (!Human.IsAlive && !Computer.IsAlive)
            {
                Outcome = BattleOutcome.Draw;
            }
            else if (!Computer.IsAlive)
            {
                Outcome = BattleOutcome.HumanWin;
            }
            else if (!Human.IsAlive)
            {
                Outcome = BattleOutcome.ComputerWin;
            }
        }
    }
}
