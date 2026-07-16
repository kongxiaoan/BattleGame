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
    private static async Task Main()
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        // 默认控制台由 Spectre 负责能力检测和降级，重定向输出时不会强制输出颜色。
        Spectre.Console.IAnsiConsole console = Spectre.Console.AnsiConsole.Console;
        bool interactiveAnsi = console.Profile.Capabilities.Ansi
            && !Console.IsInputRedirected
            && !Console.IsOutputRedirected;
        var prompts = new TerminalPrompts(
            console,
            Console.In,
            Console.Out,
            interactiveAnsi);
        string deviceToken = Online.DeviceIdentityStore.CreateDefault().LoadOrCreate();
        var shell = new SpectreGameShell(console, prompts, deviceToken);
        await shell.RunAsync(cancellation.Token);
    }
}
