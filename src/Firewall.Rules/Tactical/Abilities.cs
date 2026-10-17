using Firewall.Rules.Data;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Tactical;

/// <summary>Everything a handler needs to validate or run one ability use.</summary>
public sealed record AbilityContext(
    TacticalState State,
    TacticalUnit User,
    Target Target,
    GameData Data,
    AbilityDef Def,
    IRandom Rng,
    List<GameEvent> Events
);

/// <summary>
/// The behaviour of one ability. The engine has already checked AP, cooldown, phase and target
/// shape; <see cref="Validate"/> adds the ability's own rules (range, line of sight, ammo).
/// </summary>
public interface IAbilityHandler
{
    ValidationResult Validate(AbilityContext ctx);

    /// <summary>Applies the effect and appends its events. AP, cooldown and <see cref="AbilityUsed"/> are the engine's.</summary>
    void Execute(AbilityContext ctx);
}

/// <summary>Ability handlers by ability id.</summary>
public sealed class AbilityRegistry
{
    public const string Shoot = "shoot";
    public const string Melee = "melee_attack";
    public const string Overwatch = "overwatch";
    public const string Reload = "reload";
    public const string HunkerDown = "hunker_down";

    /// <summary>Passive: reaction shots can crit.</summary>
    public const string Sentinel = "sentinel";

    /// <summary>The standard actions every soldier has, in ability-bar order (hotkeys 1 to 4).</summary>
    public static IReadOnlyList<string> StandardActions { get; } =
    [Shoot, Overwatch, Reload, HunkerDown];

    private readonly Dictionary<string, IAbilityHandler> _handlers = new(StringComparer.Ordinal);

    /// <summary>The handlers implemented so far: Shoot, Melee, Overwatch, Reload and Hunker Down.</summary>
    public static AbilityRegistry CreateDefault()
    {
        var registry = new AbilityRegistry();
        registry.Register(Shoot, new ShootAbility());
        registry.Register(Melee, new ShootAbility(RangeType.Melee));
        registry.Register(Overwatch, new OverwatchAbility());
        registry.Register(Reload, new ReloadAbility());
        registry.Register(HunkerDown, new HunkerDownAbility());
        return registry;
    }

    public void Register(string abilityId, IAbilityHandler handler) =>
        _handlers[abilityId] = handler;

    public bool TryGet(string abilityId, out IAbilityHandler handler) =>
        _handlers.TryGetValue(abilityId, out handler!);
}
