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
    private long _read;
    private double _lastReported = -1;

    public ProgressStream(Stream inner, long? total, Action<double> report)
    {
        _inner = inner;
        _total = total ?? 0;
        _report = report;
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
        var n = await _inner.ReadAsync(buffer, cancellationToken);
        Advance(n);
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var n = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
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
