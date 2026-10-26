using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Firewall.Web.Input;

namespace Firewall.Web.Ui;

/// <summary>One button of the ability bar.</summary>
/// <param name="Key">The hotkey action; null past slot 9.</param>
public sealed record AbilitySlot(
    string AbilityId,
    string Name,
    string Description,
    int ApCost,
    int Cooldown,
    int Slot,
    GameAction? Key,
    string Icon = "icon.star",
    int? Charges = null
)
{
    public string HotkeyLabel => Slot.ToString();
}

public enum ApPip
{
    Full,
    Empty,
}

/// <summary>How a soldier shows in the squad strip.</summary>
public enum StripState
{
    Ready,
    Spent,
    BleedingOut,
    Stabilized,
    Dead,
    Evacuated,
}

/// <summary>The pure decisions behind the tactical HUD, kept apart from drawing so they can be tested.</summary>
public static class HudModel
{
    public const int MaxSlots = 9;

    /// <summary>Logical pixels the bottom panels cover; the camera aims the soldier above them.</summary>
    public const float BottomCoverPixels = 160f;

    private static readonly GameAction[] SlotKeys =
    [
        GameAction.Ability1,
        GameAction.Ability2,
        GameAction.Ability3,
        GameAction.Ability4,
        GameAction.Ability5,
        GameAction.Ability6,
        GameAction.Ability7,
        GameAction.Ability8,
        GameAction.Ability9,
    ];

    // Shoot, Overwatch, Reload and Hunker always come first, in this order.
    private static readonly string[] Fixed =
    [
        AbilityRegistry.Shoot,
        AbilityRegistry.Overwatch,
        AbilityRegistry.Reload,
        AbilityRegistry.HunkerDown,
    ];

    /// <summary>The action for slot 1 to 9 (1-based), or null.</summary>
    public static GameAction? KeyForSlot(int slot) =>
        slot is >= 1 and <= MaxSlots ? SlotKeys[slot - 1] : null;

    /// <summary>
    /// The soldier's ability bar: the four standard actions in a fixed order, then Evac (on an evac
    /// tile), Drop (while carrying), the utility items in the order they were packed, Carry (when
    /// someone next to the soldier can be picked up) and the other active abilities, up to nine.
    /// </summary>
    public static IReadOnlyList<AbilitySlot> AbilitySlots(
        TacticalUnit unit,
        GameData data,
        bool onEvacTile,
        bool carryAvailable = true
    )
    {
        var ids = new List<string>();
        foreach (var id in Fixed)
            if (unit.Abilities.Contains(id))
                ids.Add(id);
        if (onEvacTile && unit.Abilities.Contains(AbilityRegistry.Evac))
            ids.Add(AbilityRegistry.Evac);
        if (unit.Carrying is not null && unit.Abilities.Contains(AbilityRegistry.Drop))
            ids.Add(AbilityRegistry.Drop);
        foreach (var item in unit.Items)
            if (unit.Abilities.Contains(item.ItemId) && !ids.Contains(item.ItemId))
                ids.Add(item.ItemId);
        if (
            carryAvailable
            && unit.Carrying is null
            && unit.Abilities.Contains(AbilityRegistry.Carry)
        )
            ids.Add(AbilityRegistry.Carry);
        foreach (var id in unit.Abilities.OrderBy(a => a, StringComparer.Ordinal))
            if (
                !ids.Contains(id)
                && id is not (AbilityRegistry.Evac or AbilityRegistry.Drop or AbilityRegistry.Carry)
                && data.Abilities.TryGet(id, out var def)
                && def.Targeting != TargetingType.Passive
            )
                ids.Add(id);

        var slots = new List<AbilitySlot>();
        foreach (var id in ids.Take(MaxSlots))
        {
            if (!data.Abilities.TryGet(id, out var def))
                continue;
            var slot = slots.Count + 1;
            slots.Add(
                new AbilitySlot(
                    id,
                    def.Name,
                    def.Describe(),
                    def.ApCost,
                    unit.Cooldowns.GetValueOrDefault(id),
                    slot,
                    KeyForSlot(slot),
                    def.Icon ?? "icon.star",
                    unit.Items.FirstOrDefault(i => i.ItemId == id)?.Charges
                )
            );
        }
        return slots;
    }

    /// <summary>Greedy word wrap for tooltip text, which has no wrapping of its own.</summary>
    public static IEnumerable<string> Wrap(string text, int maxChars)
    {
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
            {
                yield return line;
                line = word;
            }
            else
                line = line.Length == 0 ? word : $"{line} {word}";
        }
        if (line.Length > 0)
            yield return line;
    }

    /// <summary>An ability's name, cost and description, for places with no hotkey or reason (the squad builder).</summary>
    public static string AbilityInfo(AbilityDef def)
    {
        var lines = new List<string>
        {
            $"[c=#3fd0ff]{def.Name}[/c]",
            def.Targeting == TargetingType.Passive
                ? "Passive"
                : $"Cost: [c=#ffb000]{def.ApCost} AP[/c]"
                    + (def.Cooldown > 0 ? $"   Cooldown: [c=#ffb000]{def.Cooldown}[/c] turns" : ""),
        };
        lines.AddRange(Wrap(def.Describe(), 44));
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Name and hotkey, AP cost and cooldown, the description, and in red why it is disabled.
    /// </summary>
    public static string AbilityTooltip(AbilitySlot slot, string? blockedReason)
    {
        var lines = new List<string>
        {
            $"[c=#3fd0ff]{slot.Name}[/c]  [{slot.HotkeyLabel}]",
            $"Cost: [c=#ffb000]{slot.ApCost} AP[/c]"
                + (slot.Cooldown > 0 ? $"   Cooldown: [c=#ffb000]{slot.Cooldown}[/c] turns" : ""),
        };
        if (slot.Charges is { } charges)
            lines.Add($"Charges: [c=#ffb000]{charges}[/c]");
        lines.AddRange(Wrap(slot.Description, 44));
        if (blockedReason is not null)
            lines.Add($"[c=#ff4d4d]{blockedReason}[/c]");
        return string.Join('\n', lines);
    }

    /// <summary>Two diamonds: <paramref name="ap"/> counts whole points, so each is full or empty.</summary>
    public static IReadOnlyList<ApPip> ApPips(int ap, int max = TacticalEngine.ApPerTurn)
    {
        var pips = new List<ApPip>();
        for (var i = 0; i < max; i++)
            pips.Add(i < ap ? ApPip.Full : ApPip.Empty);
        return pips;
    }

    public static StripState StripStateOf(TacticalUnit unit) =>
        !unit.IsAlive ? StripState.Dead
        : unit.Evacuated ? StripState.Evacuated
        : unit.Statuses.IsBleedingOut ? StripState.BleedingOut
        : unit.Statuses.Stabilized ? StripState.Stabilized
        : unit.ActionPoints > 0 ? StripState.Ready
        : StripState.Spent;

    /// <summary>
    /// The soldier to select when cycling with Tab: the next one with AP after <paramref name="current"/>
    /// (before it when <paramref name="step"/> is -1), wrapping. Null when nobody has AP.
    /// </summary>
    public static TacticalUnit? NextWithAp(
        IReadOnlyList<TacticalUnit> soldiers,
        UnitId? current,
        int step
    )
    {
        if (soldiers.Count == 0)
            return null;
        var start = soldiers.ToList().FindIndex(u => u.Id == current);
        for (var i = 1; i <= soldiers.Count; i++)
        {
            var candidate = soldiers[
                ((start + i * step) % soldiers.Count + soldiers.Count) % soldiers.Count
            ];
            if (candidate.IsStanding && candidate.ActionPoints > 0 && candidate.Id != current)
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Who is selected once an action finishes: the current soldier while they have AP, otherwise the
    /// next one with AP, otherwise they stay (for viewing). Null when the current one is gone and
    /// nobody can act.
    /// </summary>
    public static TacticalUnit? SelectionAfterAction(
        IReadOnlyList<TacticalUnit> soldiers,
        UnitId? current
    )
    {
        var selected = soldiers.FirstOrDefault(u => u.Id == current);
        if (selected is { IsStanding: true, ActionPoints: > 0 })
            return selected;
        return NextWithAp(soldiers, current, 1)
            ?? selected
            ?? soldiers.FirstOrDefault(u => u.IsStanding);
    }

    /// <summary>
    /// "Purge: 5 hostiles remaining (estimated)": counts hostiles that are seen or awake, and adds a
    /// "?" while a dormant pod (one never seen) may still be out there.
    /// </summary>
    public static string ObjectiveText(
        TacticalState state,
        GameData data,
        IMissionObjective objective,
        Func<TacticalUnit, bool> isVisible
    )
    {
        if (objective is RescueObjective or SabotageObjective)
            return objective.Describe(state);
        if (objective is not PurgeObjective)
            return objective.Description;
        var known = 0;
        var unexplored = false;
        foreach (var u in state.Units)
        {
            if (
                u is not { Faction: Faction.Meridian, IsAlive: true }
                || data.Enemies[u.DefId].Kind != EnemyKind.Unit
            )
                continue;
            if (u.IsActive || isVisible(u))
                known++;
            else
                unexplored = true;
        }
        var count = known == 1 ? "1 hostile" : $"{known} hostiles";
        var purge = $"Purge: {count} remaining (estimated){(unexplored ? " ?" : "")}";
        return objective is CaptureObjective capture ? capture.Describe(state, purge) : purge;
    }

    /// <summary>
    /// The phase banner's text, or null for a phase with none.
    /// </summary>
    public static string PhaseText(TacticalPhase phase, int turn) =>
        phase switch
        {
            TacticalPhase.Player => $"PLAYER TURN - TURN {turn}",
            TacticalPhase.Enemy => "MERIDIAN ACTIVITY",
            _ => "ENVIRONMENT PHASE",
        };

    /// <summary>
    /// The banner's animation at <paramref name="t"/> seconds of <paramref name="total"/>: it slides
    /// in from above and fades in over the first fifth, holds, then slides and fades out over the last.
    /// Offset is 0 at rest and -1 fully above its place; alpha is 0 to 1.
    /// </summary>
    public static (float Offset, float Alpha) BannerAnimation(float t, float total)
    {
        if (total <= 0f)
            return (0f, 0f);
        var f = Math.Clamp(t / total, 0f, 1f);
        const float edge = 0.2f;
        var amount =
            f < edge ? f / edge
            : f > 1f - edge ? (1f - f) / edge
            : 1f;
        // Ease out so the slide settles gently.
        var eased = 1f - (1f - amount) * (1f - amount);
        return (-(1f - eased), amount);
    }

    /// <summary>
    /// The world point to centre the camera on so that <paramref name="target"/> sits above the
    /// bottom panels: lowered by half of what they cover, in tiles.
    /// </summary>
    public static System.Numerics.Vector2 FocusPoint(
        System.Numerics.Vector2 target,
        float tilesVisibleVertically
    ) =>
        target
        - new System.Numerics.Vector2(0f, BottomCoverPixels / 2f / 720f * tilesVisibleVertically);
}
