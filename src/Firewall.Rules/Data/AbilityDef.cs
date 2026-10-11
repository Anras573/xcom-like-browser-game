using System.Text.Json.Serialization;

namespace Firewall.Rules.Data;

/// <summary>Ability data. The behaviour is a code handler keyed by <see cref="Id"/> (#30).</summary>
public sealed record AbilityDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public string Description { get; init; } = "";

    [JsonRequired]
    public int ApCost { get; init; }

    [JsonRequired]
    public bool EndsTurn { get; init; }
    public int Cooldown { get; init; }

    [JsonRequired]
    public TargetingType Targeting { get; init; }
    private readonly IReadOnlyDictionary<string, double>? _parameters;
    public IReadOnlyDictionary<string, double> Parameters
    {
        get => _parameters ?? Maps.Empty;
        init => _parameters = value;
    }
}
