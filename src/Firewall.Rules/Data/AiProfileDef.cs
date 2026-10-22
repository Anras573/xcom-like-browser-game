using System.Text.Json.Serialization;

namespace Firewall.Rules.Data;

/// <summary>
/// Weights for the utility AI (GDD §4.9), from <c>data/ai_profiles.json</c>. An enemy picks its profile
/// from <see cref="BehaviorDef.Kind"/>; its <see cref="BehaviorDef.CoverWeight"/> and
/// <see cref="BehaviorDef.FlankWeight"/> multiply the profile's, and a preferred range set on the enemy
/// replaces the profile's.
/// </summary>
public sealed record AiProfileDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public AiKind Kind { get; init; }

    /// <summary>Weight of the cover a tile gives against the soldiers who can see it (0 to 1).</summary>
    public double CoverWeight { get; init; }

    /// <summary>Bonus for a tile that flanks a soldier it can shoot.</summary>
    public double FlankWeight { get; init; }

    /// <summary>Penalty per soldier that would flank the tile.</summary>
    public double ExposedWeight { get; init; }

    /// <summary>Penalty per tile outside the preferred range band.</summary>
    public double RangeWeight { get; init; }
    public int PreferredRangeMin { get; init; }
    public int PreferredRangeMax { get; init; }

    /// <summary>Bonus for staying near robot allies (0 to 1: how many are within <see cref="AllyRange"/>).</summary>
    public double AllyWeight { get; init; }
    public int AllyRange { get; init; }

    /// <summary>Penalty per soldier on overwatch that sees the route to the tile.</summary>
    public double OverwatchAvoidWeight { get; init; }

    /// <summary>Upper bound of the seeded noise added to a tile's score, before the difficulty scales it.</summary>
    public double Noise { get; init; }

    /// <summary>Weight of a shot's expected damage.</summary>
    [JsonRequired]
    public double DamageWeight { get; init; }

    /// <summary>Added per unit of kill chance.</summary>
    [JsonRequired]
    public double KillBonus { get; init; }

    /// <summary>Penalty per AP spent moving, so a unit that is fine where it stands stays.</summary>
    [JsonRequired]
    public double MoveCost { get; init; }

    /// <summary>Considers dashing even when a 1 AP tile offers a shot.</summary>
    public bool AlwaysDash { get; init; }

    /// <summary>Targets civilians within this many tiles before soldiers (0 = never).</summary>
    public int CivilianRadius { get; init; }

    /// <summary>Overwatches instead of acting when no shot has at least this hit chance (0 = never).</summary>
    public int OverwatchBelowHit { get; init; }

    /// <summary>What overwatch is worth when it is chosen.</summary>
    public double OverwatchValue { get; init; }

    /// <summary>Holds its tile (and shoots from it) until a soldier flanks it.</summary>
    public bool HoldUnlessFlanked { get; init; }

    /// <summary>Value of suppressing, times the best hit chance an ally has on the target.</summary>
    public double SuppressWeight { get; init; }

    /// <summary>Value of boosting an adjacent robot ally that has used its AP and has a shot.</summary>
    public double SupportWeight { get; init; }
}
