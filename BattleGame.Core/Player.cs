using System;

namespace BattleGame.Core
{
    public sealed class Player
    {
        public Player(string name, int maxHealth, int attackPower)
        {
            // 在对象创建入口拦截无效数据，避免不完整的玩家状态进入战斗流程。
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(nameof(name));
            }

            if (maxHealth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHealth));
            }

            if (attackPower <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attackPower));
            }

            Name = name;
            MaxHealth = maxHealth;
            AttackPower = attackPower;
            Health = maxHealth;
        }

        public string Name { get; }

        public int MaxHealth { get; }

        public int AttackPower { get; }

        // 外部只能读取生命值，后续必须通过受控的受伤或治疗行为修改它。
        public int Health { get; private set; }

        // 存活状态由生命值实时推导，避免维护两份可能不一致的状态。
        public bool IsAlive => Health > 0;

        public void TakeDamage(int damage)
        {
            if (damage <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(damage));
            }

            Health = Math.Max(Health - damage, 0);
        }

        public void Heal(int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (!IsAlive)
            {
                throw new InvalidOperationException();
            }

            // 先限制实际治疗量，避免 Health + amount 在极端输入下发生整数溢出。
            Health += Math.Min(amount, MaxHealth - Health);
        }

        public void Attack(Player target)
        {
            // 先验证调用参数和双方状态，保证失败的攻击不会改变任何生命值。
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (!IsAlive)
            {
                throw new InvalidOperationException();
            }

            if (!target.IsAlive)
            {
                throw new InvalidOperationException();
            }

            target.TakeDamage(AttackPower);
        }
    }
}
