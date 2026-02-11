namespace EvidenceFoundry.Helpers;

public sealed class ThreadSafeRandom : Random
{
    private readonly Random _inner;
    private readonly object _lock = new();

    public ThreadSafeRandom(int seed)
    {
        _inner = new Random(seed);
    }

    public override int Next()
    {
        lock (_lock)
        {
            return _inner.Next();
        }
    }

    public override int Next(int maxValue)
    {
        lock (_lock)
        {
            return _inner.Next(maxValue);
        }
    }

    public override int Next(int minValue, int maxValue)
    {
        lock (_lock)
        {
            return _inner.Next(minValue, maxValue);
        }
    }

    public override double NextDouble()
    {
        lock (_lock)
        {
            return _inner.NextDouble();
        }
    }

    public override void NextBytes(byte[] buffer)
    {
        lock (_lock)
        {
            _inner.NextBytes(buffer);
        }
    }

    public override void NextBytes(Span<byte> buffer)
    {
        lock (_lock)
        {
            _inner.NextBytes(buffer);
        }
    }
}
