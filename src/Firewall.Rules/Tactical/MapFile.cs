using System.Text.Json.Serialization;
using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>The JSON shape of <c>data/maps/*.json</c>. Rows are written top row first.</summary>
public sealed record MapFile
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public Biome Biome { get; init; }

    private IReadOnlyList<string>? _rows;

    [JsonRequired]
    public IReadOnlyList<string> Rows
    {
        get => _rows ?? [];
        init => _rows = value;
    }
}
