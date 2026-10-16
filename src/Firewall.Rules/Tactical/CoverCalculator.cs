namespace Firewall.Rules.Tactical;

/// <summary>The cover a tile gives in each cardinal direction, from its four neighbours.</summary>
public readonly record struct CoverProfile(
    CoverLevel North,
    CoverLevel East,
    CoverLevel South,
    CoverLevel West
)
{
    public static readonly CoverProfile None = default;

    public bool HasAny =>
        North != CoverLevel.None
        || East != CoverLevel.None
        || South != CoverLevel.None
        || West != CoverLevel.None;
}

/// <summary>Cover against one attacker (GDD §4.3).</summary>
/// <param name="DefenseBonus">0, 20 or 40. Hunker Down doubles it where it is applied.</param>
public readonly record struct CoverResult(CoverLevel Level, bool Flanked, int DefenseBonus);

/// <summary>Directional cover (GDD §4.3). Pure and stateless.</summary>
public static class CoverCalculator
{
    public const int HalfDefense = 20;
    public const int FullDefense = 40;

    /// <summary>cos 60°: cover applies when the attacker is within 60° of its direction.</summary>
    private const double Threshold = 0.5;

    private const double Epsilon = 1e-9;

    /// <summary>Cover in N, E, S and W from the adjacent tiles; outside the map counts as none.</summary>
    public static CoverProfile GetProfile(TacticalMap map, GridPos tile) =>
        new(
            map.CoverAt(new GridPos(tile.X, tile.Y + 1)),
            map.CoverAt(new GridPos(tile.X + 1, tile.Y)),
            map.CoverAt(new GridPos(tile.X, tile.Y - 1)),
            map.CoverAt(new GridPos(tile.X - 1, tile.Y))
        );

    /// <summary>Cover <paramref name="target"/> has against a shot from <paramref name="attacker"/>.</summary>
    public static CoverResult Evaluate(TacticalMap map, GridPos target, GridPos attacker) =>
        Evaluate(GetProfile(map, target), attacker.X - target.X, attacker.Y - target.Y);

    /// <summary>
    /// Cover for an attacker at offset (<paramref name="dx"/>, <paramref name="dy"/>) from the target
    /// (+Y up). An offset of zero is flanked, though it should not happen.
    /// </summary>
    public static CoverResult Evaluate(CoverProfile profile, double dx, double dy)
    {
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0)
            return new CoverResult(CoverLevel.None, true, 0);

        var level = CoverLevel.None;
        level = Best(level, profile.North, dy / length);
        level = Best(level, profile.East, dx / length);
        level = Best(level, profile.South, -dy / length);
        level = Best(level, profile.West, -dx / length);
        return new CoverResult(level, level == CoverLevel.None, Defense(level));
    }

    /// <summary>Defense granted by a cover level: 0, 20 or 40.</summary>
    public static int Defense(CoverLevel level) =>
        level switch
        {
            CoverLevel.Half => HalfDefense,
            CoverLevel.Full => FullDefense,
            _ => 0,
        };

    // dot is the cosine between the attacker's direction and this cover's direction.
    private static CoverLevel Best(CoverLevel best, CoverLevel cover, double dot) =>
        cover > best && dot >= Threshold - Epsilon ? cover : best;

    /// <summary>
    /// True when at least one living hostile unit (pod members count before their pod activates: the player plans cover against what they see) that <paramref name="isVisible"/> accepts
    /// would flank <paramref name="tile"/>. Neutrals are never hostile. Until fog of war (#21) lands
    /// the default counts every hostile unit as visible.
    /// </summary>
    public static bool IsFlankedByAny(
        TacticalState state,
        GridPos tile,
        Faction faction,
        Func<TacticalUnit, bool>? isVisible = null
    ) => MostThreatening(state, tile, faction, isVisible) is { Flanked: true };

    /// <summary>
    /// The result against the most threatening hostile unit (flanked beats any cover; otherwise
    /// the lowest cover level), or null when there is none.
    /// </summary>
    public static CoverResult? MostThreatening(
        TacticalState state,
        GridPos tile,
        Faction faction,
        Func<TacticalUnit, bool>? isVisible = null
    )
    {
        var profile = GetProfile(state.Map, tile);
        CoverResult? worst = null;
        foreach (var unit in state.Units)
        {
            if (
                !unit.IsAlive
                || !IsHostile(faction, unit.Faction)
                || (isVisible is not null && !isVisible(unit))
            )
                continue;
            var result = Evaluate(profile, unit.Position.X - tile.X, unit.Position.Y - tile.Y);
            if (worst is null || result.Level < worst.Value.Level)
                worst = result;
        }
        return worst;
    }

    private static bool IsHostile(Faction a, Faction b) =>
        a != b && a != Faction.Neutral && b != Faction.Neutral;
}
