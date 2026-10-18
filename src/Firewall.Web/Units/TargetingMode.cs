using Firewall.Rules.Tactical;

namespace Firewall.Web.Units;

/// <summary>A target the shooter can fire at, with its odds.</summary>
public sealed record TargetCandidate(TacticalUnit Unit, ShotPreview Preview);

/// <summary>
/// Shoot targeting: the shooter, its candidate targets (best hit chance first) and the chosen one.
/// Built fresh from the engine, so it never goes stale across a state change.
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
        foreach (var unit in Hostiles(engine, shooter, isVisible))
        {
            var cmd = new UseAbilityCommand(shooter.Id, abilityId, Target.Unit(unit.Id));
            if (
                engine.Validate(cmd).IsValid
                && engine.PreviewShot(shooter, unit, abilityId) is { } preview
            )
                list.Add(new TargetCandidate(unit, preview));
        }
        return list.OrderByDescending(c => c.Preview.HitChance)
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
            return new TargetingMode(shooter.Id, abilityId, candidates);
        }
        // The nearest seen hostile gives the most useful reason: no AP, out of ammo, no LOS, ...
        var nearest = Hostiles(engine, shooter, isVisible)
            .OrderBy(u => u.Position.DistanceTo(shooter.Position))
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
        reason = nearest is null
            ? "No enemies in sight."
            : engine
                .Validate(new UseAbilityCommand(shooter.Id, abilityId, Target.Unit(nearest.Id)))
                .Reason;
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

    private static IEnumerable<TacticalUnit> Hostiles(
        TacticalEngine engine,
        TacticalUnit shooter,
        Func<TacticalUnit, bool> isVisible
    ) =>
        engine.State.Units.Where(u =>
            u.IsOnMap
            && !u.Statuses.IsDown
            && u.Faction != shooter.Faction
            && u.Faction != Faction.Neutral
            && isVisible(u)
        );
}
