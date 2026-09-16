using Crapnet.Application.Abstractions;

namespace Crapnet.Application.Engine;

/// <summary>
/// Holds delayed packets and hands them back the moment they come due.
/// </summary>
/// <remarks>
/// Every delay-shaped impairment ends up here, so this is what actually produces lag, jitter,
/// reordering, bandwidth queueing and throttle bursts.
///
/// Entries are keyed on (release time, arrival sequence). The sequence number is not decoration:
/// <see cref="PriorityQueue{TElement,TPriority}"/> is not a stable heap, so without it two packets
/// sharing a release millisecond could swap places and manufacture reordering that no rule asked
/// for.
/// </remarks>
public sealed class PacketScheduler : IDisposable
{
    private readonly record struct Entry(CapturedPacket Packet, int ExtraCopies);

    private readonly PriorityQueue<Entry, (long ReleaseAt, long Sequence)> _queue = new();
    private readonly object _gate = new();
    private readonly IClock _clock;
    private readonly Action<CapturedPacket, int> _release;
    private readonly Action<CapturedPacket> _discard;

    private Thread? _thread;
    private long _sequence;
    private bool _running;

    /// <summary>
    /// Ceiling on held packets. A deep queue is the point of this class, but an unbounded one
    /// would turn a mistyped delay into an out-of-memory crash, so past this point we shed load
    /// exactly as an overfull device buffer would.
    /// </summary>
    public int Capacity { get; init; } = 32_768;

    public PacketScheduler(IClock clock, Action<CapturedPacket, int> release, Action<CapturedPacket> discard)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _discard = discard ?? throw new ArgumentNullException(nameof(discard));
    }

    public int Count
    {
        get { lock (_gate) return _queue.Count; }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
        }

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "crapnet-scheduler",
            // Releases are time-critical: being late here shows up directly as jitter the user
            // did not configure.
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    /// <summary>Queues a packet for release. Returns false when the queue is full and it must be shed.</summary>
    public bool TrySchedule(CapturedPacket packet, long releaseAtMilliseconds, int extraCopies)
    {
        lock (_gate)
        {
            if (!_running || _queue.Count >= Capacity) return false;

            _queue.Enqueue(new Entry(packet, extraCopies), (releaseAtMilliseconds, _sequence++));

            // Only the head matters for wake-up timing, so nudge the thread whenever the packet
            // we just queued became the new head.
            Monitor.Pulse(_gate);
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            Monitor.PulseAll(_gate);
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;

        DrainRemaining();
    }

    private void Run()
    {
        var due = new List<Entry>(64);

        while (true)
        {
            due.Clear();

            lock (_gate)
            {
                while (_running)
                {
                    if (_queue.Count == 0)
                    {
                        Monitor.Wait(_gate, 250);
                        continue;
                    }

                    _queue.TryPeek(out _, out var head);
                    var remaining = head.ReleaseAt - _clock.ElapsedMilliseconds;
                    if (remaining > 0)
                    {
                        // Cap the wait so a stop request is never more than a moment away.
                        Monitor.Wait(_gate, (int)Math.Min(remaining, 250));
                        continue;
                    }

                    var now = _clock.ElapsedMilliseconds;
                    while (_queue.Count > 0)
                    {
                        _queue.TryPeek(out _, out var next);
                        if (next.ReleaseAt > now) break;
                        due.Add(_queue.Dequeue());
                    }

                    break;
                }

                if (!_running) return;
            }

            // Sending can block, so it happens outside the lock: holding it here would stall
            // every capture thread trying to queue new work.
            foreach (var entry in due) _release(entry.Packet, entry.ExtraCopies);
        }
    }

    private void DrainRemaining()
    {
        List<Entry> leftovers;
        lock (_gate)
        {
            leftovers = new List<Entry>(_queue.Count);
            while (_queue.Count > 0) leftovers.Add(_queue.Dequeue());
        }

        // Packets still in hand when the engine stops are returned to the pool rather than sent:
        // releasing a burst of stale traffic on shutdown would confuse whatever is being tested.
        foreach (var entry in leftovers) _discard(entry.Packet);
    }

    public void Dispose() => Stop();
}
