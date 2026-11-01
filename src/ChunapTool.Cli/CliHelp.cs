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
          get         状態を取得（--basis frame|client）
          set         位置・サイズを変更（--x --y --width --height --basis --keep-aspect --aspect）
          maximize    最大化
          unmaximize  通常表示へ戻す
          constrain   作業領域へ収める
          restore     get等で保存した状態へ戻す（--from <JSONファイル>）
          preset      サイズのプリセット（list add remove rename move apply）
          config      設定（path get set reset）
          version     版を表示
          help        使い方（chunap help <コマンド>）

        共通オプション:
          --json         結果をJSONで出力
          --dry-run      変更せずに結果だけを計算（変更系のコマンド）
          --timeout <ms> 反映の確認を待つ時間（既定3000、100〜60000）
          --settings-dir <フォルダー>  設定ファイルの場所（既定 %LOCALAPPDATA%\KotorinToys\ChunapTool）

        終了コード: 0 成功 / 1 失敗 / 2 引数の誤り / 3 対象なし / 4 対象が複数 / 5 応答なし・拒否 / 7 反映を確認できない / 8 設定ファイルの不正
        """;

    internal static string? For(string command) => command switch
    {
        "list" => "chunap list [--filter <文字列>] [--json]\n  操作できるウィンドウを一覧します。ChunapTool自身は含みません。",
        "pick" => "chunap pick --at <x,y> [--json]\n  画面座標にあるトップレベルウィンドウを返します（GUIの⌖ドラッグと同じ判定）。",
        "get" => "chunap get <対象の指定> [--basis frame|client] [--json]\n"
            + "  見える枠・中身（クライアント領域）・外枠・最大化・WINDOWPLACEMENTを出力します。--jsonの出力はrestore --fromで使えます。\n"
            + "  --basisで、文章とJSONのboundsに出す基準を選びます（既定frame）。\n\n" + Selector,
        "set" => "chunap set <対象の指定> [--x N] [--y N] [--width N] [--height N] [--basis frame|client]\n"
            + "           [--keep-aspect | --aspect W:H] [--allow-outside] [--dry-run] [--json]\n"
            + "  位置・サイズを変更します。指定しなかった値は現在の値を使います。\n"
            + "  --basis client では、タイトルバーと枠を除いた中身の位置・サイズとして扱います（既定frameは見える枠）。\n"
            + "  --keep-aspect は現在の縦横比を、--aspect は指定の比率を保ちます。幅と高さのどちらか一方だけを指定してください。\n"
            + "  --allow-outsideがなければ、対象のモニターの作業領域へ収めます（GUIの「画面外可」OFFと同じ）。\n\n" + Selector,
        "preset" => "chunap preset list\n"
            + "chunap preset add <名前> --width N --height N [--basis client|frame]\n"
            + "chunap preset remove <名前>\n"
            + "chunap preset rename <名前> <新しい名前>\n"
            + "chunap preset move <名前> --to <順番（1から）>\n"
            + "chunap preset apply <名前> <対象の指定> [--allow-outside] [--dry-run] [--json]\n"
            + "  プリセットは位置を変えず、幅・高さだけを変えます。基準の既定はclient（中身）です。\n"
            + "  名前は大文字小文字を区別しません。存在しない名前や重複は終了コード2です。\n\n" + Selector,
        "config" => "chunap config path\n"
            + "chunap config get [キー]\n"
            + "chunap config set <キー> <値>\n"
            + "chunap config reset\n"
            + "  キー: gui.autoRead gui.autoApply gui.allowOutside gui.toolTopMost gui.keepAspect gui.previewExpanded（true|false）、\n"
            + "        gui.basis（frame|client）。\n"
            + "  起動中のGUIには、次回の起動から反映されます。resetはGUIの項目を既定値へ戻し、プリセットは残します。\n"
            + "  読めない設定ファイルは終了コード8です。resetは読めないファイルを退避してから既定値で作り直します。",
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
