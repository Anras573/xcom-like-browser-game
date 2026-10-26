using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

public enum MissionType
{
    Purge,
    Rescue,
    Sabotage,
    Capture,

    /// <summary>A handcrafted story mission: it uses <see cref="MissionSpec.StoryMapId"/> and a Purge goal until its own rules land.</summary>
    Story,
}

public enum Difficulty
{
    Recruit,
    Normal,
    Veteran,
}

/// <summary>
/// What a mission is made of (GDD §4.10): the campaign and the Skirmish menu both fill one in and
/// hand it to <see cref="MissionSetup"/>.
/// </summary>
public sealed record MissionSpec
{
    public MissionType Type { get; init; }
    public Biome Biome { get; init; } = Biome.Industrial;
    public int ForceLevel { get; init; } = 2;
    public Difficulty Difficulty { get; init; } = Difficulty.Normal;
    public ulong Seed { get; init; }

    /// <summary>The campaign district the mission is in; null in a skirmish.</summary>
    public string? DistrictId { get; init; }

    /// <summary>When set, the handcrafted map replaces the generator.</summary>
    public string? StoryMapId { get; init; }

    /// <summary>Reward lines for the briefing, display only.</summary>
    public IReadOnlyList<string> Rewards { get; init; } = [];

    /// <summary>The id of the mission type in <c>missions.json</c>.</summary>
    public string MissionId => Type.ToString().ToLowerInvariant();
}
