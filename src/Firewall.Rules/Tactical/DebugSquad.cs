using Firewall.Rules.Data;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Tactical;

/// <summary>
/// Stand-in for the barracks roster: rookies with the starting rifle by default, or the soldiers of
/// the debug squad builder (#30), with random names. The real
/// roster replaces it with the barracks work.
/// </summary>
public static class DebugSquad
{
    /// <summary>The utility item each default rookie carries, in squad order: a bit of everything to try.</summary>
    public static IReadOnlyList<string> DefaultItems { get; } =
    [
        AbilityRegistry.Medkit,
        AbilityRegistry.FragGrenade,
        AbilityRegistry.ArcStunner,
        AbilityRegistry.BreachCharge,
    ];

    public static IReadOnlyList<TacticalUnit> Create(
        TacticalState state,
        GameData data,
        IRandom rng,
        int count = 4
    ) =>
        Create(
            state,
            data,
            rng,
            Enumerable
                .Range(0, count)
                .Select(i => new SoldierSpec { ItemIds = [DefaultItems[i % DefaultItems.Count]] })
                .ToList()
        );

    /// <summary>One soldier per spec (the squad builder's choices), with random names and looks.</summary>
    public static IReadOnlyList<TacticalUnit> Create(
        TacticalState state,
        GameData data,
        IRandom rng,
        IReadOnlyList<SoldierSpec> specs
    )
    {
        var names = rng.Fork("squad-names");
        var looks = rng.Fork("squad-looks");
        var squad = new List<TacticalUnit>();
        foreach (var spec in specs)
        {
            var name = $"{Pick(data.Names.FirstNames, names)} {Pick(data.Names.LastNames, names)}";
            squad.Add(
                SoldierBuilder.Build(
                    state,
                    data,
                    spec,
                    name,
                    Pick(data.Soldier.AppearanceSets, looks)
                )
            );
        }
        return squad;
    }

    private static string Pick(IReadOnlyList<string> list, IRandom rng) =>
        list[rng.Next(0, list.Count)];
}
