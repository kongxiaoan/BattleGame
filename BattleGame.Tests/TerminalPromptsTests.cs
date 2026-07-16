using BattleGame.Cli;
using Spectre.Console;

namespace BattleGame.Tests;

[TestFixture]
public sealed class TerminalPromptsTests
{
    [Test]
    public void Select_WhenAnsiInteractionUnavailable_ShouldUseNumberedFallback()
    {
        var input = new StringReader("2\n");
        var output = new StringWriter();
        IAnsiConsole console = CreatePlainConsole(output);
        var prompts = new TerminalPrompts(console, input, output, useInteractiveAnsi: false);

        string selected = prompts.Select(
            "选择模式",
            new[] { "单机", "局域网" },
            value => value);

        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.EqualTo("局域网"));
            Assert.That(output.ToString(), Does.Contain("1. 单机"));
            Assert.That(output.ToString(), Does.Contain("2. 局域网"));
        });
    }

    [Test]
    public void ReadText_WhenValidationFails_ShouldRepeatWithoutLosingDefaultBehavior()
    {
        var input = new StringReader("\n有效名字\n");
        var output = new StringWriter();
        IAnsiConsole console = CreatePlainConsole(output);
        var prompts = new TerminalPrompts(console, input, output, useInteractiveAnsi: false);

        string value = prompts.ReadText(
            "输入名字",
            defaultValue: null,
            text => text.Length == 0 ? "名字不能为空" : null);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo("有效名字"));
            Assert.That(output.ToString(), Does.Contain("名字不能为空"));
        });
    }

    private static IAnsiConsole CreatePlainConsole(TextWriter writer)
    {
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
    }
}
