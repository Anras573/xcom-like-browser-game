using Firewall.Rules.Data;
using Firewall.Rules.Tactical;

namespace Firewall.Web.Scenes;

/// <summary>
/// The debug squad builder's state (#30): four <see cref="SoldierSpec"/>s the player edits. Every
/// edit keeps the spec legal (<see cref="SoldierBuilder.Validate"/>): changing the class or rank
/// re-fits the ability picks and the utility slots, so the Skirmish can always start.
/// </summary>
public sealed class SquadBuilderModel
{
    public const int Size = 4;

    private readonly GameData _data;
    private readonly SoldierSpec[] _specs;

    private SquadBuilderModel(GameData data, SoldierSpec[] specs)
    {
        _data = data;
        _specs = specs;
    }

    public IReadOnlyList<SoldierSpec> Specs => _specs;

    /// <summary>One Captain of each class, with the first ability of every choice.</summary>
    public static SquadBuilderModel Default(GameData data) =>
        new(
            data,
            data.Classes.Take(Size)
                .Select(c => SoldierSpec.Default(data, c.Id, Rank.Captain))
                .ToArray()
        );

    /// <summary>The classes a soldier can have; null is the Rookie.</summary>
    public IReadOnlyList<string?> ClassChoices => [null, .. _data.Classes.Select(c => c.Id)];

    public ClassDef? ClassOf(int soldier) =>
        _specs[soldier].ClassId is { } id ? _data.Classes[id] : null;

    public void CycleClass(int soldier, int step)
    {
        var choices = ClassChoices;
        var next = choices[
            Wrap(choices.ToList().IndexOf(_specs[soldier].ClassId) + step, choices.Count)
        ];
        // A new class starts at the soldier's rank (Corporal if it was a Rookie), on default picks.
        var rank =
            next is null ? Rank.Rookie
            : _specs[soldier].Rank > Rank.Corporal ? _specs[soldier].Rank
            : Rank.Corporal;
        _specs[soldier] = Refit(_specs[soldier] with { ClassId = next, Rank = rank, Picks = [] });
    }

    /// <summary>Corporal to Captain; a Rookie has no ranks to choose.</summary>
    public void CycleRank(int soldier, int step)
    {
        var spec = _specs[soldier];
        if (spec.ClassId is null)
            return;
        var rank = (Rank)(Wrap((int)spec.Rank - 1 + step, 4) + 1);
        _specs[soldier] = Refit(spec with { Rank = rank });
    }

    /// <summary>Chooses <paramref name="abilityId"/> at the <paramref name="pickIndex"/>th choice (Sergeant first).</summary>
    public bool SetPick(int soldier, int pickIndex, string abilityId)
    {
        var spec = _specs[soldier];
        if (spec.ClassId is null)
            return false;
        var ranks = SoldierBuilder.PickRanks(_data.Classes[spec.ClassId], spec.Rank).ToList();
        if (
            pickIndex < 0
            || pickIndex >= ranks.Count
            || !ranks[pickIndex].Abilities.Contains(abilityId)
        )
            return false;
        var picks = spec.Picks.ToList();
        picks[pickIndex] = abilityId;
        _specs[soldier] = spec with { Picks = picks };
        return true;
    }

    public void CycleWeaponTier(int soldier, int step) =>
        _specs[soldier] = _specs[soldier] with
        {
            WeaponTier = Wrap(_specs[soldier].WeaponTier - 1 + step, 3) + 1,
        };

    public void CycleArmor(int soldier, int step)
    {
        var ids = _data.Armor.Select(a => a.Id).ToList();
        _specs[soldier] = _specs[soldier] with
        {
            ArmorId = ids[Wrap(ids.IndexOf(_specs[soldier].ArmorId) + step, ids.Count)],
        };
    }

    /// <summary>Utility slots the soldier has right now (the Tech earns a second at Sergeant).</summary>
    public int ItemSlots(int soldier) =>
        SoldierBuilder.ItemSlots(ClassOf(soldier), _specs[soldier].Rank);

    /// <summary>Cycles one slot through the items and back to empty.</summary>
    public void CycleItem(int soldier, int slot, int step)
    {
        if (slot < 0 || slot >= ItemSlots(soldier))
            return;
        var choices = new List<string?> { null };
        choices.AddRange(_data.Items.Select(i => i.Id));
        var items = _specs[soldier].ItemIds.ToList();
        var current = slot < items.Count ? items[slot] : null;
        var next = choices[Wrap(choices.IndexOf(current) + step, choices.Count)];
        // A slot past the end of the list is filled in order, so an empty slot 1 pushes slot 2 down.
        var padded = Enumerable
            .Range(0, ItemSlots(soldier))
            .Select(i => i < items.Count ? items[i] : null)
            .ToList();
        padded[slot] = next;
        _specs[soldier] = _specs[soldier] with { ItemIds = padded.OfType<string>().ToList() };
    }

    /// <summary>The item in a slot, or null when it is empty.</summary>
    public string? ItemAt(int soldier, int slot) =>
        slot < _specs[soldier].ItemIds.Count ? _specs[soldier].ItemIds[slot] : null;

    // Keeps the picks that still fit, fills the rest with each choice's first ability, and trims items.
    private SoldierSpec Refit(SoldierSpec spec)
    {
        if (spec.ClassId is null)
            return spec with { Rank = Rank.Rookie, Picks = [], ItemIds = Trim(spec.ItemIds, 1) };
        var ranks = SoldierBuilder.PickRanks(_data.Classes[spec.ClassId], spec.Rank).ToList();
        var picks = ranks
            .Select(
                (r, i) =>
                    i < spec.Picks.Count && r.Abilities.Contains(spec.Picks[i])
                        ? spec.Picks[i]
                        : r.Abilities[0]
            )
            .ToList();
        return spec with
        {
            Picks = picks,
            ItemIds = Trim(
                spec.ItemIds,
                SoldierBuilder.ItemSlots(_data.Classes[spec.ClassId], spec.Rank)
            ),
        };
    }

    private static IReadOnlyList<string> Trim(IReadOnlyList<string> items, int slots) =>
        items.Take(slots).ToList();

    private static int Wrap(int value, int count) => (value % count + count) % count;
}
