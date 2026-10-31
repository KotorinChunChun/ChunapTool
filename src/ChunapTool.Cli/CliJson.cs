using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChunapTool.Core;

namespace ChunapTool.Cli;

/// <summary>出力JSONの組立てと、restoreで読む状態の解釈。既定のエンコーダーで非ASCIIを\uXXXXにし、コードページに依存させない。</summary>
internal static class CliJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal static string Serialize(JsonNode node) => node.ToJsonString(Options);

    internal static JsonObject Bounds(WindowBounds bounds) => new()
    {
        ["x"] = bounds.X, ["y"] = bounds.Y, ["width"] = bounds.Width, ["height"] = bounds.Height,
    };

    internal static JsonObject Target(WindowEntry entry) => new()
    {
        ["hwnd"] = Handle(entry.Identity.Handle),
        ["pid"] = entry.Identity.ProcessId,
        ["process"] = entry.ProcessName,
        ["title"] = entry.Title,
        ["class"] = entry.ClassName,
    };

    internal static string Handle(nint handle) => "0x" + ((long)handle).ToString("X", CultureInfo.InvariantCulture);

    internal static JsonObject State(WindowState state) => new()
    {
        ["frame"] = Bounds(state.Bounds),
        ["outer"] = state.OuterBounds is { } outer ? Bounds(outer) : null,
        ["maximized"] = state.IsMaximized,
        ["minimized"] = state.IsMinimized,
        ["canMaximize"] = state.CanMaximize,
        ["placement"] = new JsonObject
        {
            ["flags"] = state.Placement.Flags,
            ["showCommand"] = state.Placement.ShowCommand,
            ["minPosition"] = new JsonObject { ["x"] = state.Placement.MinPosition.X, ["y"] = state.Placement.MinPosition.Y },
            ["maxPosition"] = new JsonObject { ["x"] = state.Placement.MaxPosition.X, ["y"] = state.Placement.MaxPosition.Y },
            ["normalBounds"] = Bounds(state.Placement.NormalBounds),
        },
    };

    /// <summary>getの出力（state）または変更系の出力（before）を、復元する状態として読む。対象は呼出し側で決める。</summary>
    internal static WindowState ReadState(string json, WindowIdentity target)
    {
        try
        {
            var root = JsonNode.Parse(json) ?? throw new FormatException("空のJSONです。");
            var state = root["state"] ?? root["before"] ?? root;
            var placement = state["placement"] ?? throw new FormatException("placementがありません。");
            return new WindowState(target, ReadBounds(state["frame"]),
                new WindowPlacement(Value<uint>(placement, "flags"), Value<uint>(placement, "showCommand"),
                    ReadPoint(placement["minPosition"]), ReadPoint(placement["maxPosition"]), ReadBounds(placement["normalBounds"])),
                state["canMaximize"]?.GetValue<bool>() ?? true)
            {
                OuterBounds = state["outer"] is { } outer ? ReadBounds(outer) : null,
            };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new CliException(ExitCodes.Usage, "invalid-state", "復元する状態のJSONを読めません: " + ex.Message);
        }
    }

    private static WindowBounds ReadBounds(JsonNode? node) =>
        node is null ? throw new FormatException("矩形がありません。")
            : new(Value<int>(node, "x"), Value<int>(node, "y"), Value<int>(node, "width"), Value<int>(node, "height"));

    private static WindowPoint ReadPoint(JsonNode? node) =>
        node is null ? throw new FormatException("座標がありません。") : new(Value<int>(node, "x"), Value<int>(node, "y"));

    private static T Value<T>(JsonNode node, string name) =>
        (node[name] ?? throw new FormatException(name + "がありません。")).GetValue<T>();
}
