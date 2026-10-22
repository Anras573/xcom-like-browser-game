namespace Firewall.Rules.Data;

/// <summary>
/// Mk II and Mk III variants are generated from a base <see cref="EnemyDef"/> and an
/// <see cref="EliteDef"/> (GDD §5.6), so there is one definition of each enemy in the data.
/// </summary>
public static class EliteTransform
{
    /// <summary>Id of the variant of <paramref name="baseId"/> made with <paramref name="elite"/>, such as <c>warden_mk2</c>.</summary>
    public static string VariantId(string baseId, EliteDef elite) => $"{baseId}_{elite.Id}";

    /// <summary>Bosses and map objects have no elite variants.</summary>
    public static bool HasVariants(EnemyDef def) =>
        def is { Kind: EnemyKind.Unit, EliteTier: 0 } && def.Behavior.Kind != AiKind.Boss;

    /// <summary>The variant for elite tier <paramref name="tier"/> (1 = first elite in the table): extra HP, aim and armor, a tint, a name suffix and loot ×multiplier (rounded up).</summary>
    public static EnemyDef Apply(EnemyDef def, EliteDef elite, int tier) =>
        def with
        {
            Id = VariantId(def.Id, elite),
            Name = $"{def.Name} {elite.Name}",
            Hp = def.Hp + elite.Hp,
            Aim = def.Aim + elite.Aim,
            Armor = def.Armor + elite.Armor,
            EliteTier = tier,
            BaseId = def.Id,
            Sprite = def.Sprite with { Tint = elite.Tint },
            Loot =
            [
                .. def.Loot.Select(l =>
                    l with
                    {
                        Amount = (int)Math.Ceiling(l.Amount * elite.LootMultiplier - 1e-9),
                    }
                ),
            ],
        };

    /// <summary>Every variant of <paramref name="enemies"/>, tier by tier in <paramref name="elites"/> order.</summary>
    public static IEnumerable<EnemyDef> Variants(
        IReadOnlyList<EnemyDef> enemies,
        IReadOnlyList<EliteDef> elites
    ) =>
        enemies
            .Where(HasVariants)
            .SelectMany((def, _) => elites.Select((elite, i) => Apply(def, elite, i + 1)));

    /// <summary>The highest elite tier whose <see cref="EliteDef.MinConvergence"/> is at or below <paramref name="convergence"/> (0 = none).</summary>
    public static int TierAt(IReadOnlyList<EliteDef> elites, int convergence)
    {
        var tier = 0;
        for (var i = 0; i < elites.Count; i++)
            if (elites[i].MinConvergence <= convergence)
                tier = i + 1;
        return tier;
    }
}
