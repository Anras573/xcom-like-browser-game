using Firewall.Rules.Data;
using Firewall.Rules.Tactical.Vision;

namespace Firewall.Rules.Tactical;

/// <summary>
/// A standing order to shoot at a hostile unit that moves in sight (GDD §4.6). Overwatch sets one;
/// Suppression (#30) aims one at a single unit and Killing Zone gives one several shots. A unit's
/// triggers expire at the start of its next turn.
/// </summary>
public sealed class ReactionTrigger(string source, StatusFlags status)
{
    /// <summary>The ability that set the trigger, such as <c>overwatch</c>.</summary>
    public string Source { get; } = source;

    /// <summary>The status that shows the trigger; removed once no trigger carries it.</summary>
    public StatusFlags Status { get; } = status;

    /// <summary>Only this mover sets it off (Suppression); null means any hostile.</summary>
    public UnitId? Only { get; init; }

    public int ShotsLeft { get; set; } = 1;

    /// <summary>The movers it already shot: a trigger fires at each mover at most once.</summary>
    public HashSet<UnitId> FiredAt { get; } = [];
}

/// <summary>
/// Reaction fire (GDD §4.6) as a step hook: after each step, every unit with a
/// <see cref="ReactionTrigger"/> that can see the mover fires one reaction shot at it, in unit-id
/// order, until the mover dies or goes down. Emits <see cref="ReactionShotTriggered"/>, then the shot's events,
/// then <see cref="StatusRemoved"/> when the trigger is spent.
/// </summary>
public sealed class ReactionFireHook(GameData data, ShootAbility shoot) : IStepHook
{
    public StepResult OnUnitStepped(StepContext ctx)
    {
        var mover = ctx.Mover;
        var watchers = ctx
            .State.Units.Where(u =>
                u.IsOnMap && u.Reactions.Count > 0 && IsHostile(u.Faction, mover.Faction)
            )
            .OrderBy(u => u.Id.Value)
            .ToList();
        foreach (var watcher in watchers)
        {
            if (!mover.IsStanding)
                break;
            if (!LosTracer.CanSee(ctx.State, watcher, mover))
                continue;
            var trigger = watcher.Reactions.FirstOrDefault(t =>
                t.ShotsLeft > 0 && !t.FiredAt.Contains(mover.Id) && (t.Only ?? mover.Id) == mover.Id
            );
            if (trigger is null)
                continue;
            var shot = ShotContext.For(data, watcher) with
            {
                Reaction = true,
                ReactionCrit = watcher.Abilities.Contains(AbilityRegistry.Sentinel),
                // Mayhem: the reaction shots of a suppression hit harder.
                BonusDamage =
                    trigger.Source == SoldierPerks.Suppression
                    && watcher.Abilities.Contains(SoldierPerks.Mayhem)
                        ? SoldierPerks.IntParam(data, SoldierPerks.Mayhem, "damage", 2)
                        : 0,
            };
            // Out of ammo, stunned and the like: the trigger stays for a later step.
            if (!HitCalculator.Preview(ctx.State, watcher, mover, shot).CanFire)
                continue;

            ctx.Events.Add(new ReactionShotTriggered(watcher.Id, mover.Id));
            trigger.ShotsLeft--;
            trigger.FiredAt.Add(mover.Id);
            shoot.Fire(ctx.State, watcher, mover, shot, ctx.State.Rng, ctx.Events);
            if (trigger.ShotsLeft > 0)
                continue;
            watcher.Reactions.Remove(trigger);
            if (
                trigger.Status != StatusFlags.None
                && !watcher.Reactions.Exists(t => t.Status == trigger.Status)
                && watcher.Statuses.Has(trigger.Status)
            )
            {
                watcher.Statuses.Set(trigger.Status, false);
                ctx.Events.Add(new StatusRemoved(watcher.Id, trigger.Status));
            }
        }
        return mover.IsStanding ? StepResult.Continue : StepResult.Interrupt;
    }

    private static bool IsHostile(Faction a, Faction b) =>
        a != b && a != Faction.Neutral && b != Faction.Neutral;
}
