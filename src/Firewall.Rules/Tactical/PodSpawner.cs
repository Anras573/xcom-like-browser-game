using Firewall.Rules.Data;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Tactical;

/// <summary>Places enemy pods on the map's <c>e</c> tiles.</summary>
public static class PodSpawner
{
    /// <summary>
    /// Spawns one pod per spec on a distinct <c>e</c> tile (which tile each pod gets is shuffled
    /// with a fork of <paramref name="rng"/>). The first member stands on the spawn tile and the
    /// rest on its nearest free walkable neighbours. Pods start inactive.
    /// </summary>
    public static IReadOnlyList<Pod> Spawn(
        TacticalState state,
        GameData data,
        IReadOnlyList<PodSpec> pods,
        IRandom rng
    )
    {
        var spawns = state.Map.EnemySpawns.ToList();
        if (pods.Count > spawns.Count)
            throw new InvalidOperationException(
                $"Map '{state.Map.Id}' has {spawns.Count} enemy spawns for {pods.Count} pods."
            );

        var shuffle = rng.Fork("pod-spawns");
        for (var i = spawns.Count - 1; i > 0; i--)
        {
            var j = shuffle.Next(0, i + 1);
            (spawns[i], spawns[j]) = (spawns[j], spawns[i]);
        }

        var created = new List<Pod>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var p = 0; p < pods.Count; p++)
        {
            var spec = pods[p];
            var spawn = spawns[p];
            var podId = state.AddPod(spec.Name, spawn);
            created.Add(state.Pods[^1]);

            var free = state.NearestFreeTiles(spawn).Take(spec.EnemyIds.Count).ToList();
            if (free.Count < spec.EnemyIds.Count)
                throw new InvalidOperationException($"No room for pod '{spec.Name}'.");

            for (var m = 0; m < spec.EnemyIds.Count; m++)
            {
                var def = data.Enemies[spec.EnemyIds[m]];
                counts[def.Id] = counts.GetValueOrDefault(def.Id) + 1;
                var unit = CreateUnit(state, def, $"{def.Name} {(char)('A' + counts[def.Id] - 1)}");
                unit.Position = free[m];
                unit.IsActive = false;
                unit.Pod = podId;
                state.AddUnit(unit);
                created[^1].Members.Add(unit.Id);
            }
        }
        return created;
    }

    /// <summary>
    /// Puts an object enemy (the Relay Node) on every objective (<c>O</c>) tile of the map. Objects
    /// are not in a pod and are active from the start.
    /// </summary>
    public static IReadOnlyList<TacticalUnit> SpawnObjects(
        TacticalState state,
        GameData data,
        string enemyId
    )
    {
        var def = data.Enemies[enemyId];
        var created = new List<TacticalUnit>();
        foreach (var tile in state.Map.Objectives)
        {
            var unit = CreateUnit(state, def, def.Name);
            unit.Position = tile;
            state.AddUnit(unit);
            created.Add(unit);
        }
        return created;
    }

    /// <summary>A new enemy unit for <paramref name="def"/>, not yet added to the state or placed on the map.</summary>
    public static TacticalUnit CreateUnit(TacticalState state, EnemyDef def, string name)
    {
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Meridian, def.Id, name)
        {
            Hp = def.Hp,
            MaxHp = def.Hp,
            Aim = def.Aim,
            Mobility = def.Mobility,
            Defense = def.Defense,
            Armor = def.Armor,
            Sight = def.Sight,
            AppearanceId = def.Sprite.Set,
            ImmuneToStun = def.ImmuneToStun,
        };
        foreach (var ability in def.Abilities)
            unit.Abilities.Add(ability);
        return unit;
    }
}
