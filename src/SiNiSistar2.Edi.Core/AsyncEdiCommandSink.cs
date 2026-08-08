namespace SiNiSistar2.Edi.Core;

/// <summary>
/// Reconciles each output towards its latest desired state, and sends one EDI request per group
/// of outputs that want the identical payload.
///
/// Grouping lives here rather than in the coordinator because it is the transport that must not
/// duplicate: a naive "one worker per output" sink turns a three-device trigger into three
/// requests, restarting playback on the devices that were already started (SPEC001 4.3, FR-053).
/// Only the latest state per output is ever sent, so a burst of transitions during an outage
/// collapses instead of replaying (SPEC001 7.3).
/// </summary>
public sealed class AsyncEdiCommandSink : IPlaybackCommandSink
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly IEdiClient _client;
    private readonly IReadOnlyList<string> _outputs;
    private readonly Dictionary<string, OutputState> _states;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Task _worker;
    private readonly Action<string>? _info;
    private readonly Action<string>? _error;
    private bool _wakePending;
    private bool _wasUnavailable;
    private int _shutdownStarted;

    public AsyncEdiCommandSink(
        IEdiClient client,
        IReadOnlyList<string> outputs,
        Action<string>? info = null,
        Action<string>? error = null)
    {
        _client = client;
        _outputs = outputs.ToArray();
        _states = _outputs.ToDictionary(x => x, _ => new OutputState(), StringComparer.Ordinal);
        _info = info;
        _error = error;
        _worker = Task.Run(RunAsync);
    }

    public void Publish(IReadOnlyList<PlaybackCommand> commands)
    {
        if (Volatile.Read(ref _shutdownStarted) != 0 || commands.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            foreach (PlaybackCommand command in commands)
            {
                if (!_states.TryGetValue(command.Output, out OutputState? state))
                {
                    throw new ArgumentException(
                        $"Unknown EDI output '{command.Output}'.", nameof(commands));
                }

                state.Desired = command;
            }
        }

        Wake();
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        // Best effort, and in one request: every output is meant to end up stopped (FR-019).
        try
        {
            await _client
                .ExecuteAsync(new PlaybackRequest(PlaybackCommandKind.Stop, _outputs), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _error?.Invoke($"Best-effort Stop failed for {string.Join(", ", _outputs)}: {exception.Message}");
        }

        _shutdown.Cancel();
        await _worker.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        _shutdown.Dispose();
        _signal.Dispose();
    }

    private void Wake()
    {
        lock (_sync)
        {
            if (_wakePending)
            {
                return;
            }

            _wakePending = true;
        }

        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Another publisher won the race; the worker is already about to run.
        }
    }

    private async Task RunAsync()
    {
        CancellationToken token = _shutdown.Token;
        TimeSpan backoff = InitialBackoff;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_sync)
            {
                _wakePending = false;
            }

            while (!token.IsCancellationRequested)
            {
                IReadOnlyList<PlaybackRequest> pending = TakePending();
                if (pending.Count == 0)
                {
                    break;
                }

                var failed = false;
                foreach (PlaybackRequest request in pending)
                {
                    try
                    {
                        await _client.ExecuteAsync(request, token).ConfigureAwait(false);
                        Commit(request);
                        if (_wasUnavailable)
                        {
                            _wasUnavailable = false;
                            _info?.Invoke("EDI is available again; the latest desired state was applied.");
                        }

                        backoff = InitialBackoff;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception) when (
                        exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
                    {
                        if (!_wasUnavailable)
                        {
                            _wasUnavailable = true;
                            _error?.Invoke(
                                $"EDI is unavailable; the latest desired state is retained and will be "
                                + $"re-sent: {exception.Message}");
                        }

                        failed = true;
                        break;
                    }
                }

                if (!failed)
                {
                    continue;
                }

                try
                {
                    await Task.Delay(backoff, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, MaximumBackoff.TotalSeconds));
            }
        }
    }

    /// <summary>
    /// Snapshots the outputs whose desired state has not been sent and groups the ones that share
    /// a payload. Grouping is by value, so it works whether the coordinator published them in one
    /// batch or the reconciler happened to catch them in the same pass.
    /// </summary>
    private IReadOnlyList<PlaybackRequest> TakePending()
    {
        lock (_sync)
        {
            var groups = new Dictionary<(PlaybackCommandKind, string?, long, bool), List<string>>();
            var order = new List<(PlaybackCommandKind, string?, long, bool)>();

            // Roster order keeps the emitted `channels` list stable and the logs comparable.
            foreach (string output in _outputs)
            {
                OutputState state = _states[output];
                if (state.Desired is null || state.Desired == state.Sent)
                {
                    continue;
                }

                var payload = state.Desired.Payload;
                if (!groups.TryGetValue(payload, out List<string>? members))
                {
                    members = new List<string>();
                    groups[payload] = members;
                    order.Add(payload);
                }

                members.Add(output);
            }

            return order
                .Select(payload => new PlaybackRequest(
                    payload.Item1, groups[payload], payload.Item2, payload.Item3, payload.Item4))
                .ToArray();
        }
    }

    private void Commit(PlaybackRequest request)
    {
        lock (_sync)
        {
            foreach (string output in request.Outputs)
            {
                OutputState state = _states[output];
                if (state.Desired is { } desired && desired.Payload == (
                        request.Kind, request.Gallery, request.SeekMilliseconds, request.UntilResume))
                {
                    state.Sent = desired;
                }
            }
        }
    }

    private sealed class OutputState
    {
        public PlaybackCommand? Desired { get; set; }
        public PlaybackCommand? Sent { get; set; }
    }
}
