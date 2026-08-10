namespace SiNiSistar2.Edi.Core;

public enum PlaybackCommandKind
{
    Play,
    Stop,
    Pause,
}

/// <summary>
/// One output's desired state. Commands are per output; the sink is what groups outputs that
/// want the identical payload into a single EDI request (SPEC001 5.5, FR-046, FR-053).
/// </summary>
public sealed record PlaybackCommand(
    PlaybackCommandKind Kind,
    string Output,
    string? Gallery = null,
    long SeekMilliseconds = 0,
    bool UntilResume = false)
{
    public static PlaybackCommand Play(string gallery, long seekMilliseconds, string output) =>
        new(PlaybackCommandKind.Play, output, gallery, seekMilliseconds);

    public static PlaybackCommand Stop(string output) =>
        new(PlaybackCommandKind.Stop, output);

    public static PlaybackCommand Pause(string output) =>
        new(PlaybackCommandKind.Pause, output, UntilResume: true);

    /// <summary>Everything except the output, so the sink can tell which commands may be grouped.</summary>
    public (PlaybackCommandKind, string?, long, bool) Payload =>
        (Kind, Gallery, SeekMilliseconds, UntilResume);
}

public interface IPlaybackCommandSink : IAsyncDisposable
{
    /// <summary>
    /// Publishes a set of desired states at once. Passing them together is what lets the sink
    /// emit one request for the outputs that share a payload, so a trigger that starts three
    /// devices starts them together (FR-046).
    /// </summary>
    void Publish(IReadOnlyList<PlaybackCommand> commands);

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

public static class PlaybackCommandSinkExtensions
{
    public static void Publish(this IPlaybackCommandSink sink, PlaybackCommand command) =>
        sink.Publish(new[] { command });
}

public sealed class PlaybackCoordinator
{
    private readonly object _sync = new();
    private readonly MappingRepository _mappings;
    private readonly IPlaybackCommandSink _sink;
    private readonly IEventDiagnostics _diagnostics;
    private readonly Action<string>? _warning;
    private readonly IOutputAvailability? _availability;
    private readonly Dictionary<string, OutputRuntime> _outputs;
    private readonly IReadOnlyList<string> _outputIds;
    private readonly HashSet<EventKey> _warnedUnknownEvents = new();
    private HashSet<string> _activeStatuses = new(StringComparer.Ordinal);
    private EventObservation? _currentEvent;
    private bool _isGameplayActive;
    private bool _isPaused;
    private bool _isPreviewing;

    public PlaybackCoordinator(
        MappingRepository mappings,
        IPlaybackCommandSink sink,
        IEventDiagnostics diagnostics,
        Action<string>? warning = null,
        IOutputAvailability? availability = null)
    {
        _mappings = mappings;
        _sink = sink;
        _diagnostics = diagnostics;
        _warning = warning;
        _availability = availability;
        _outputIds = mappings.OutputIds;
        _outputs = _outputIds.ToDictionary(x => x, _ => new OutputRuntime(), StringComparer.Ordinal);
    }

    public void SetGameplayActive(IEnumerable<string> activeStatuses)
    {
        lock (_sync)
        {
            _isGameplayActive = true;
            _activeStatuses = activeStatuses.ToHashSet(StringComparer.Ordinal);
            var batch = new List<PlaybackCommand>();
            if (!_isPaused)
            {
                ApplyFillersToIdleOutputs(batch);
            }

            Emit(batch);
        }
    }

    public void UpdateStatuses(IEnumerable<string> activeStatuses)
    {
        lock (_sync)
        {
            _activeStatuses = activeStatuses.ToHashSet(StringComparer.Ordinal);
            var batch = new List<PlaybackCommand>();
            if (_isGameplayActive && !_isPaused)
            {
                ApplyFillersToIdleOutputs(batch);
            }

            Emit(batch);
        }
    }

    public bool ObserveEvent(EventObservation observation, bool forceReplay = false)
    {
        lock (_sync)
        {
            if (!_mappings.TryResolve(observation.Key, out _))
            {
                _diagnostics.RecordEvent(observation);
                if (observation.Key.IsUnidentifiedActor)
                {
                    // Saying "author a funscript for it" here would be wrong advice: the trigger is
                    // shared by every unidentified binder, so authoring is refused (FR-060). What
                    // needs fixing is the identification, and this line is the evidence for it.
                    if (_warnedUnknownEvents.Add(observation.Key))
                    {
                        _warning?.Invoke(
                            $"Hold observed but its binder could not be identified ({observation.Key}). "
                            + "The trigger is catalogued so the hold is not lost, but no funscript can "
                            + "be authored for it, because it stands for every unidentified binder at "
                            + "once. Report this line with the scene and the enemy.");
                    }
                }
                else if (_mappings.Classify(observation.Key) == MappingDisposition.Unclassified
                    && _warnedUnknownEvents.Add(observation.Key))
                {
                    _warning?.Invoke(
                        $"Unregistered trigger {observation.Key}; playback was suppressed until a "
                        + "funscript is authored and saved for it.");
                }

                return false;
            }

            _isGameplayActive = true;
            _currentEvent = observation;
            var batch = new List<PlaybackCommand>();
            bool resolved = ObserveEventCore(observation, forceReplay, batch);
            Emit(batch);
            return resolved;
        }
    }

    public void EndEvent(EventKey key)
    {
        lock (_sync)
        {
            if (_currentEvent?.Key == key)
            {
                _currentEvent = null;
            }

            var batch = new List<PlaybackCommand>();
            foreach ((string output, OutputRuntime runtime) in _outputs)
            {
                if (runtime.ActiveEvent != key)
                {
                    continue;
                }

                runtime.ActiveEvent = null;
                if (_isPaused)
                {
                    runtime.Mode = OutputMode.Paused;
                    continue;
                }

                if (_isGameplayActive)
                {
                    ApplyFiller(output, runtime, batch);
                }
                else
                {
                    runtime.Mode = OutputMode.Inactive;
                    PublishIfChanged(output, PlaybackCommand.Stop(output), batch);
                }
            }

            Emit(batch);
        }
    }

    /// <summary>
    /// Starts an authoring preview. Preview ranks immediately below <c>Inactive</c> so the game's
    /// own playback cannot interrupt a check the user explicitly asked for (SPEC001 5.1, FR-037).
    /// </summary>
    public void BeginPreview(string gallery, IReadOnlyList<string> outputs)
    {
        if (string.IsNullOrWhiteSpace(gallery))
        {
            throw new ArgumentException("A preview requires a gallery name.", nameof(gallery));
        }

        string[] targets = outputs.Where(_outputIds.Contains).Distinct(StringComparer.Ordinal).ToArray();
        if (targets.Length == 0)
        {
            throw new ArgumentException("A preview requires at least one known output.", nameof(outputs));
        }

        // Auditioning on an output with no device behind it would silently do nothing, which is
        // exactly the failure the binding gate exists to make visible (SPEC001 7.1, FR-042).
        if (_availability is not null)
        {
            string[] unavailable = targets.Where(x => !_availability.IsAvailable(x)).ToArray();
            if (unavailable.Length > 0)
            {
                throw new ArgumentException(
                    $"EDI output(s) {string.Join(", ", unavailable)} are suppressed, so there is no "
                    + "device to preview on. Check the binding report, connect the device in "
                    + "Intiface Central, and restart the game.",
                    nameof(outputs));
            }
        }

        lock (_sync)
        {
            _isPreviewing = true;
            var batch = new List<PlaybackCommand>();
            foreach (string output in targets)
            {
                var runtime = _outputs[output];
                runtime.Mode = OutputMode.Preview;
                runtime.ActiveEvent = null;
                runtime.LastPublished = PlaybackCommand.Play(gallery, 0, output);
                batch.Add(runtime.LastPublished);
            }

            _sink.Publish(batch);
        }
    }

    /// <summary>Ends the preview and re-derives every output from the current game state.</summary>
    public void EndPreview()
    {
        lock (_sync)
        {
            if (!_isPreviewing)
            {
                return;
            }

            _isPreviewing = false;
            foreach (var runtime in _outputs.Values)
            {
                runtime.Mode = OutputMode.Inactive;
                runtime.ActiveEvent = null;

                // Force a republish: EDI is now playing preview content on these outputs, so the
                // pre-preview command is no longer an accurate record of the device state.
                runtime.LastPublished = null;
            }

            var batch = new List<PlaybackCommand>();
            if (!_isGameplayActive)
            {
                StopAll(batch);
                Emit(batch);
                return;
            }

            if (_isPaused)
            {
                foreach ((string output, OutputRuntime runtime) in _outputs)
                {
                    runtime.Mode = OutputMode.Paused;
                    PublishIfChanged(output, PlaybackCommand.Pause(output), batch);
                }

                Emit(batch);
                return;
            }

            if (_currentEvent is not null)
            {
                ObserveEventCore(_currentEvent, true, batch);
            }

            ApplyFillersToIdleOutputs(batch);
            Emit(batch);
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
            var batch = new List<PlaybackCommand>();
            foreach ((string output, OutputRuntime runtime) in _outputs)
            {
                runtime.Mode = OutputMode.Paused;
                PublishIfChanged(output, PlaybackCommand.Pause(output), batch);
            }

            Emit(batch);
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
            foreach (var runtime in _outputs.Values)
            {
                runtime.Mode = OutputMode.Filler;
                runtime.ActiveEvent = null;
            }

            _currentEvent = currentEvent;
            var batch = new List<PlaybackCommand>();
            if (_isGameplayActive)
            {
                if (currentEvent is not null)
                {
                    ObserveEventCore(currentEvent, true, batch);
                }

                ApplyFillersToIdleOutputs(batch);
            }
            else
            {
                StopAll(batch);
            }

            Emit(batch);
        }
    }

    public void SetInactive()
    {
        lock (_sync)
        {
            _isGameplayActive = false;
            _isPaused = false;
            _currentEvent = null;

            // Inactive outranks Preview, so leaving gameplay also ends an authoring preview.
            _isPreviewing = false;
            var batch = new List<PlaybackCommand>();
            StopAll(batch);
            Emit(batch);
        }
    }

    private bool ObserveEventCore(
        EventObservation observation,
        bool forceReplay,
        List<PlaybackCommand> batch)
    {
        _diagnostics.RecordEvent(observation);
        if (!_mappings.TryResolve(observation.Key, out var mapping))
        {
            return false;
        }

        long seek = CalculateSeek(mapping.SeekMode!, observation);
        foreach (OutputAssignment assignment in mapping.Outputs)
        {
            if (!_outputs.TryGetValue(assignment.Id, out OutputRuntime? runtime))
            {
                continue;
            }

            if (!forceReplay && runtime.ActiveEvent == observation.Key)
            {
                continue;
            }

            runtime.ActiveEvent = observation.Key;
            if (assignment.Gallery is null)
            {
                // The trigger deliberately leaves this device still. Expressed as a stop rather
                // than as a flat waveform so the two are distinguishable (FR-047).
                runtime.Mode = OutputMode.Silent;
                PublishIfChanged(assignment.Id, PlaybackCommand.Stop(assignment.Id), batch);
                continue;
            }

            runtime.Mode = OutputMode.Event;
            PublishIfChanged(
                assignment.Id,
                PlaybackCommand.Play(assignment.Gallery, seek, assignment.Id),
                batch);
        }

        return true;
    }

    /// <summary>
    /// Re-derives every output that is not currently owned by a trigger. Outputs a trigger holds
    /// are left alone, so a status change never interrupts a performance (FR-012).
    /// </summary>
    private void ApplyFillersToIdleOutputs(List<PlaybackCommand> batch)
    {
        foreach ((string output, OutputRuntime runtime) in _outputs)
        {
            if (runtime.ActiveEvent is not null)
            {
                continue;
            }

            ApplyFiller(output, runtime, batch);
        }
    }

    private void ApplyFiller(string output, OutputRuntime runtime, List<PlaybackCommand> batch)
    {
        string? filler = _mappings.SelectFiller(output, _activeStatuses);
        if (filler is null)
        {
            runtime.Mode = OutputMode.Silent;
            PublishIfChanged(output, PlaybackCommand.Stop(output), batch);
            return;
        }

        runtime.Mode = OutputMode.Filler;
        PublishIfChanged(output, PlaybackCommand.Play(filler, 0, output), batch);
    }

    private void StopAll(List<PlaybackCommand> batch)
    {
        foreach ((string output, OutputRuntime runtime) in _outputs)
        {
            runtime.Mode = OutputMode.Inactive;
            runtime.ActiveEvent = null;
            PublishIfChanged(output, PlaybackCommand.Stop(output), batch);
        }
    }

    private void PublishIfChanged(string output, PlaybackCommand command, List<PlaybackCommand> batch)
    {
        // Game-driven playback is suppressed while the user is auditioning a script. Internal
        // state still advances so EndPreview can restore the correct desired state.
        if (_isPreviewing)
        {
            return;
        }

        var runtime = _outputs[output];
        if (runtime.LastPublished == command)
        {
            return;
        }

        runtime.LastPublished = command;
        batch.Add(command);
    }

    private void Emit(List<PlaybackCommand> batch)
    {
        if (batch.Count > 0)
        {
            _sink.Publish(batch);
        }
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

    /// <summary>
    /// Per-output logical state (SPEC001 5.1). <c>Suppressed</c> is not listed here: it is owned
    /// by <see cref="OutputGate"/>, which holds commands for an output whose binding does not
    /// hold, so the coordinator's state stays a pure function of the game.
    /// </summary>
    private enum OutputMode
    {
        Inactive,
        Silent,
        Filler,
        Event,
        Paused,
        Preview,
    }

    private sealed class OutputRuntime
    {
        public OutputMode Mode { get; set; } = OutputMode.Inactive;

        /// <summary>
        /// The trigger currently owning this output, if any. Doubles as the identity that lets a
        /// stale end notification be rejected (SPEC001 4.3 property 2, FR-009).
        /// </summary>
        public EventKey? ActiveEvent { get; set; }

        public PlaybackCommand? LastPublished { get; set; }
    }
}
