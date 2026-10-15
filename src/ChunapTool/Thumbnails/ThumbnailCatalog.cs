using ChunapTool.Core;

namespace ChunapTool.Thumbnails;

internal interface IThumbnailCapture : IDisposable
{
    Task<ThumbnailCaptureResult> CaptureAsync(WindowIdentity target, Size maximumSize, CancellationToken cancellationToken);
}

internal sealed class ThumbnailCaptureResult(Bitmap? image, string status) : IDisposable
{
    internal Bitmap? Image { get; private set; } = image;
    internal string Status { get; } = status;
    internal Bitmap? TakeImage() { var image = Image; Image = null; return image; }
    public void Dispose() { Image?.Dispose(); Image = null; }
}

internal sealed class ThumbnailTile(WindowEntry entry)
{
    internal WindowEntry Entry { get; set; } = entry;
    internal Bitmap? Image { get; set; }
    internal string Status { get; set; } = "未取得";
    internal DateTimeOffset? LastAttempt { get; set; }
}

/// <summary>画像の所有権、候補の寿命、単一撮影と更新間隔をまとめて管理する。</summary>
internal sealed class ThumbnailCatalog(IThumbnailCapture capture) : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<WindowIdentity, ThumbnailTile> tiles = [];
    private readonly CancellationTokenSource lifetime = new();
    private bool refreshing, disposed;
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    internal static readonly Size MaximumImageSize = new(320, 200);

    internal void UpdateEntries(IEnumerable<WindowEntry> entries)
    {
        lock (gate)
        {
            if (disposed) return;
            var current = entries.DistinctBy(entry => entry.Identity).ToArray();
            var identities = current.Select(entry => entry.Identity).ToHashSet();
            foreach (var identity in tiles.Keys.Where(id => !identities.Contains(id)).ToArray())
            {
                tiles[identity].Image?.Dispose();
                tiles.Remove(identity);
            }
            foreach (var entry in current)
            {
                if (tiles.TryGetValue(entry.Identity, out var tile)) tile.Entry = entry;
                else tiles.Add(entry.Identity, new ThumbnailTile(entry));
            }
        }
    }

    internal IReadOnlyList<ThumbnailTile> Filter(string filter)
    {
        lock (gate)
            return disposed ? [] : tiles.Values.Where(tile => WindowCandidates.Matches(tile.Entry, filter))
                .OrderBy(tile => tile.Entry.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tile => tile.Entry.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tile => tile.Entry.Identity.ProcessId).ThenBy(tile => tile.Entry.Identity.Handle).ToArray();
    }

    internal async Task<WindowIdentity?> RefreshNextAsync(IReadOnlyList<WindowIdentity> visible,
        IReadOnlyList<WindowIdentity> matches, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ThumbnailTile tile;
        CancellationToken lifetimeToken;
        lock (gate)
        {
            if (disposed || refreshing || cancellationToken.IsCancellationRequested) return null;
            var due = matches.Distinct().Where(id => tiles.ContainsKey(id)).Select(id => tiles[id])
                .Where(tile => tile.LastAttempt is null || now - tile.LastAttempt.Value >= RefreshInterval).ToArray();
            // 未取得を先に埋め、取得済みは更新期限の古い順。画面内を優先する。
            var next = due.OrderBy(tile => !visible.Contains(tile.Entry.Identity))
                .ThenBy(tile => tile.LastAttempt is not null)
                .ThenBy(tile => tile.LastAttempt).FirstOrDefault();
            if (next is null) return null;
            tile = next;
            tile.LastAttempt = now;
            refreshing = true;
            lifetimeToken = lifetime.Token;
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, cancellationToken);
            using var result = await capture.CaptureAsync(tile.Entry.Identity, MaximumImageSize, linked.Token);
            lock (gate)
            {
                if (disposed || linked.IsCancellationRequested
                    || !tiles.TryGetValue(tile.Entry.Identity, out var current) || current != tile) return null;
                var image = result.TakeImage();
                if (image is not null) { tile.Image?.Dispose(); tile.Image = image; }
                tile.Status = result.Status;
                return tile.Entry.Identity;
            }
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            lock (gate)
            {
                if (disposed || cancellationToken.IsCancellationRequested || !tiles.ContainsValue(tile)) return null;
                tile.Status = "取得不可: " + ex.Message;
                return tile.Entry.Identity;
            }
        }
        finally { lock (gate) refreshing = false; }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            foreach (var tile in tiles.Values) { tile.Image?.Dispose(); tile.Image = null; }
            tiles.Clear();
            capture.Dispose();
            lifetime.Dispose();
        }
    }
}
