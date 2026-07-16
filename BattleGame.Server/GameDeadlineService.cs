using BattleGame.Online;

namespace BattleGame.Server;

/// <summary>
/// 服务端统一推进题目截止时间。客户端倒计时只负责显示，关闭客户端或修改本机时间
/// 都不能延长答题窗口。
/// </summary>
public sealed class GameDeadlineService : BackgroundService
{
    private readonly OnlineCommandProcessor _processor;

    public GameDeadlineService(OnlineCommandProcessor processor)
    {
        _processor = processor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await _processor.ProcessTimeoutsAsync(stoppingToken);
        }
    }
}
