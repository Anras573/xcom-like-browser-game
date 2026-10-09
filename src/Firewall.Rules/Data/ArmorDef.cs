using System.Text.Json.Serialization;

namespace Firewall.Rules.Data;

public sealed record ArmorDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public int Hp { get; init; }

    [JsonRequired]
    public int Armor { get; init; }

    [JsonRequired]
    public int Mobility { get; init; }

    /// <summary>Research id that unlocks it; null when available from the start.</summary>
    public string? UnlockedBy { get; init; }
}
