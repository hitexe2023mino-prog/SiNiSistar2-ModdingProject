namespace SiNiSistar2.Edi.Core;

public sealed class AsyncEdiCommandSink : IPlaybackCommandSink
{
    private readonly IEdiClient _client;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, ChannelWorker> _workers;
    private readonly Action<string>? _info;
    private readonly Action<string>? _error;
    private int _shutdownStarted;

    public AsyncEdiCommandSink(
        IEdiClient client,
        Action<string>? info = null,
        Action<string>? error = null)
    {
        _client = client;
        _info = info;
        _error = error;
        _workers = EdiChannels.All.ToDictionary(
            channel => channel,
            channel => new ChannelWorker(channel, client, _shutdown.Token, OnAvailable, OnUnavailable),
            StringComparer.Ordinal);
    }

    public void Publish(PlaybackCommand command)
    {
        if (!_workers.TryGetValue(command.Channel, out var worker))
        {
            throw new ArgumentException($"Unknown EDI channel '{command.Channel}'.", nameof(command));
        }

        if (Volatile.Read(ref _shutdownStarted) == 0)
        {
            worker.Publish(command);
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        foreach (var channel in EdiChannels.All)
        {
            try
            {
                await _client.ExecuteAsync(PlaybackCommand.Stop(channel), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                _error?.Invoke($"Best-effort Stop failed for channel '{channel}': {exception.Message}");
            }
        }

        _shutdown.Cancel();
        await Task.WhenAll(_workers.Values.Select(x => x.Completion)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private void OnAvailable(string channel) =>
        _info?.Invoke($"EDI channel '{channel}' is available; latest desired state applied.");

    private void OnUnavailable(string channel, Exception exception) =>
        _error?.Invoke($"EDI channel '{channel}' is unavailable: {exception.Message}");

    private sealed class ChannelWorker
    {
        private readonly object _sync = new();
        private readonly string _channel;
        private readonly IEdiClient _client;
        private readonly CancellationToken _shutdown;
        private readonly Action<string> _onAvailable;
        private readonly Action<string, Exception> _onUnavailable;
        private readonly SemaphoreSlim _signal = new(0, 1);
        private PlaybackCommand? _latest;
        private bool _wakePending;
        private bool _wasUnavailable;

        public ChannelWorker(
            string channel,
            IEdiClient client,
            CancellationToken shutdown,
            Action<string> onAvailable,
            Action<string, Exception> onUnavailable)
        {
            _channel = channel;
            _client = client;
            _shutdown = shutdown;
            _onAvailable = onAvailable;
            _onUnavailable = onUnavailable;
            Completion = RunAsync();
        }

        public Task Completion { get; }

        public void Publish(PlaybackCommand command)
        {
            lock (_sync)
            {
                _latest = command;
                if (!_wakePending)
                {
                    _wakePending = true;
                    _signal.Release();
                }
            }
        }

        private async Task RunAsync()
        {
            try
            {
                while (true)
                {
                    await _signal.WaitAsync(_shutdown).ConfigureAwait(false);
                    PlaybackCommand? command;
                    lock (_sync)
                    {
                        _wakePending = false;
                        command = _latest;
                        _latest = null;
                    }

                    if (command is not null)
                    {
                        await ExecuteLatestAsync(command).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            finally
            {
                _signal.Dispose();
            }
        }

        private async Task ExecuteLatestAsync(PlaybackCommand command)
        {
            var backoff = TimeSpan.FromSeconds(1);
            while (!_shutdown.IsCancellationRequested)
            {
                lock (_sync)
                {
                    if (_latest is not null)
                    {
                        command = _latest;
                        _latest = null;
                    }
                }

                try
                {
                    await _client.ExecuteAsync(command, _shutdown).ConfigureAwait(false);
                    if (_wasUnavailable)
                    {
                        _wasUnavailable = false;
                        _onAvailable(_channel);
                    }

                    return;
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    if (!_wasUnavailable)
                    {
                        _wasUnavailable = true;
                        _onUnavailable(_channel, exception);
                    }

                    await Task.Delay(backoff, _shutdown).ConfigureAwait(false);
                    backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
                }
            }
        }
    }
}
