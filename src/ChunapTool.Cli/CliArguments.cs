using System.Globalization;
using ChunapTool.Core;

namespace ChunapTool.Cli;

/// <summary>「コマンド [サブコマンド] --名前 値 --フラグ」形式の引数。未知の名前や重複は引数の誤りとする。</summary>
internal sealed class CliArguments
{
    // 値を取らないオプション。
    internal static readonly HashSet<string> Flags = ["json", "dry-run", "allow-outside", "foreground", "help", "keep-aspect"];

    private readonly Dictionary<string, string?> options = new(StringComparer.Ordinal);
    private readonly HashSet<string> used = new(StringComparer.Ordinal);

    internal IReadOnlyList<string> Words { get; }

    private CliArguments(List<string> words) => Words = words;

    internal static CliArguments Parse(IReadOnlyList<string> args)
    {
        var words = new List<string>();
        var parsed = new CliArguments(words);
        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length == 2)
            {
                words.Add(arg);
                continue;
            }
            var name = arg[2..];
            if (parsed.options.ContainsKey(name)) throw CliException.Usage($"--{name} が重複しています。");
            if (Flags.Contains(name)) { parsed.options[name] = null; continue; }
            // 次がオプション名なら値の書き忘れとみなす（--pid --json の--jsonを値として扱わない）。
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw CliException.Usage($"--{name} の値がありません。");
            parsed.options[name] = args[++i];
        }
        return parsed;
    }

    internal string? Word(int index) => index < Words.Count ? Words[index] : null;

    internal bool Flag(string name)
    {
        used.Add(name);
        return options.ContainsKey(name);
    }

    internal string? Text(string name)
    {
        used.Add(name);
        return options.TryGetValue(name, out var value) ? value : null;
    }

    internal int? Integer(string name, int minimum = int.MinValue, int maximum = int.MaxValue)
    {
        var text = Text(name);
        if (text is null) return null;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
            throw CliException.Usage($"--{name} には {minimum}〜{maximum} の整数を指定してください: {text}");
        return value;
    }

    internal Point? Coordinate(string name)
    {
        var text = Text(name);
        if (text is null) return null;
        var parts = text.Split(',');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            throw CliException.Usage($"--{name} は「x,y」の形式で指定してください: {text}");
        return new Point(x, y);
    }

    internal BoundsBasis Basis(BoundsBasis fallback = BoundsBasis.Frame)
    {
        var text = Text("basis");
        if (text is null) return fallback;
        return BoundsBases.TryParse(text, out var basis) ? basis : throw CliException.Usage($"--basis には frame か client を指定してください: {text}");
    }

    internal TimeSpan Timeout => TimeSpan.FromMilliseconds(Integer("timeout", 100, 60000) ?? 3000);

    internal TargetCriteria Criteria()
    {
        var handleText = Text("hwnd");
        nint? handle = null;
        if (handleText is not null)
        {
            bool hex = handleText.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (!long.TryParse(hex ? handleText[2..] : handleText, hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                throw CliException.Usage($"--hwnd には10進数か0xで始まる16進数を指定してください: {handleText}");
            handle = (nint)parsed;
        }
        var processId = Integer("pid", 1);
        return new TargetCriteria
        {
            Handle = handle,
            ProcessId = processId is null ? null : (uint)processId.Value,
            Title = Text("title"),
            TitleExact = Text("title-exact"),
            Process = Text("process"),
            ClassName = Text("class"),
            At = Coordinate("at"),
            Foreground = Flag("foreground"),
        };
    }

    // 各コマンドが読まなかったオプションは、そのコマンドでは使えないものとして扱う。
    internal void EnsureAllUsed()
    {
        var unknown = options.Keys.Where(name => !used.Contains(name)).ToArray();
        if (unknown.Length > 0) throw CliException.Usage("このコマンドでは使えないオプションです: " + string.Join(" ", unknown.Select(name => "--" + name)));
    }
}
