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
    private readonly Dictionary<string, IAbilityHandler> _handlers = new(StringComparer.Ordinal);

    public void Register(string abilityId, IAbilityHandler handler) =>
        _handlers[abilityId] = handler;

    public bool TryGet(string abilityId, out IAbilityHandler handler) =>
        _handlers.TryGetValue(abilityId, out handler!);
}
