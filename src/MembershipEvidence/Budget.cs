using System.Diagnostics;

namespace MembershipEvidence;

internal sealed class Budget(Limits limits, CancellationToken token)
{
    private readonly long started = Stopwatch.GetTimestamp();
    internal int Requests { get; private set; }
    internal CancellationToken Token => token;
    internal void Check()
    {
        if (token.IsCancellationRequested) throw new ObservationFailure("CANCELLED");
        if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= limits.Milliseconds)
            throw new ObservationFailure("DEADLINE");
    }
    internal int RemainingMilliseconds
    {
        get
        {
            Check();
            return Math.Max(1, (int)Math.Floor(limits.Milliseconds - Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        }
    }
    internal void Request()
    {
        Check();
        if (Requests == limits.Requests) throw new ObservationFailure("REQUEST_CAP");
        Requests++;
    }
}
