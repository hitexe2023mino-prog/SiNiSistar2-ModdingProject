namespace SiNiSistar2.Edi.Core;

/// <summary>Which outputs currently have a verified device behind them (SPEC001 7.1, FR-042).</summary>
public interface IOutputAvailability
{
    bool IsAvailable(string output);
}

/// <summary>
/// Forwards commands only for outputs whose binding has been verified. An output whose device is
/// missing, on the wrong channel, or carrying the wrong variant has its commands held rather than
/// sent; the other outputs keep working. A configuration with only some of the devices connected
/// is a supported way to run the MOD, which is why this is per output and not a single
/// all-or-nothing gate (SPEC001 7.1, FR-042).
/// </summary>
public sealed class OutputGate : IPlaybackCommandSink, IOutputAvailability
{
    private readonly object _sync = new();
    private readonly IPlaybackCommandSink _inner;
    private readonly IReadOnlyList<string> _outputs;
    private readonly Dictionary<string, PlaybackCommand> _latest = new(StringComparer.Ordinal);
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);

    public OutputGate(IPlaybackCommandSink inner, IReadOnlyList<string> outputs)
    {
        _inner = inner;
        _outputs = outputs.ToArray();
    }

    public bool IsAvailable(string output)
    {
        lock (_sync)
        {
            return _open.Contains(output);
        }
    }

    /// <summary>Outputs that are still held, in roster order.</summary>
    public IReadOnlyList<string> Suppressed()
    {
        lock (_sync)
        {
            return _outputs.Where(x => !_open.Contains(x)).ToArray();
        }
    }

    /// <summary>
    /// Opens an output and sends whatever state it should already be in. Returns false if it was
    /// already open, so a caller can log the transition once.
    /// </summary>
    public bool Open(string output)
    {
        PlaybackCommand? pending;
        lock (_sync)
        {
            if (!_open.Add(output))
            {
                return false;
            }

            // The output missed every command published while it was held, so the latest desired
            // state is sent now rather than replaying the history behind it (SPEC001 7.3).
            _latest.TryGetValue(output, out pending);
        }

        if (pending is not null)
        {
            _inner.Publish(new[] { pending });
        }

        return true;
    }

    /// <summary>
    /// Closes an output that was open. The device is stopped first: holding commands alone would
    /// leave whatever was last commanded looping on a device the MOD has stopped steering
    /// (SPEC001 5.1, FR-055). Returns false if the output was already suppressed.
    /// </summary>
    public bool Suppress(string output)
    {
        lock (_sync)
        {
            if (!_open.Remove(output))
            {
                return false;
            }
        }

        _inner.Publish(new[] { PlaybackCommand.Stop(output) });
        return true;
    }

    public void Publish(IReadOnlyList<PlaybackCommand> commands)
    {
        var forwarded = new List<PlaybackCommand>(commands.Count);
        lock (_sync)
        {
            foreach (PlaybackCommand command in commands)
            {
                _latest[command.Output] = command;
                if (_open.Contains(command.Output))
                {
                    forwarded.Add(command);
                }
            }
        }

        if (forwarded.Count > 0)
        {
            _inner.Publish(forwarded);
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        _inner.ShutdownAsync(cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
