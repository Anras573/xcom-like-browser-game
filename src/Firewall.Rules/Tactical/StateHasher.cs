using System.Text;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Tactical;

/// <summary>FNV-1a over every field of a <see cref="TacticalState"/>; collections go in sorted order.</summary>
internal static class StateHasher
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Hash(TacticalState state)
    {
        var h = Offset;
        Add(ref h, state.Map.Id);
        Add(ref h, state.Map.Width);
        Add(ref h, state.Map.Height);
        for (var y = 0; y < state.Map.Height; y++)
        for (var x = 0; x < state.Map.Width; x++)
            Add(ref h, (int)state.Map[new GridPos(x, y)]);

        Add(ref h, state.TurnNumber);
        Add(ref h, state.Convergence);
        Add(ref h, (int)state.Phase);
        Add(ref h, state.Result is { } r ? (int)r.Outcome : -1);
        Add(ref h, state.Objective.MissionId);
        Add(ref h, state.Objective.Progress);
        Add(ref h, state.Objective.Target);
        Add(ref h, state.Objective.Complete);
        if (state.Rng is Pcg32Random pcg)
        {
            var s = pcg.GetState();
            Add(ref h, s.Seed);
            Add(ref h, s.Stream);
            Add(ref h, s.State);
        }

        Add(ref h, state.Smoke.Count);
        foreach (var zone in state.Smoke)
        {
            Add(ref h, zone.TurnsLeft);
            Add(ref h, zone.Defense);
            foreach (var tile in zone.Tiles)
            {
                Add(ref h, tile.X);
                Add(ref h, tile.Y);
            }
        }

        Add(ref h, state.Pods.Count);
        foreach (var pod in state.Pods)
        {
            Add(ref h, pod.Id.Value);
            Add(ref h, pod.Name);
            Add(ref h, pod.Spawn.X);
            Add(ref h, pod.Spawn.Y);
            Add(ref h, pod.Active);
            Add(ref h, pod.PatrolTarget.X);
            Add(ref h, pod.PatrolTarget.Y);
            Add(ref h, pod.Members.Count);
            foreach (var member in pod.Members)
                Add(ref h, member.Value);
        }

        Add(ref h, state.Units.Count);
        foreach (var u in state.Units)
        {
            Add(ref h, u.Id.Value);
            Add(ref h, (int)u.Faction);
            Add(ref h, u.DefId);
            Add(ref h, u.DisplayName);
            Add(ref h, (int)u.Rank);
            Add(ref h, u.Position.X);
            Add(ref h, u.Position.Y);
            Add(ref h, u.Hp);
            Add(ref h, u.MaxHp);
            Add(ref h, u.Aim);
            Add(ref h, u.Mobility);
            Add(ref h, u.Defense);
            Add(ref h, u.Armor);
            Add(ref h, u.Sight);
            Add(ref h, u.ActionPoints);
            Add(ref h, u.MovedThisTurn);
            Add(ref h, u.Weapon is null ? "" : u.Weapon.Def.Id);
            Add(ref h, u.Weapon?.Ammo ?? -1);
            Add(ref h, u.Items.Count);
            foreach (var item in u.Items)
            {
                Add(ref h, item.ItemId);
                Add(ref h, item.Charges);
            }
            Add(ref h, u.Abilities.Count);
            foreach (var a in u.Abilities.Order(StringComparer.Ordinal))
                Add(ref h, a);
            Add(ref h, u.Cooldowns.Count);
            foreach (var (k, v) in u.Cooldowns.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                Add(ref h, k);
                Add(ref h, v);
            }
            Add(ref h, (int)u.Statuses.Flags);
            Add(ref h, u.Statuses.BleedingOutTurnsLeft);
            Add(ref h, u.Reactions.Count);
            foreach (var t in u.Reactions)
            {
                Add(ref h, t.Source);
                Add(ref h, (int)t.Status);
                Add(ref h, t.Only?.Value ?? -1);
                Add(ref h, t.ShotsLeft);
                Add(ref h, t.FiredAt.Count);
                foreach (var id in t.FiredAt.Select(i => i.Value).Order())
                    Add(ref h, id);
            }
            Add(ref h, u.Perks.DashedThisTurn);
            Add(ref h, u.Perks.ShootOnlyAp);
            Add(ref h, u.Perks.HitAndRunUsed);
            Add(ref h, u.Perks.LostTurnToStun);
            Add(ref h, u.Perks.InTheZoneUses);
            Add(ref h, u.Perks.LightningReflexesSpent);
            Add(ref h, u.Perks.UntouchableArmed);
            foreach (var (k, v) in u.Perks.ChargesUsed.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                Add(ref h, k);
                Add(ref h, v);
            }
            Add(ref h, u.Stats.Kills);
            Add(ref h, u.Stats.DamageDealt);
            Add(ref h, u.Stats.ShotsFired);
            Add(ref h, u.Stats.Hits);
            Add(ref h, u.IsAlive);
            Add(ref h, u.Evacuated);
            Add(ref h, u.IsActive);
            Add(ref h, u.Pod?.Value ?? -1);
            Add(ref h, u.AppearanceId);
            Add(ref h, u.Statuses.SuppressedBy?.Value ?? -1);
            Add(ref h, u.SummonedBy?.Value ?? -1);
            Add(ref h, u.HasSummoned);
            Add(ref h, u.ImmuneToStun);
            Add(ref h, u.Carrying?.Id.Value ?? -1);
        }
        return h;
    }

    private static void Add(ref ulong h, ulong value)
    {
        for (var i = 0; i < 8; i++)
        {
            h ^= (byte)(value >> (i * 8));
            h *= Prime;
        }
    }

    private static void Add(ref ulong h, int value) => Add(ref h, unchecked((ulong)(long)value));

    private static void Add(ref ulong h, bool value) => Add(ref h, value ? 1 : 0);

    // Length-prefixed so "ab","c" and "a","bc" differ.
    private static void Add(ref ulong h, string value)
    {
        Add(ref h, value.Length);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            h ^= b;
            h *= Prime;
        }
    }
}
