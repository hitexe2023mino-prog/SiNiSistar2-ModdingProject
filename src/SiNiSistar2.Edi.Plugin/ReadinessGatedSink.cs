using SiNiSistar2.Edi.Core;

namespace SiNiSistar2.Edi.Plugin;

internal sealed class ReadinessGatedSink : IPlaybackCommandSink
{
    private readonly object _sync = new();
    private readonly IPlaybackCommandSink _inner;
    private readonly Dictionary<string, PlaybackCommand> _latest = new(StringComparer.Ordinal);
    private bool _ready;

    public ReadinessGatedSink(IPlaybackCommandSink inner) => _inner = inner;

    public void Publish(PlaybackCommand command)
    {
        lock (_sync)
        {
            _latest[command.Channel] = command;
            if (_ready)
            {
                _inner.Publish(command);
            }
        }
    }

    public void SetReady()
    {
        lock (_sync)
        {
            if (_ready)
            {
                return;
            }

            _ready = true;
            foreach (PlaybackCommand command in _latest.Values)
            {
                _inner.Publish(command);
            }
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        _inner.ShutdownAsync(cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
