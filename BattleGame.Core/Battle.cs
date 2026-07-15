using System;

namespace BattleGame.Core
{
    /// <summary>
    /// 管理双人战斗的回合顺序与胜负状态。
    /// 战斗进行期间应由此类型统一推进回合，避免外部直接修改参战者状态导致流程失真。
    /// </summary>
    public sealed class Battle
    {
        public Battle(Player firstPlayer, Player secondPlayer)
        {
            if (firstPlayer == null)
            {
                throw new ArgumentNullException(nameof(firstPlayer));
            }

            if (secondPlayer == null)
            {
                throw new ArgumentNullException(nameof(secondPlayer));
            }

            // 使用引用身份判断，因为同名玩家仍然可以是两个不同的参战对象。
            if (ReferenceEquals(firstPlayer, secondPlayer))
            {
                throw new ArgumentException(null, nameof(secondPlayer));
            }

            if (!firstPlayer.IsAlive)
            {
                throw new ArgumentException(null, nameof(firstPlayer));
            }

            if (!secondPlayer.IsAlive)
            {
                throw new ArgumentException(null, nameof(secondPlayer));
            }

            CurrentAttacker = firstPlayer;
            CurrentDefender = secondPlayer;
        }

        public Player CurrentAttacker { get; private set; }

        public Player CurrentDefender { get; private set; }

        // 胜者是战斗结束的唯一事实来源，避免额外布尔字段与 Winner 状态不一致。
        public bool IsFinished => Winner != null;

        public Player? Winner { get; private set; }

        /// <summary>
        /// 执行一个完整回合：攻击、判定胜负，并在战斗继续时交换攻守双方。
        /// </summary>
        public void ExecuteTurn()
        {
            ExecuteTurn(BattleAction.Attack, healingAmount: 1);
        }

        /// <summary>
        /// 执行指定动作，并在战斗继续时把回合交给另一名玩家。
        /// </summary>
        /// <param name="action">当前攻击者选择的回合动作。</param>
        /// <param name="healingAmount">治疗动作恢复的生命值；攻击动作不会使用该值。</param>
        public void ExecuteTurn(BattleAction action, int healingAmount)
        {
            // 结束后禁止继续推进，确保胜负结果和角色生命值不再被战斗流程修改。
            if (IsFinished)
            {
                throw new InvalidOperationException();
            }

            // 先验证动作再修改玩家，确保非法动作不会留下执行到一半的回合。
            switch (action)
            {
                case BattleAction.Attack:
                    CurrentAttacker.Attack(CurrentDefender);
                    break;
                case BattleAction.Heal:
                    CurrentAttacker.Heal(healingAmount);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }

            if (!CurrentDefender.IsAlive)
            {
                Winner = CurrentAttacker;
                return;
            }

            // 先保存下一位攻击者，避免属性赋值过程中覆盖并丢失原攻击者引用。
            Player nextAttacker = CurrentDefender;
            CurrentDefender = CurrentAttacker;
            CurrentAttacker = nextAttacker;
        }
    }
}
