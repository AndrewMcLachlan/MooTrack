namespace MooTrack.Agent;

/// <summary>
/// A source of observations. <see cref="Start"/> may be slow: it is never called on
/// the service start path, because a service that takes too long to report Started
/// is one the SCM kills.
/// </summary>
public interface ISignalSource : IDisposable
{
    event Action<SignalObserved, DateTimeOffset>? Observed;

    void Start();
}
