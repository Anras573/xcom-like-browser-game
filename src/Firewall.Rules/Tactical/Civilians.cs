namespace Firewall.Rules.Tactical;

/// <summary>Rescue mission civilians (GDD §4.10): neutral, unarmed, and theirs to save.</summary>
public static class Civilians
{
    public const string DefId = "civilian";
    public const int Hp = 3;
    public const int Mobility = 6;

    /// <summary>Sprite sets a civilian can wear (GDD §3).</summary>
    public static readonly IReadOnlyList<string> Appearances = ["manOld", "womanGreen"];

    public static bool IsCivilian(TacticalUnit u) =>
        u is { Faction: Faction.Neutral, DefId: DefId };

    public static int Saved(TacticalState state) =>
        state.Units.Count(u => IsCivilian(u) && u is { IsAlive: true, Evacuated: true });

    public static int Lost(TacticalState state) =>
        state.Units.Count(u => IsCivilian(u) && !u.IsAlive);

    /// <summary>Alive and still on the map (or being carried).</summary>
    public static int Remaining(TacticalState state) =>
        state.Units.Count(u => IsCivilian(u) && u is { IsAlive: true, Evacuated: false });

    /// <summary>A civilian that can be moved and evacuated by the player.</summary>
    public static TacticalUnit Create(TacticalState state, string appearance, string name)
    {
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Neutral, DefId, name)
        {
            Hp = Hp,
            MaxHp = Hp,
            Mobility = Mobility,
            AppearanceId = appearance,
            IsActive = true,
        };
        unit.Abilities.Add(AbilityRegistry.Evac);
        return unit;
    }
}
