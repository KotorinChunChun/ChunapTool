using System.ComponentModel;
using ChunapTool.Operations;

namespace ChunapTool.Cli;

/// <summary>終了コードは設計書5.4節。0成功、1その他、2引数、3対象なし、4曖昧、5応答なし・拒否、6 GUI、7確認できない、8設定。</summary>
internal static class ExitCodes
{
    internal const int Success = 0;
    internal const int Failure = 1;
    internal const int Usage = 2;
    internal const int NotFound = 3;
    internal const int Ambiguous = 4;
    internal const int Refused = 5;
    internal const int Gui = 6;
    internal const int NotConfirmed = 7;
    internal const int Settings = 8;
}

internal sealed class CliException(int exitCode, string code, string message, object? details = null) : Exception(message)
{
    internal int ExitCode { get; } = exitCode;
    internal string Code { get; } = code;
    internal object? Details { get; } = details;

    internal static CliException Usage(string message) => new(ExitCodes.Usage, "usage", message);

    // 操作中に発生した例外を、終了コードと機械向けの識別子へ対応付ける。
    internal static CliException From(Exception exception) => exception switch
    {
        CliException cli => cli,
        TargetGoneException => new(ExitCodes.NotFound, "target-gone", exception.Message),
        Win32Exception { NativeErrorCode: 1400 } => new(ExitCodes.NotFound, "target-gone", exception.Message),
        Win32Exception => new(ExitCodes.Refused, "target-refused", exception.Message),
        TimeoutException => new(ExitCodes.NotConfirmed, "not-confirmed", exception.Message),
        InvalidOperationException or ArgumentException => new(ExitCodes.Failure, "operation-failed", exception.Message),
        _ => new(ExitCodes.Failure, "unexpected", $"{exception.GetType().Name}: {exception.Message}"),
    };
}
