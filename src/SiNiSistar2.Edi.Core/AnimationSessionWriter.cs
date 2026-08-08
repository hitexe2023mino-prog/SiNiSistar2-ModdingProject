using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace SiNiSistar2.Edi.Core;

public sealed class AnimationSessionWriter : IAsyncDisposable
{
    private readonly Channel<CaptureRecord> _channel;
    private readonly StreamWriter _writer;
    private readonly Task _worker;
    private readonly Action<string>? _logWarning;
    private readonly object _enqueueLock = new();
    private long _sequence;
    private long _pending;
    private long _highWatermark;
    private long _dropped;
    private long _firstDroppedSequence;
    private long _lastDroppedSequence;
    private long _reportedDropped;
    private int _stopping;

    public AnimationSessionWriter(
        string sessionsRoot,
        string gameBuildId,
        string pluginVersion,
        IReadOnlyList<CaptureCapability> capabilities,
        int capacity = 4096,
        Action<string>? logWarning = null)
    {
        if (capacity < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        SessionId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        string directory = Path.Combine(sessionsRoot, SanitizeSegment(gameBuildId));
        Directory.CreateDirectory(directory);
        OutputPath = Path.Combine(directory, $"{SessionId}.animation.jsonl");
        _writer = new StreamWriter(
            new FileStream(OutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        _logWarning = logWarning;
        _channel = Channel.CreateBounded<CaptureRecord>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _worker = Task.Run(WriteLoopAsync);
        Enqueue(
            "session-start",
            new
            {
                pluginVersion,
                gameBuildId,
                capabilities,
                processId = Environment.ProcessId,
            },
            0,
            0);
    }

    public string SessionId { get; }
    public string OutputPath { get; }
    public long DroppedCount => Interlocked.Read(ref _dropped);
    public long HighWatermark => Interlocked.Read(ref _highWatermark);
    public bool IsComplete => DroppedCount == 0;

    public bool Enqueue(string recordType, object payload, long realtimeMs, int frameCount)
    {
        lock (_enqueueLock)
        {
            if (Volatile.Read(ref _stopping) != 0)
            {
                return false;
            }

            TryEnqueueDropWarning(realtimeMs, frameCount);

            long sequence = Interlocked.Increment(ref _sequence);
            var record = new CaptureRecord(
                1,
                recordType,
                SessionId,
                sequence,
                DateTimeOffset.UtcNow,
                realtimeMs,
                frameCount,
                payload);

            if (!_channel.Writer.TryWrite(record))
            {
                RecordDrop(sequence);
                return false;
            }

            long pending = Interlocked.Increment(ref _pending);
            UpdateHighWatermark(pending);
            return true;
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        bool alreadyStopping;
        lock (_enqueueLock)
        {
            alreadyStopping = Interlocked.Exchange(ref _stopping, 1) != 0;
        }

        if (alreadyStopping)
        {
            await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        long droppedBeforeEnd = DroppedCount;
        if (droppedBeforeEnd != Interlocked.Read(ref _reportedDropped))
        {
            long warningSequence = Interlocked.Increment(ref _sequence);
            await _channel.Writer.WriteAsync(
                new CaptureRecord(
                    1,
                    "capture-warning",
                    SessionId,
                    warningSequence,
                    DateTimeOffset.UtcNow,
                    0,
                    0,
                    new
                    {
                        category = "queue-overflow",
                        droppedRecords = droppedBeforeEnd,
                        firstDroppedSequence = Interlocked.Read(ref _firstDroppedSequence),
                        lastDroppedSequence = Interlocked.Read(ref _lastDroppedSequence),
                        catalogComplete = false,
                    }),
                cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _pending);
            Interlocked.Exchange(ref _reportedDropped, droppedBeforeEnd);
        }

        var summary = new
        {
            queuedRecords = Interlocked.Read(ref _sequence),
            droppedRecords = DroppedCount,
            firstDroppedSequence = Interlocked.Read(ref _firstDroppedSequence),
            lastDroppedSequence = Interlocked.Read(ref _lastDroppedSequence),
            highWatermark = HighWatermark,
            complete = IsComplete,
        };
        long sequence = Interlocked.Increment(ref _sequence);
        await _channel.Writer.WriteAsync(
            new CaptureRecord(
                1,
                "session-end",
                SessionId,
                sequence,
                DateTimeOffset.UtcNow,
                0,
                0,
                summary),
            cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _pending);
        _channel.Writer.TryComplete();
        await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        _writer.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        _writer.Dispose();
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (CaptureRecord record in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                string json = JsonSerializer.Serialize(record, JsonOptions);
                await _writer.WriteLineAsync(json).ConfigureAwait(false);
                Interlocked.Decrement(ref _pending);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logWarning?.Invoke($"Animation session write failed: {exception.Message}");
            _channel.Writer.TryComplete(exception);
            throw;
        }
    }

    private void UpdateHighWatermark(long pending)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref _highWatermark);
            if (pending <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _highWatermark, pending, current) != current);
    }

    private void TryEnqueueDropWarning(long realtimeMs, int frameCount)
    {
        long dropped = DroppedCount;
        if (dropped == Interlocked.Read(ref _reportedDropped))
        {
            return;
        }

        long sequence = Interlocked.Increment(ref _sequence);
        var warning = new CaptureRecord(
            1,
            "capture-warning",
            SessionId,
            sequence,
            DateTimeOffset.UtcNow,
            realtimeMs,
            frameCount,
            new
            {
                category = "queue-overflow",
                droppedRecords = dropped,
                firstDroppedSequence = Interlocked.Read(ref _firstDroppedSequence),
                lastDroppedSequence = Interlocked.Read(ref _lastDroppedSequence),
                catalogComplete = false,
            });
        if (_channel.Writer.TryWrite(warning))
        {
            Interlocked.Exchange(ref _reportedDropped, dropped);
            long pending = Interlocked.Increment(ref _pending);
            UpdateHighWatermark(pending);
        }
        else
        {
            RecordDrop(sequence);
        }
    }

    private void RecordDrop(long sequence)
    {
        if (Interlocked.Increment(ref _dropped) == 1)
        {
            Interlocked.Exchange(ref _firstDroppedSequence, sequence);
        }

        Interlocked.Exchange(ref _lastDroppedSequence, sequence);
        _logWarning?.Invoke(
            $"Trigger capture queue is full; sequence {sequence} was not saved. "
            + "The catalog may be missing stages from this interval.");
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private sealed record CaptureRecord(
        int SchemaVersion,
        string RecordType,
        string SessionId,
        long Sequence,
        DateTimeOffset Utc,
        long RealtimeMs,
        int FrameCount,
        object Payload);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
