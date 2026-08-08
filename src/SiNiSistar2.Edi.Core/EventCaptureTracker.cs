namespace SiNiSistar2.Edi.Core;

public sealed class EventCaptureTracker
{
    private readonly IEventDiagnostics _diagnostics;
    private EventObservation? _current;

    public EventCaptureTracker(IEventDiagnostics diagnostics) => _diagnostics = diagnostics;

    public bool Observe(EventObservation? observation)
    {
        bool created = false;
        EventObservation? previous = _current;
        _current = observation;

        if (observation is not null)
        {
            created |= _diagnostics.RecordEvent(observation);
            if (previous is null || previous.Key != observation.Key)
            {
                created |= _diagnostics.RecordEvent(WithPhase(observation, "start"));
            }
        }

        if (previous is not null && (observation is null || previous.Key != observation.Key))
        {
            created |= _diagnostics.RecordEvent(WithPhase(previous, "end"));
        }

        return created;
    }

    private static EventObservation WithPhase(EventObservation observation, string phase) =>
        observation with
        {
            Key = observation.Key with { Phase = phase },
            ObservedAt = DateTimeOffset.UtcNow,
        };
}
