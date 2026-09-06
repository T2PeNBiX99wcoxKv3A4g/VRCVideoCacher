namespace VRCVideoCacher.Utils;

/// <summary>
/// Read-only pass-through stream that reports download progress as a 0..1 fraction of a known total,
/// used to surface tool/self-update download progress in the status bar. If <c>total</c> is null or 0 it
/// just forwards bytes and never reports (the activity stays indeterminate). Reporting is throttled to ~1%
/// steps so a per-chunk copy loop cannot flood the UI thread. Owns and disposes the inner stream.
/// </summary>
public sealed class ProgressStream : Stream
{
    private readonly Stream _inner;
    private readonly long _total;
    private readonly Action<double> _report;
    private readonly TimeSpan? _stallTimeout;
    private long _read;
    private double _lastReported = -1;

    /// <param name="stallTimeout">
    /// If set, a single read that receives no data within this window throws a <see cref="TimeoutException"/>.
    /// With <c>ResponseHeadersRead</c> the HttpClient timeout no longer bounds the body, so this is what
    /// stops a dead connection from hanging the download forever. Null = no per-read timeout.
    /// </param>
    public ProgressStream(Stream inner, long? total, Action<double> report, TimeSpan? stallTimeout = null)
    {
        _inner = inner;
        _total = total ?? 0;
        _report = report;
        _stallTimeout = stallTimeout;
    }

    private async ValueTask<int> ReadInnerAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_stallTimeout is not { } stall)
            return await _inner.ReadAsync(buffer, cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(stall);
        try
        {
            return await _inner.ReadAsync(buffer, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Download stalled: no data received for {stall.TotalSeconds:0}s.");
        }
    }

    private void Advance(int n)
    {
        if (n <= 0 || _total <= 0)
            return;
        _read += n;
        var fraction = Math.Clamp((double)_read / _total, 0, 1);
        if (fraction - _lastReported >= 0.01 || fraction >= 1.0)
        {
            _lastReported = fraction;
            _report(fraction);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = _inner.Read(buffer, offset, count);
        Advance(n);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await ReadInnerAsync(buffer, cancellationToken);
        Advance(n);
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var n = await ReadInnerAsync(buffer.AsMemory(offset, count), cancellationToken);
        Advance(n);
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.CanSeek ? _inner.Length : 0;
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        await base.DisposeAsync();
    }
}
