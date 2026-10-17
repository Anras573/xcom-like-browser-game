using Firewall.Rules.Data;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Tactical;

/// <summary>
/// Stand-in for the barracks roster: rookies with the starting rifle and random names. The real
/// roster replaces it with the barracks work.
/// </summary>
public static class DebugSquad
{
    public static IReadOnlyList<TacticalUnit> Create(
        TacticalState state,
        GameData data,
        IRandom rng,
        int count = 4
    )
    {
        var soldier = data.Soldier;
        var weapon = data.Weapon(soldier.StartingWeaponClass, 1);
        var names = rng.Fork("squad-names");
        var looks = rng.Fork("squad-looks");
        var squad = new List<TacticalUnit>();
        for (var i = 0; i < count; i++)
        {
            var name = $"{Pick(data.Names.FirstNames, names)} {Pick(data.Names.LastNames, names)}";
            squad.Add(
                new TacticalUnit(
                    state.NextUnitId(),
                    Faction.Firewall,
                    SquadDeployer.SoldierDefId,
                    name
                )
                {
                    Hp = soldier.Hp,
                    MaxHp = soldier.Hp,
                    Aim = soldier.Aim,
                    Mobility = soldier.Mobility,
                    Defense = soldier.Defense,
                    Sight = soldier.Sight,
                    ActionPoints = 2,
                    Weapon = new WeaponState(weapon, weapon.Clip),
                    AppearanceId = Pick(soldier.AppearanceSets, looks),
                    Abilities = { AbilityRegistry.Shoot },
                }
            );
        }
        return squad;
    }

    private static string Pick(IReadOnlyList<string> list, IRandom rng) =>
        list[rng.Next(0, list.Count)];
}
