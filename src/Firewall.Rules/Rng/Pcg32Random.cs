using System.Text;

namespace Firewall.Rules.Rng;

/// <summary>Complete, serializable state of a <see cref="Pcg32Random"/>.</summary>
public readonly record struct Pcg32State(ulong Seed, ulong Stream, ulong State);

/// <summary>
/// PCG-XSH-RR 32 generator (O'Neill). Implemented here rather than using <see cref="System.Random"/>,
/// whose sequence is not guaranteed across runtimes.
/// </summary>
public sealed class Pcg32Random : IRandom
{
    private const ulong Multiplier = 6364136223846793005UL;

    private readonly ulong _seed;
    private readonly ulong _stream;
    private readonly ulong _inc;
    private ulong _state;

    public Pcg32Random(ulong seed, ulong stream = 54UL)
    {
        _seed = seed;
        _stream = stream;
        _inc = (stream << 1) | 1UL;
        _state = 0;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    private Pcg32Random(Pcg32State s)
    {
        _seed = s.Seed;
        _stream = s.Stream;
        _inc = (s.Stream << 1) | 1UL;
        _state = s.State;
    }

    /// <summary>Snapshot to store in a save.</summary>
    public Pcg32State GetState() => new(_seed, _stream, _state);

    /// <summary>Restores a generator that continues exactly where the snapshot was taken.</summary>
    public static Pcg32Random FromState(Pcg32State state) => new(state);

    public uint NextUInt()
    {
        var old = _state;
        _state = unchecked(old * Multiplier + _inc);
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }

    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                "maxExclusive must be greater than minInclusive."
            );
        var range = (uint)((long)maxExclusive - minInclusive);
        // Rejection sampling removes modulo bias.
        var threshold = (0u - range) % range;
        while (true)
        {
            var r = NextUInt();
            if (r >= threshold)
                return (int)(minInclusive + (long)(r % range));
        }
    }

    public double NextDouble()
    {
        var hi = NextUInt() >> 5; // 27 bits
        var lo = NextUInt() >> 6; // 26 bits
        return (hi * 67108864.0 + lo) / 9007199254740992.0;
    }

    public IRandom Fork(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var childSeed = SplitMix(Fnv1A(label) ^ SplitMix(_seed));
        var childStream = SplitMix(childSeed + 0x9E3779B97F4A7C15UL);
        return new Pcg32Random(childSeed, childStream);
    }

    private static ulong Fnv1A(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(text))
            hash = unchecked((hash ^ b) * 1099511628211UL);
        return hash;
    }

    private static ulong SplitMix(ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
