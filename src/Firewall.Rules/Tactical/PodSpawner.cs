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
                var unit = new TacticalUnit(
                    state.NextUnitId(),
                    Faction.Meridian,
                    def.Id,
                    $"{def.Name} {(char)('A' + counts[def.Id] - 1)}"
                )
                {
                    Position = free[m],
                    Hp = def.Hp,
                    MaxHp = def.Hp,
                    Aim = def.Aim,
                    Mobility = def.Mobility,
                    Defense = def.Defense,
                    Armor = def.Armor,
                    Sight = def.Sight,
                    IsActive = false,
                    Pod = podId,
                    AppearanceId = def.Sprite.Set,
                };
                unit.Statuses.Set(StatusFlags.Capturable, def.Capturable);
                foreach (var ability in def.Abilities)
                    unit.Abilities.Add(ability);
                state.AddUnit(unit);
            }
        }
        return created;
    }
}
