using System.Collections.Concurrent;

namespace Gateway.Services;

public class GatewayHandshakeTracker
{
    private readonly ConcurrentQueue<string> _subjects = new();
    private readonly int _maxSubjects;
    private long _handshakeCount;

    public GatewayHandshakeTracker(int maxSubjects = 10)
    {
        _maxSubjects = maxSubjects;
    }

    public long TotalHandshakes => Interlocked.Read(ref _handshakeCount);

    public IReadOnlyCollection<string> RecentSubjects => _subjects.ToArray();

    public void Record(string subject)
    {
        Interlocked.Increment(ref _handshakeCount);
        _subjects.Enqueue(subject);

        while (_subjects.Count > _maxSubjects && _subjects.TryDequeue(out _))
        {
        }
    }
}
