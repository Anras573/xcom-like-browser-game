using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Firewall.Web.Scenes;

namespace Firewall.Web.Tests;

public class MissionResultTextTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    private static SoldierResult Soldier(SoldierFate fate, int hpLost = 0) =>
        new(new UnitId(1), "A", fate, hpLost, 0, 0, 0, 0, false);

    private static MissionResult Result(IReadOnlyDictionary<string, int> killed) =>
        new(MissionOutcome.Success, 3, [], killed, Loot.Empty, [], null);

    [Theory]
    [InlineData(MissionOutcome.Success, "MISSION SUCCESS")]
    [InlineData(MissionOutcome.Failure, "MISSION FAILED")]
    [InlineData(MissionOutcome.Aborted, "MISSION ABORTED")]
    public void TitleNamesTheOutcome(MissionOutcome outcome, string title) =>
        Assert.Equal(title, MissionResultText.Title(outcome));

    [Fact]
    public void FatesReadAsStatusLabels()
    {
        Assert.Equal("Healthy", MissionResultText.Fate(Soldier(SoldierFate.Healthy)));
        Assert.Equal("Wounded (-3 HP)", MissionResultText.Fate(Soldier(SoldierFate.Wounded, 3)));
        Assert.Equal("KIA", MissionResultText.Fate(Soldier(SoldierFate.Dead)));
        Assert.Equal("MIA", MissionResultText.Fate(Soldier(SoldierFate.Mia)));
    }

    [Fact]
    public void EnemiesKilledUseDisplayNames()
    {
        Assert.Equal(
            "None",
            MissionResultText.EnemiesKilled(Result(new Dictionary<string, int>()), Data)
        );
        Assert.Equal(
            "1x Enforcer, 2x Warden",
            MissionResultText.EnemiesKilled(
                Result(new SortedDictionary<string, int> { ["warden"] = 2, ["enforcer"] = 1 }),
                Data
            )
        );
    }

    [Fact]
    public void LootListsWhatWasCollected()
    {
        Assert.Equal("Nothing", MissionResultText.Loot(Loot.Empty));
        Assert.Equal("8 Salvage, 1 Core", MissionResultText.Loot(new Loot(0, 8, 1)));
        Assert.Equal("1 Salvage, 20 Credits, 3 Cores", MissionResultText.Loot(new Loot(20, 1, 3)));
    }
}
