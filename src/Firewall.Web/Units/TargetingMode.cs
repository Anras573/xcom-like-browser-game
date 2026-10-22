using Firewall.Rules.Data;
using Firewall.Rules.Tactical;

namespace Firewall.Web.Units;

/// <summary>A target the ability can be used on, with its odds when it has any (a shot, a hack).</summary>
public sealed record TargetCandidate(TacticalUnit Unit, ShotPreview? Preview);

/// <summary>
/// Targeting for any ability aimed at a unit (Shoot, Rapid Fire, Hack, Suppression, Overclock, ...):
/// the user, its candidate targets (best hit chance first) and the chosen one. Enemy abilities
/// offer the hostiles in view, ally abilities the squadmates. Built fresh from the engine, so it
/// never goes stale across a state change.
/// </summary>
public sealed class TargetingMode
{
    private TargetingMode(
        UnitId shooter,
        string abilityId,
        IReadOnlyList<TargetCandidate> candidates
    )
    {
        Shooter = shooter;
        AbilityId = abilityId;
        Candidates = candidates;
    }

    public UnitId Shooter { get; }

    public string AbilityId { get; }

    /// <summary>True when the ability is aimed at squadmates (Overclock, Medkit) rather than hostiles.</summary>
    public bool TargetsAllies { get; private init; }

    /// <summary>True when any unit will do (Carry: a downed squadmate or a stunned Proxy).</summary>
    public bool TargetsAnyUnit { get; private init; }

    /// <summary>Never empty; sorted by hit chance (descending), then distance, then id.</summary>
    public IReadOnlyList<TargetCandidate> Candidates { get; }

    public int Index { get; private set; }

    public TargetCandidate Current => Candidates[Index];

    /// <summary>The command that fires at <see cref="Current"/>.</summary>
    public UseAbilityCommand Command => new(Shooter, AbilityId, Target.Unit(Current.Unit.Id));

    /// <summary>
    /// The hostiles <paramref name="shooter"/> can fire at right now and that
    /// <paramref name="isVisible"/> accepts (the squad sees them).
    /// </summary>
    public static List<TargetCandidate> FindCandidates(
        TacticalEngine engine,
        TacticalUnit shooter,
        Func<TacticalUnit, bool> isVisible,
        string abilityId = AbilityRegistry.Shoot
    )
    {
        var list = new List<TargetCandidate>();
        foreach (var unit in Possible(engine, shooter, isVisible, abilityId))
        {
            var cmd = new UseAbilityCommand(shooter.Id, abilityId, Target.Unit(unit.Id));
            if (engine.Validate(cmd).IsValid)
                list.Add(new TargetCandidate(unit, engine.PreviewShot(shooter, unit, abilityId)));
        }
        return list.OrderByDescending(c => c.Preview?.HitChance ?? 0)
            .ThenBy(c => c.Unit.Position.DistanceTo(shooter.Position))
            .ThenBy(c => c.Unit.Id.Value)
            .ToList();
    }

    /// <summary>Enters targeting, or returns why the Shoot button is disabled.</summary>
    public static TargetingMode? TryBegin(
        TacticalEngine engine,
        TacticalUnit shooter,
        Func<TacticalUnit, bool> isVisible,
        out string reason,
        string abilityId = AbilityRegistry.Shoot
    )
    {
        var candidates = FindCandidates(engine, shooter, isVisible, abilityId);
        if (candidates.Count > 0)
        {
            reason = "";
            return new TargetingMode(shooter.Id, abilityId, candidates)
            {
                TargetsAllies = AimsAtAllies(engine, abilityId),
                TargetsAnyUnit = AimsAtAnyUnit(engine, abilityId),
            };
        }
        // The nearest unit gives the most useful reason: no AP, out of ammo, no LOS, ... Units the
        // ability can never aim at ("Husk is not a robot") only count when nothing else is left.
        var reasons = Possible(engine, shooter, isVisible, abilityId)
            .OrderBy(u => u.Position.DistanceTo(shooter.Position))
            .ThenBy(u => u.Id.Value)
            .Select(u =>
                engine
                    .Validate(new UseAbilityCommand(shooter.Id, abilityId, Target.Unit(u.Id)))
                    .Reason
            )
            .ToList();
        reason =
            reasons.FirstOrDefault(r => !r.Contains("is not a robot"))
            ?? (reasons.Count > 0 ? "No robot in sight." : null)
            ?? (
                AimsAtAnyUnit(engine, abilityId) ? "Nobody to pick up."
                : AimsAtAllies(engine, abilityId) ? "No squadmate to target."
                : "No enemies in sight."
            );
        return null;
    }

    /// <summary>Steps through the candidates, wrapping; <paramref name="step"/> is +1 or -1.</summary>
    public void Cycle(int step) =>
        Index = ((Index + step) % Candidates.Count + Candidates.Count) % Candidates.Count;

    /// <summary>Chooses <paramref name="unit"/> if it is a candidate.</summary>
    public bool Select(UnitId unit)
    {
        for (var i = 0; i < Candidates.Count; i++)
            if (Candidates[i].Unit.Id == unit)
            {
                Index = i;
                return true;
            }
        return false;
    }

    private static bool AimsAtAllies(TacticalEngine engine, string abilityId) =>
        engine.Data.Abilities.TryGet(abilityId, out var def) && def.Targeting == TargetingType.Ally;

    private static bool AimsAtAnyUnit(TacticalEngine engine, string abilityId) =>
        engine.Data.Abilities.TryGet(abilityId, out var def) && def.Targeting == TargetingType.Unit;

    // The Medkit can be used on its own user.
    private static bool AllowsSelf(TacticalEngine engine, string abilityId) =>
        engine.Data.Abilities.TryGet(abilityId, out var def)
        && def.Parameters.GetValueOrDefault("allowSelf") > 0;

    // The units worth asking the engine about: hostiles in view, or the other squadmates.
    private static IEnumerable<TacticalUnit> Possible(
        TacticalEngine engine,
        TacticalUnit user,
        Func<TacticalUnit, bool> isVisible,
        string abilityId
    ) =>
        AimsAtAnyUnit(engine, abilityId)
            ? engine.State.Units.Where(u => u.IsOnMap && u.Id != user.Id && isVisible(u))
        : AimsAtAllies(engine, abilityId)
            ? engine.State.Units.Where(u =>
                u.IsOnMap
                && u.Faction == user.Faction
                && (u.Id != user.Id || AllowsSelf(engine, abilityId))
            )
        : engine.State.Units.Where(u =>
            u.IsOnMap
            && !u.Statuses.IsDown
            && u.Faction != user.Faction
            && u.Faction != Faction.Neutral
            && isVisible(u)
        );
}
