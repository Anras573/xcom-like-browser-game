using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;
using Firewall.Web.Ui;
using Firewall.Web.Units;

namespace Firewall.Web.Tests;

public class TargetingModeTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    // A 20x3 yard; the crate at (7, 1) covers a unit on (8, 1) from the west.
    private static TacticalState Yard()
    {
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = [new string('.', 20), ".......c" + new string('.', 12), new string('.', 20)],
            },
            Data.Tiles
        );
        return new TacticalState(map, new Pcg32Random(1));
    }

    private static TacticalUnit Add(TacticalState state, Faction faction, string defId, int x)
    {
        var unit = new TacticalUnit(state.NextUnitId(), faction, defId, $"{defId} {x}")
        {
            Position = new GridPos(x, 1),
            Hp = 6,
            MaxHp = 6,
            Aim = 65,
            Sight = 20,
            ActionPoints = 2,
        };
        if (faction == Faction.Firewall)
        {
            var rifle = Data.Weapons["rifle_t1"];
            unit.Weapon = new WeaponState(rifle, rifle.Clip);
            unit.Abilities.Add(AbilityRegistry.Shoot);
        }
        state.AddUnit(unit);
        return unit;
    }

    [Fact]
    public void CandidatesAreSortedByHitChanceAndCycleBothWays()
    {
        var state = Yard();
        var shooter = Add(state, Faction.Firewall, "soldier", 0);
        var covered = Add(state, Faction.Meridian, "warden", 8);
        var open = Add(state, Faction.Meridian, "husk", 12);
        var engine = new TacticalEngine(state, Data);

        var mode = TargetingMode.TryBegin(engine, shooter, _ => true, out _)!;

        Assert.Equal([open.Id, covered.Id], mode.Candidates.Select(c => c.Unit.Id));
        Assert.Equal(open.Id, mode.Current.Unit.Id);
        mode.Cycle(1);
        Assert.Equal(covered.Id, mode.Current.Unit.Id);
        mode.Cycle(1);
        Assert.Equal(open.Id, mode.Current.Unit.Id);
        mode.Cycle(-1);
        Assert.Equal(covered.Id, mode.Current.Unit.Id);
        Assert.True(mode.Select(open.Id));
        Assert.False(mode.Select(shooter.Id));
        Assert.Equal(
            new UseAbilityCommand(shooter.Id, "shoot", Target.Unit(open.Id)),
            mode.Command
        );
    }

    [Fact]
    public void HiddenEnemiesAreNotCandidatesAndTheReasonExplainsWhy()
    {
        var state = Yard();
        var shooter = Add(state, Faction.Firewall, "soldier", 0);
        var husk = Add(state, Faction.Meridian, "husk", 5);
        var engine = new TacticalEngine(state, Data);

        Assert.Null(TargetingMode.TryBegin(engine, shooter, u => u != husk, out var none));
        Assert.Equal("No enemies in sight.", none);

        shooter.Weapon!.Ammo = 0;
        Assert.Null(TargetingMode.TryBegin(engine, shooter, _ => true, out var empty));
        Assert.Equal("Out of ammo.", empty);
    }

    [Theory]
    [InlineData(10, "+10")]
    [InlineData(-20, "-20")]
    [InlineData(0, "0")]
    public void BreakdownValuesAreSigned(int value, string text) =>
        Assert.Equal(text, ShotPanel.Signed(value));
}
