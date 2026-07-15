//导入命名空间，不等于添加项目引用；两者都需要

namespace BattleGame.Cli;

/**
 * internal：Program 只允许当前项目使用。
static class：入口类不需要创建对象，明确禁止 new Program()。
删除 string[] args：当前不接收命令行参数，消除未使用提示。
Main 必须是 static，因为程序启动时还没有任何 Program 对象
 */
internal static class Program
{
    private static void Main()
    {
        // 入口只负责连接真实控制台，具体流程保留在可独立测试的 ConsoleGame 中。
        var game = new ConsoleGame(Console.In, Console.Out);
        game.Run();
    }
}
