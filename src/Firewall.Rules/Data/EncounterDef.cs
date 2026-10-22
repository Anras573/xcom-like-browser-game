using System.Text.Json.Serialization;

namespace Firewall.Rules.Data;

/// <summary>One pod that can spawn at a Force Level: which enemies, and how likely it is relative to the others.</summary>
public sealed record PodTemplateDef
{
    [JsonRequired]
    public int Weight { get; init; }

    /// <summary>Base enemy ids, leader first.</summary>
    [JsonRequired]
    public IReadOnlyList<string> Units { get; init; } = [];

    /// <summary>0 for base enemies, 1 for Mk II, 2 for Mk III: every unit becomes that elite variant.</summary>
    public int Tier { get; init; }

    /// <summary>Overrides the pod's banner; otherwise it is the banner of the strongest member.</summary>
    public string? Name { get; init; }
}

/// <summary>The weighted pod templates of one Force Level (<c>data/encounters.json</c>).</summary>
public sealed record ForceLevelDef : IDef
{
    [JsonRequired]
    public int Level { get; init; }

    [JsonRequired]
    public IReadOnlyList<PodTemplateDef> Pods { get; init; } = [];

    string IDef.Id => Level.ToString();
}
