using Spectre.Console;

namespace BattleGame.Cli;

/// <summary>
/// 交互输入的能力降级层。Spectre 只在终端明确支持 ANSI 交互时接管方向键；
/// SSH、重定向和 TERM=dumb 环境使用稳定的数字/文本输入，玩法不会被视觉能力绑架。
/// </summary>
public sealed class TerminalPrompts
{
    private readonly IAnsiConsole _console;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly bool _useInteractiveAnsi;

    public TerminalPrompts(
        IAnsiConsole console,
        TextReader input,
        TextWriter output,
        bool useInteractiveAnsi)
    {
        _console = console;
        _input = input;
        _output = output;
        _useInteractiveAnsi = useInteractiveAnsi;
    }

    public T Select<T>(
        string title,
        IReadOnlyList<T> choices,
        Func<T, string> labelSelector)
        where T : notnull
    {
        if (choices.Count == 0)
        {
            throw new ArgumentException(nameof(choices));
        }

        if (_useInteractiveAnsi)
        {
            return _console.Prompt(
                new SelectionPrompt<T>()
                    .Title($"[bold cyan]{Markup.Escape(title)}[/]")
                    .HighlightStyle(new Style(Color.Black, Color.Cyan, Decoration.Bold))
                    .UseConverter(labelSelector)
                    .AddChoices(choices));
        }

        while (true)
        {
            _output.WriteLine(title);
            for (int index = 0; index < choices.Count; index++)
            {
                _output.WriteLine($"  {index + 1}. {labelSelector(choices[index])}");
            }

            _output.Write("> ");
            string? value = _input.ReadLine();
            if (int.TryParse(value, out int selected)
                && selected >= 1
                && selected <= choices.Count)
            {
                return choices[selected - 1];
            }

            _output.WriteLine(GameText.InvalidChoice);
        }
    }

    public string ReadText(
        string prompt,
        string? defaultValue,
        Func<string, string?>? validator = null)
    {
        if (_useInteractiveAnsi)
        {
            var textPrompt = new TextPrompt<string>($"[cyan]{Markup.Escape(prompt)}[/]");
            if (defaultValue != null)
            {
                textPrompt.DefaultValue(defaultValue);
            }

            if (validator != null)
            {
                textPrompt.Validate(value => validator(value) is string error
                    ? ValidationResult.Error(error)
                    : ValidationResult.Success());
            }

            return _console.Prompt(textPrompt);
        }

        while (true)
        {
            _output.Write(defaultValue == null
                ? $"{prompt}："
                : $"{prompt}（默认 {defaultValue}）：");
            string? input = _input.ReadLine();
            string value = string.IsNullOrEmpty(input) && defaultValue != null
                ? defaultValue
                : input ?? string.Empty;
            string? error = validator?.Invoke(value);
            if (error == null)
            {
                return value;
            }

            _output.WriteLine(error);
        }
    }

    public void WaitForEnter(string prompt)
    {
        _output.WriteLine(prompt);
        _input.ReadLine();
    }
}
