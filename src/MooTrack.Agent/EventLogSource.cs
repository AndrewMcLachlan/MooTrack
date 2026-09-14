using System.Diagnostics.Eventing.Reader;
using MooTrack.Derivation;

namespace MooTrack.Agent;

/// <summary>
/// Replays the Windows event log from the last recorded position, then follows it.
/// The log survives the agent being down, so this is what makes an outage recoverable.
/// </summary>
public sealed class EventLogSource(
    PositionStore bookmarks, ILogger<EventLogSource> logger) : ISignalSource
{
    private static readonly Dictionary<int, (ObservedEvent Event, string Detail)> Interesting = new()
    {
        [4800] = (ObservedEvent.Lock, "4800 workstation locked"),
        [4801] = (ObservedEvent.Unlock, "4801 workstation unlocked"),
        [6005] = (ObservedEvent.Resume, "6005 event log started"),
        [6006] = (ObservedEvent.Shutdown, "6006 event log stopping"),
        [6008] = (ObservedEvent.Shutdown, "6008 unexpected shutdown"),
    };

    private readonly List<EventLogWatcher> _watchers = [];

    public event Action<SignalObserved, DateTimeOffset>? Observed;

    public void Start()
    {
        Follow("Security", [4800, 4801]);
        Follow("System", [6005, 6006, 6008]);
    }

    private void Follow(string logName, int[] ids)
    {
        var selector = String.Join(" or ", ids.Select(id => $"EventID={id}"));
        var query = $"*[System[({selector})]]";

        try
        {
            Replay(logName, query);

            var watcher = new EventLogWatcher(new EventLogQuery(logName, PathType.LogName, query));
            watcher.EventRecordWritten += (_, e) =>
            {
                if (e.EventRecord is { } record) Handle(logName, record);
            };
            watcher.Enabled = true;
            _watchers.Add(watcher);
        }
        catch (Exception e) when (e is EventLogException or UnauthorizedAccessException)
        {
            logger.LogError(e, "cannot follow {Log}; continuing without it", logName);
        }
    }

    // With a bookmark, replay covers the agent's downtime. Without one this is a first
    // run: adopt the log's current position rather than importing its whole retained
    // history, which would back-fill months and duplicate on every reinstall.
    private void Replay(string logName, string query)
    {
        var since = bookmarks.LastRecordId(logName);

        using var reader = new EventLogReader(new EventLogQuery(logName, PathType.LogName, query));
        var replayed = 0;
        long? newest = null;

        while (reader.ReadEvent() is { } record)
        {
            using (record)
            {
                if (record.RecordId is { } id && (newest is null || id > newest)) newest = id;
                if (since is null) continue;
                if (record.RecordId <= since) continue;

                Handle(logName, record, persist: false);
                replayed++;
            }
        }

        if (since is null)
        {
            bookmarks.Save(logName, newest ?? 0);
            logger.LogInformation("adopted current position in {Log}", logName);
        }
        else if (replayed > 0)
        {
            logger.LogInformation("replayed {Count} from {Log}", replayed, logName);
        }
    }

    private void Handle(string logName, EventRecord record, bool persist = true)
    {
        try
        {
            if (record.Id is var id && !Interesting.TryGetValue(id, out var mapped)) return;

            var at = record.TimeCreated is { } created
                ? new DateTimeOffset(created)
                : DateTimeOffset.Now;

            Observed?.Invoke(new SignalObserved(mapped.Event, "EventLog", mapped.Detail), at);

            if (record.RecordId is { } recordId) bookmarks.Save(logName, recordId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to handle a {Log} record", logName);
        }
        finally
        {
            if (persist) record.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.Enabled = false;
                watcher.Dispose();
            }
            catch (EventLogException)
            {
            }
        }
    }
}
