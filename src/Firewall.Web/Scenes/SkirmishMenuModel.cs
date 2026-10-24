using Firewall.Rules.Data;
using Firewall.Rules.Tactical;

namespace Firewall.Web.Scenes;

/// <summary>
/// The Skirmish menu's choices (#33): mission type, biome, Force Level, difficulty and seed. The
/// scene draws it; <see cref="ToSpec"/> turns it into the <see cref="MissionSpec"/> that
/// <see cref="MissionSetup"/> builds from, the same one the campaign will use.
/// </summary>
public sealed class SkirmishMenuModel
{
    public static readonly IReadOnlyList<MissionType> Types =
    [
        MissionType.Purge,
        MissionType.Rescue,
        MissionType.Sabotage,
        MissionType.Capture,
    ];

    public static readonly IReadOnlyList<Biome> Biomes =
    [
        Biome.Industrial,
        Biome.Urban,
        Biome.Suburban,
    ];

    public static readonly IReadOnlyList<Difficulty> Difficulties =
    [
        Difficulty.Recruit,
        Difficulty.Normal,
        Difficulty.Veteran,
    ];

    public MissionType Type { get; private set; } = MissionType.Purge;
    public Biome Biome { get; private set; } = Biome.Industrial;
    public int ForceLevel { get; private set; } = 2;
    public Difficulty Difficulty { get; private set; } = Difficulty.Normal;

    /// <summary>Null means a fresh random seed at launch.</summary>
    public ulong? Seed { get; set; }

    public void CycleType(int step) =>
        Type = Types[Wrap(Types.ToList().IndexOf(Type) + step, Types.Count)];

    public void CycleBiome(int step) =>
        Biome = Biomes[Wrap(Biomes.ToList().IndexOf(Biome) + step, Biomes.Count)];

    public void CycleDifficulty(int step) =>
        Difficulty = Difficulties[
            Wrap(Difficulties.ToList().IndexOf(Difficulty) + step, Difficulties.Count)
        ];

    public void CycleForceLevel(int step) =>
        ForceLevel =
            Wrap(
                ForceLevel - EncounterBuilder.MinForceLevel + step,
                EncounterBuilder.MaxForceLevel - EncounterBuilder.MinForceLevel + 1
            ) + EncounterBuilder.MinForceLevel;

    /// <summary>The spec to launch; <paramref name="randomSeed"/> is used when no seed is fixed.</summary>
    public MissionSpec ToSpec(ulong randomSeed) =>
        new()
        {
            Type = Type,
            Biome = Biome,
            ForceLevel = ForceLevel,
            Difficulty = Difficulty,
            Seed = Seed ?? randomSeed,
        };

    private static int Wrap(int value, int count) => ((value % count) + count) % count;
}
