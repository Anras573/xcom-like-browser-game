namespace Firewall.Rules.Data;

internal static class GameDataValidator
{
    public static IReadOnlyList<string> Validate(GameData d)
    {
        var errors = new List<string>();
        void Err(string message) => errors.Add(message);

        Ids(errors, "weapon", d.Weapons);
        Ids(errors, "armor", d.Armor);
        Ids(errors, "item", d.Items);
        Ids(errors, "ability", d.Abilities);
        Ids(errors, "class", d.Classes);
        Ids(errors, "enemy", d.Enemies);
        Ids(errors, "elite", d.Elites);
        Ids(errors, "research", d.Research);
        Ids(errors, "facility", d.Facilities);
        Ids(errors, "workshop item", d.Workshop);
        Ids(errors, "mission type", d.Missions);
        Ids(errors, "district", d.Districts);
        foreach (var facility in d.Facilities)
            Ids(errors, $"upgrade of facility '{facility.Id}'", facility.Upgrades);

        // Ids unlockable by research must be unique across the tables research can point into.
        var unlockable = new Dictionary<string, string>(StringComparer.Ordinal);
        void Claim(string id, string table)
        {
            if (!unlockable.TryAdd(id, table))
                Err($"id '{id}' is used by both {unlockable[id]} and {table}");
        }
        foreach (var x in d.Items)
            Claim(x.Id, "item");
        foreach (var x in d.Armor)
            Claim(x.Id, "armor");
        foreach (var x in d.Workshop)
            Claim(x.Id, "workshop item");
        foreach (var x in d.Missions)
            Claim(x.Id, "mission type");

        foreach (var w in d.Weapons)
        {
            var p = $"weapon '{w.Id}'";
            if (w.Tier is < 1 or > 3)
                Err($"{p}: tier {w.Tier} must be 1-3");
            if (w.DmgMin > w.DmgMax)
                Err($"{p}: dmgMin {w.DmgMin} > dmgMax {w.DmgMax}");
            if (w.DmgMin < 0)
                Err($"{p}: negative dmgMin {w.DmgMin}");
            if (w.Clip < 1)
                Err($"{p}: clip {w.Clip} must be at least 1");
            if (w.Crit is < 0 or > 100)
                Err($"{p}: crit {w.Crit} must be 0-100");
            foreach (var band in w.Range)
            {
                if (band.MaxDist is not null && band.PerTileBeyond is not null)
                    Err($"{p}: a range band cannot set both maxDist and perTileBeyond");
                if (band.MaxDist < 0 || band.PerTileBeyond < 0)
                    Err($"{p}: negative range threshold");
            }
        }
        foreach (var wc in Enum.GetValues<WeaponClass>())
        foreach (var tier in new[] { 1, 2, 3 })
            if (d.Weapons.Count(w => w.Class == wc && w.Tier == tier) != 1)
                Err($"weapon class {wc} needs exactly one tier {tier} weapon");

        foreach (var a in d.Armor)
        {
            var p = $"armor '{a.Id}'";
            if (a.Hp < 0 || a.Armor < 0)
                Err($"{p}: negative hp or armor");
            if (a.UnlockedBy is not null && !d.Research.Contains(a.UnlockedBy))
                Err($"{p}: unlockedBy unknown research '{a.UnlockedBy}'");
        }

        foreach (var i in d.Items)
        {
            if (i.Charges < 0)
                Err($"item '{i.Id}': negative charges {i.Charges}");
            if (i.Kind != ItemKind.Passive && i.Charges == 0)
                Err($"item '{i.Id}': a non-passive item needs at least 1 charge");
        }

        foreach (var a in d.Abilities)
        {
            if (a.ApCost < 0)
                Err($"ability '{a.Id}': negative apCost {a.ApCost}");
            if (a.Cooldown < 0)
                Err($"ability '{a.Id}': negative cooldown {a.Cooldown}");
        }

        foreach (var c in d.Classes)
        {
            var p = $"class '{c.Id}'";
            if (!d.Weapons.Any(w => w.Class == c.WeaponClass))
                Err($"{p}: no weapon of class {c.WeaponClass}");
            var seenRanks = new HashSet<Rank>();
            foreach (var r in c.AbilityTree)
            {
                if (!seenRanks.Add(r.Rank))
                    Err($"{p}: rank {r.Rank} listed twice");
                if (r.Pick < 0 || r.Pick > r.Abilities.Count)
                    Err($"{p}: rank {r.Rank} picks {r.Pick} of {r.Abilities.Count} abilities");
                foreach (var id in r.Abilities)
                    if (!d.Abilities.Contains(id))
                        Err($"{p}: rank {r.Rank} references unknown ability '{id}'");
            }
        }

        var s = d.Soldier;
        if (s.Hp <= 0)
            Err($"soldier: hp {s.Hp} must be positive");
        for (var i = 1; i < s.Ranks.Count; i++)
            if (s.Ranks[i].Xp <= s.Ranks[i - 1].Xp)
                Err($"soldier: xp for rank {s.Ranks[i].Rank} must exceed the previous rank's");

        foreach (var e in d.Enemies)
        {
            var p = $"enemy '{e.Id}'";
            if (e.Hp <= 0)
                Err($"{p}: hp {e.Hp} must be positive");
            if (e.DmgMin > e.DmgMax)
                Err($"{p}: dmgMin {e.DmgMin} > dmgMax {e.DmgMax}");
            if (e.Armor < 0 || e.Mobility < 0 || e.Defense < 0 || e.Sight < 0 || e.DmgMin < 0)
                Err($"{p}: negative stat");
            if (e.PodMin < 1 || e.PodMin > e.PodMax)
                Err($"{p}: invalid pod size {e.PodMin}-{e.PodMax}");
            foreach (var id in e.Abilities)
                if (!d.Abilities.Contains(id))
                    Err($"{p}: unknown ability '{id}'");
            if (e.Summon is { } sm)
            {
                if (!d.Enemies.Contains(sm.EnemyId))
                    Err($"{p}: summons unknown enemy '{sm.EnemyId}'");
                if (sm.Count < 1)
                    Err($"{p}: summon count must be at least 1");
                if (sm.EveryTurns <= 0 && sm.BelowHpPercent <= 0)
                    Err($"{p}: summon needs everyTurns or belowHpPercent");
            }
            foreach (var l in e.Loot)
            {
                if (l.Amount < 0)
                    Err($"{p}: negative loot amount");
                if (l.ChancePercent is < 0 or > 100)
                    Err($"{p}: loot chance {l.ChancePercent} must be 0-100");
            }
        }

        foreach (var e in d.Elites)
        {
            if (e.LootMultiplier < 1)
                Err($"elite '{e.Id}': lootMultiplier {e.LootMultiplier} is below 1");
            if (e.Hp < 0)
                Err($"elite '{e.Id}': negative hp bonus");
        }

        foreach (var r in d.Research)
        {
            var p = $"research '{r.Id}'";
            if (r.BaseDays <= 0)
                Err($"{p}: baseDays {r.BaseDays} must be positive");
            foreach (var req in r.Requires)
            {
                switch (req.Kind)
                {
                    case RequirementKind.Research:
                        if (req.Id is null || !d.Research.Contains(req.Id))
                            Err($"{p}: requires unknown research '{req.Id}'");
                        else if (req.Id == r.Id)
                            Err($"{p}: requires itself");
                        break;
                    case RequirementKind.EnemyKilled:
                    case RequirementKind.EnemyCaptured:
                        if (req.Id is null || !d.Enemies.Contains(req.Id))
                            Err($"{p}: requires unknown enemy '{req.Id}'");
                        break;
                    case RequirementKind.MissionWon:
                        if (req.Id is null || !d.Missions.Contains(req.Id))
                            Err($"{p}: requires unknown mission '{req.Id}'");
                        break;
                    case RequirementKind.Salvage:
                    case RequirementKind.Cores:
                        if (req.Amount <= 0)
                            Err($"{p}: {req.Kind} requirement needs a positive amount");
                        break;
                    case RequirementKind.KeyItem:
                        if (string.IsNullOrEmpty(req.Id))
                            Err($"{p}: key item requirement needs an id");
                        break;
                }
            }
            foreach (var id in r.Unlocks)
                if (!unlockable.ContainsKey(id))
                    Err($"{p}: unlocks unknown id '{id}'");
        }

        // Research prerequisites must not loop (A -> B -> A).
        var state = new Dictionary<string, bool>(StringComparer.Ordinal); // false = on stack, true = done
        void Visit(ResearchDef r, Stack<string> path)
        {
            if (state.TryGetValue(r.Id, out var done))
            {
                if (!done)
                    Err(
                        $"research cycle: {string.Join(" -> ", path.Reverse().SkipWhile(x => x != r.Id).Append(r.Id))}"
                    );
                return;
            }
            state[r.Id] = false;
            path.Push(r.Id);
            foreach (var req in r.Requires)
                if (
                    req.Kind == RequirementKind.Research
                    && req.Id is not null
                    && req.Id != r.Id
                    && d.Research.TryGet(req.Id, out var next)
                )
                    Visit(next, path);
            path.Pop();
            state[r.Id] = true;
        }
        foreach (var r in d.Research)
            Visit(r, new Stack<string>());

        foreach (var f in d.Facilities)
        {
            var p = $"facility '{f.Id}'";
            if (f.Cost < 0 || f.BuildDays < 0)
                Err($"{p}: negative cost or buildDays");
            if (f.MaxCount < 1)
                Err($"{p}: maxCount must be at least 1");
            foreach (var u in f.Upgrades)
            {
                if (u.Cost < 0)
                    Err($"{p}: upgrade '{u.Id}' has negative cost");
                if (u.Requires is not null && f.Upgrades.All(x => x.Id != u.Requires))
                    Err($"{p}: upgrade '{u.Id}' requires unknown upgrade '{u.Requires}'");
            }
        }

        foreach (var w in d.Workshop)
        {
            var p = $"workshop item '{w.Id}'";
            if (w.Credits < 0 || w.Salvage < 0 || w.Cores < 0 || w.MinEngineers < 0)
                Err($"{p}: negative cost or engineers");
            if (w.BuildDays < 0)
                Err($"{p}: negative buildDays");
            switch (w.Kind)
            {
                case WorkshopKind.Item:
                    if (w.TargetId is null || !d.Items.Contains(w.TargetId))
                        Err($"{p}: builds unknown item '{w.TargetId}'");
                    break;
                case WorkshopKind.Armor:
                    if (w.TargetId is null || !d.Armor.Contains(w.TargetId))
                        Err($"{p}: builds unknown armor '{w.TargetId}'");
                    break;
                case WorkshopKind.TierUpgrade:
                    if (w.Tier is < 2 or > 3)
                        Err($"{p}: tier upgrade must grant tier 2 or 3");
                    break;
            }
        }

        foreach (var m in d.Missions)
            if (m.RequiresItem is not null && !d.Items.Contains(m.RequiresItem))
                Err($"mission type '{m.Id}': requires unknown item '{m.RequiresItem}'");

        if (d.Names.FirstNames.Count == 0 || d.Names.LastNames.Count == 0)
            Err("names: first and last name lists must not be empty");

        return errors;
    }

    private static void Ids<T>(List<string> errors, string what, IEnumerable<T> defs)
        where T : IDef
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in defs)
        {
            if (string.IsNullOrWhiteSpace(def.Id))
                errors.Add($"{what} with empty id");
            else if (!seen.Add(def.Id))
                errors.Add($"duplicate {what} id '{def.Id}'");
        }
    }
}
