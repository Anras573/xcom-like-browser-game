using System.Text.Json.Serialization;

namespace Firewall.Rules.Data;

/// <summary>
/// One entry of a weapon's range table. A band with <see cref="MaxDist"/> applies up to and
/// including that distance (first match wins). A band with <see cref="PerTileBeyond"/> adds
/// <see cref="Mod"/> for every tile past that distance. A band with neither applies at any distance.
/// </summary>
public sealed record RangeBand
{
    public int? MaxDist { get; init; }
    public int? PerTileBeyond { get; init; }

    [JsonRequired]
    public int Mod { get; init; }
}

public sealed record WeaponDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public WeaponClass Class { get; init; }

    [JsonRequired]
    public int Tier { get; init; }

    [JsonRequired]
    public int DmgMin { get; init; }

    [JsonRequired]
    public int DmgMax { get; init; }

    [JsonRequired]
    public int Clip { get; init; }

    [JsonRequired]
    public int Crit { get; init; }

    [JsonRequired]
    private readonly IReadOnlyList<RangeBand>? _range;
    public IReadOnlyList<RangeBand> Range
    {
        get => _range ?? [];
        init => _range = value;
    }
    private readonly IReadOnlyList<WeaponFlag>? _flags;
    public IReadOnlyList<WeaponFlag> Flags
    {
        get => _flags ?? [];
        init => _flags = value;
    }

    /// <summary>
    /// Aim modifier for a shot at <paramref name="distanceTiles"/> (Euclidean, in tiles). Per-tile
    /// bands count each started tile past their threshold.
    /// </summary>
    public int RangeMod(double distanceTiles)
    {
        var total = 0;
        var banded = false;
        foreach (var band in Range)
        {
            if (band.PerTileBeyond is { } beyond)
            {
                if (distanceTiles > beyond)
                    total += band.Mod * (int)Math.Ceiling(distanceTiles - beyond);
            }
            else if (band.MaxDist is { } max)
            {
                if (!banded && distanceTiles <= max)
                {
                    total += band.Mod;
                    banded = true;
                }
            }
            else
                total += band.Mod;
        }
        return total;
    }
}
