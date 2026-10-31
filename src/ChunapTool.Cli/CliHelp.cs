namespace ChunapTool.Cli;

internal static class CliHelp
{
    private const string Selector = """
        対象の指定（組み合わせると、すべてを満たすもの）:
          --hwnd <0x16進数|10進数>  --pid <番号>  --title <部分一致>  --title-exact <完全一致>
          --process <名前.exe>  --class <クラス名>  --at <x,y>  --foreground
          一致が0件なら終了コード3、2件以上なら終了コード4で、何も変更しません。
        """;

    internal const string Overview = """
        chunap - ChunapToolのコマンドライン

        使い方: chunap <コマンド> [対象の指定] [オプション]

        コマンド:
          list        操作できるウィンドウの一覧（--filter <文字列>）
          pick        座標にあるウィンドウ（--at x,y）
          get         状態を取得
          set         位置・サイズを変更（--x --y --width --height）
          maximize    最大化
          unmaximize  通常表示へ戻す
          constrain   作業領域へ収める
          restore     get等で保存した状態へ戻す（--from <JSONファイル>）
          version     版を表示
          help        使い方（chunap help <コマンド>）

        共通オプション:
          --json         結果をJSONで出力
          --dry-run      変更せずに結果だけを計算（変更系のコマンド）
          --timeout <ms> 反映の確認を待つ時間（既定3000、100〜60000）

        終了コード: 0 成功 / 1 失敗 / 2 引数の誤り / 3 対象なし / 4 対象が複数 / 5 応答なし・拒否 / 7 反映を確認できない
        """;

    internal static string? For(string command) => command switch
    {
        "list" => "chunap list [--filter <文字列>] [--json]\n  操作できるウィンドウを一覧します。ChunapTool自身は含みません。",
        "pick" => "chunap pick --at <x,y> [--json]\n  画面座標にあるトップレベルウィンドウを返します（GUIの⌖ドラッグと同じ判定）。",
        "get" => "chunap get <対象の指定> [--json]\n  見える枠・外枠・最大化・WINDOWPLACEMENTを出力します。--jsonの出力はrestore --fromで使えます。\n\n" + Selector,
        "set" => "chunap set <対象の指定> [--x N] [--y N] [--width N] [--height N] [--allow-outside] [--dry-run] [--json]\n"
            + "  見える枠の位置・サイズを変更します。指定しなかった値は現在の値を使います。\n"
            + "  --allow-outsideがなければ、対象のモニターの作業領域へ収めます（GUIの「画面外可」OFFと同じ）。\n\n" + Selector,
        "maximize" => "chunap maximize <対象の指定> [--dry-run] [--json]\n\n" + Selector,
        "unmaximize" => "chunap unmaximize <対象の指定> [--dry-run] [--json]\n\n" + Selector,
        "constrain" => "chunap constrain <対象の指定> [--dry-run] [--json]\n  現在の位置・サイズを作業領域へ収めます。\n\n" + Selector,
        "restore" => "chunap restore <対象の指定> --from <JSONファイル> [--dry-run] [--json]\n"
            + "  get --jsonの出力（state）か、変更系の出力（before）を読み、その状態へ戻します。\n\n" + Selector,
        "version" => "chunap version [--json]",
        "help" => "chunap help [コマンド]",
        _ => null,
    };
}
