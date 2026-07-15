namespace BattleGame.Core
{
    /// <summary>
    /// 描述参战者在当前回合可以执行的动作。
    /// 使用枚举代替控制台输入值，避免核心规则依赖具体的用户界面。
    /// </summary>
    public enum BattleAction
    {
        Attack = 1,
        Heal = 2
    }
}
