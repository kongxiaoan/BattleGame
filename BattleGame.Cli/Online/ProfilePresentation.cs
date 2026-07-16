using System.Globalization;
using BattleGame.Online;

namespace BattleGame.Cli.Online;

/// <summary>
/// 个人资料的纯展示规则。把计算从 Spectre 组件中抽离，便于测试，也避免不同终端显示出不同胜率。
/// </summary>
public static class ProfilePresentation
{
    public static double CalculateWinRate(int wins, int gamesPlayed)
    {
        return gamesPlayed == 0
            ? 0
            : Math.Round((double)wins / gamesPlayed * 100, 1, MidpointRounding.AwayFromZero);
    }

    public static string DescribeTagLifetime(PlayerTag tag)
    {
        return tag.ExpiresAt is DateTimeOffset expiresAt
            ? string.Format(
                CultureInfo.CurrentCulture,
                GameText.TagExpires,
                expiresAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture))
            : GameText.TagPermanent;
    }
}
