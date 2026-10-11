using System.Text.Json.Serialization;

namespace Firewall.Rules.Data;

public sealed record ItemDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public string Description { get; init; } = "";

    [JsonRequired]
    public ItemKind Kind { get; init; }

    /// <summary>Uses per mission; 0 for passive items.</summary>
    [JsonRequired]
    public int Charges { get; init; }

    /// <summary>Numbers the item handler reads (damage, radius, range, heal, ...).</summary>
    private readonly IReadOnlyDictionary<string, double>? _parameters;
    public IReadOnlyDictionary<string, double> Parameters
    {
        get => _parameters ?? Maps.Empty;
        init => _parameters = value;
    }
}
