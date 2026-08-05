namespace SiNiSistar2.Edi.Core;

public enum PlaybackCommandKind
{
    Play,
    Stop,
    Pause,
}

public sealed record PlaybackCommand(
    PlaybackCommandKind Kind,
    string Channel,
    string? Gallery = null,
    long SeekMilliseconds = 0,
    bool UntilResume = false)
{
    public static PlaybackCommand Play(string gallery, long seekMilliseconds, string channel) =>
        new(PlaybackCommandKind.Play, channel, gallery, seekMilliseconds);

    public static PlaybackCommand Stop(string channel) =>
        new(PlaybackCommandKind.Stop, channel);

    public static PlaybackCommand Pause(string channel) =>
        new(PlaybackCommandKind.Pause, channel, UntilResume: true);
}

public interface IPlaybackCommandSink : IAsyncDisposable
{
    void Publish(PlaybackCommand command);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

public sealed class PlaybackCoordinator
{
    private readonly object _sync = new();
    private readonly MappingRepository _mappings;
    private readonly IPlaybackCommandSink _sink;
    private readonly IEventDiagnostics _diagnostics;
    private readonly Action<string>? _warning;
    private readonly Dictionary<string, ChannelRuntime> _channels = EdiChannels.All
        .ToDictionary(x => x, _ => new ChannelRuntime(), StringComparer.Ordinal);
    private readonly HashSet<EventKey> _warnedUnknownEvents = new();
    private HashSet<string> _activeStatuses = new(StringComparer.Ordinal);
    private bool _isGameplayActive;
    private bool _isPaused;

    public PlaybackCoordinator(
        MappingRepository mappings,
        IPlaybackCommandSink sink,
        IEventDiagnostics diagnostics,
        Action<string>? warning = null)
    {
        _mappings = mappings;
        _sink = sink;
        _diagnostics = diagnostics;
        _warning = warning;
    }

    public void SetGameplayActive(IEnumerable<string> activeStatuses)
    {
        lock (_sync)
        {
            _isGameplayActive = true;
            _activeStatuses = activeStatuses.ToHashSet(StringComparer.Ordinal);
            if (!_isPaused)
            {
                ApplyFillersToIdleChannels();
            }
        }
    }

    public void UpdateStatuses(IEnumerable<string> activeStatuses)
    {
        lock (_sync)
        {
            _activeStatuses = activeStatuses.ToHashSet(StringComparer.Ordinal);
            if (_isGameplayActive && !_isPaused)
            {
                ApplyFillersToIdleChannels();
            }
        }
    }

    public bool ObserveEvent(EventObservation observation, bool forceReplay = false)
    {
        lock (_sync)
        {
            _diagnostics.RecordEvent(observation);
            if (!_mappings.TryResolve(observation.Key, out var mapping))
            {
                if (_mappings.Classify(observation.Key) == MappingDisposition.Unclassified
                    && _warnedUnknownEvents.Add(observation.Key))
                {
                    _warning?.Invoke(
                        $"Unregistered event {observation.Key.Context}/{observation.Key.ActorId}/"
                        + $"{observation.Key.AnimationId}/{observation.Key.Phase}; playback was suppressed.");
                }

                return false;
            }

            _isGameplayActive = true;
            foreach (var channel in mapping.Channels)
            {
                var runtime = _channels[channel];
                if (!forceReplay
                    && runtime.Mode == ChannelMode.Event
                    && runtime.ActiveEvent == observation.Key)
                {
                    continue;
                }

                runtime.Mode = ChannelMode.Event;
                runtime.ActiveEvent = observation.Key;
                PublishIfChanged(
                    channel,
                    PlaybackCommand.Play(
                        mapping.Gallery!,
                        CalculateSeek(mapping.SeekMode!, observation),
                        channel));
            }

            return true;
        }
    }

    public void EndEvent(EventKey key)
    {
        lock (_sync)
        {
            foreach (var (channel, runtime) in _channels)
            {
                if (runtime.Mode != ChannelMode.Event || runtime.ActiveEvent != key)
                {
                    continue;
                }

                runtime.ActiveEvent = null;
                runtime.Mode = _isPaused ? ChannelMode.Paused : ChannelMode.Filler;
                if (_isGameplayActive && !_isPaused)
                {
                    PublishFiller(channel, runtime);
                }
            }
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (!_isGameplayActive || _isPaused)
            {
                return;
            }

            _isPaused = true;
            foreach (var (channel, runtime) in _channels)
            {
                runtime.Mode = ChannelMode.Paused;
                PublishIfChanged(channel, PlaybackCommand.Pause(channel));
            }
        }
    }

    public void Resume(EventObservation? currentEvent)
    {
        lock (_sync)
        {
            if (!_isPaused)
            {
                return;
            }

            _isPaused = false;
            foreach (var runtime in _channels.Values)
            {
                runtime.Mode = ChannelMode.Filler;
                runtime.ActiveEvent = null;
            }

            if (currentEvent is not null && ObserveEventCore(currentEvent, true))
            {
                ApplyFillersToIdleChannels();
            }
            else if (_isGameplayActive)
            {
                ApplyFillersToIdleChannels();
            }
            else
            {
                StopAll();
            }
        }
    }

    public void SetInactive()
    {
        lock (_sync)
        {
            _isGameplayActive = false;
            _isPaused = false;
            StopAll();
        }
    }

    private bool ObserveEventCore(EventObservation observation, bool forceReplay)
    {
        _diagnostics.RecordEvent(observation);
        if (!_mappings.TryResolve(observation.Key, out var mapping))
        {
            return false;
        }

        foreach (var channel in mapping.Channels)
        {
            var runtime = _channels[channel];
            if (!forceReplay
                && runtime.Mode == ChannelMode.Event
                && runtime.ActiveEvent == observation.Key)
            {
                continue;
            }

            runtime.Mode = ChannelMode.Event;
            runtime.ActiveEvent = observation.Key;
            PublishIfChanged(
                channel,
                PlaybackCommand.Play(
                    mapping.Gallery!,
                    CalculateSeek(mapping.SeekMode!, observation),
                    channel));
        }

        return true;
    }

    private void ApplyFillersToIdleChannels()
    {
        foreach (var (channel, runtime) in _channels)
        {
            if (runtime.Mode != ChannelMode.Event)
            {
                runtime.Mode = ChannelMode.Filler;
                runtime.ActiveEvent = null;
                PublishFiller(channel, runtime);
            }
        }
    }

    private void PublishFiller(string channel, ChannelRuntime runtime)
    {
        var filler = _mappings.SelectFiller(channel, _activeStatuses);
        runtime.Mode = ChannelMode.Filler;
        PublishIfChanged(channel, PlaybackCommand.Play(filler, 0, channel));
    }

    private void StopAll()
    {
        foreach (var (channel, runtime) in _channels)
        {
            runtime.Mode = ChannelMode.Inactive;
            runtime.ActiveEvent = null;
            PublishIfChanged(channel, PlaybackCommand.Stop(channel));
        }
    }

    private void PublishIfChanged(string channel, PlaybackCommand command)
    {
        var runtime = _channels[channel];
        if (runtime.LastPublished == command)
        {
            return;
        }

        runtime.LastPublished = command;
        _sink.Publish(command);
    }

    private static long CalculateSeek(string seekMode, EventObservation observation)
    {
        if (seekMode == "zero" || observation.ClipLengthSeconds <= 0)
        {
            return 0;
        }

        var normalized = observation.NormalizedTime - Math.Floor(observation.NormalizedTime);
        var milliseconds = normalized * observation.ClipLengthSeconds * 1000d;
        return Math.Max(0, checked((long)Math.Round(milliseconds, MidpointRounding.AwayFromZero)));
    }

    private enum ChannelMode
    {
        Inactive,
        Filler,
        Event,
        Paused,
    }

    private sealed class ChannelRuntime
    {
        public ChannelMode Mode { get; set; } = ChannelMode.Inactive;
        public EventKey? ActiveEvent { get; set; }
        public PlaybackCommand? LastPublished { get; set; }
    }
}
