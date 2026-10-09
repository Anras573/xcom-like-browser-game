namespace Firewall.Rules.Rng;

/// <summary>
/// Deterministic random source. Rules code takes one of these and never touches
/// <see cref="System.Random"/> or the clock.
/// </summary>
public interface IRandom
{
    /// <summary>Uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    int Next(int minInclusive, int maxExclusive);

    /// <summary>Uniform double in [0, 1).</summary>
    double NextDouble();

    /// <summary>
    /// Derives an independent stream from this generator's seed and <paramref name="label"/>.
    /// The result does not depend on how many numbers were already drawn, so adding a roll in one
    /// subsystem never shifts the rolls of another.
    /// </summary>
    IRandom Fork(string label);

    /// <summary>True with probability <paramref name="percent"/> / 100.</summary>
    bool Roll(int percent) => Next(0, 100) < percent;
}
