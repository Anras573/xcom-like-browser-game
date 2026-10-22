using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;
using Firewall.Web.Playback;
using Firewall.Web.Ui;
using Firewall.Web.Units;

namespace Firewall.Web.Tests;

/// <summary>Targeting UI and playback for the utility items (#31).</summary>
public class ItemUiTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    // A wall column at x = 10 splits the yard; the squad stands at the west end.
    private static TacticalState Yard()
    {
        var rows = Enumerable.Repeat("..........#.........", 5).ToArray();
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = rows,
            },
            Data.Tiles
        );
        return new TacticalState(map, new Pcg32Random(3));
    }

    private static TacticalUnit Soldier(TacticalState state, int x, params string[] items)
    {
        var spec = new SoldierSpec { ItemIds = items };
        var unit = SoldierBuilder.Build(state, Data, spec, "Test", "soldier1");
        unit.Position = new GridPos(x, 2);
        unit.Hp = unit.MaxHp = 20;
        unit.Sight = 30;
        state.AddUnit(unit);
        return unit;
    }

    private static TacticalUnit Husk(TacticalState state, int x, int y = 2)
    {
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Meridian, "husk", "Husk")
        {
            Position = new GridPos(x, y),
            Hp = 20,
            MaxHp = 20,
            Sight = 30,
            ActionPoints = 2,
        };
        state.AddUnit(unit);
        return unit;
    }

    // ------------------------------------------------------------------ tile targeting

    [Fact]
    public void TheThrowRingIsClampedByLineOfSightAndRange()
    {
        var state = Yard();
        var thrower = Soldier(state, 2, "frag_grenade");
        Husk(state, 19, 4);
        var engine = new TacticalEngine(state, Data);

        var mode = TileTargetingMode.TryBegin(engine, thrower, "frag_grenade", out _)!;

        Assert.True(mode.CanTarget(new GridPos(10, 2))); // the wall tile itself can be hit
        Assert.False(mode.CanTarget(new GridPos(12, 2))); // behind the wall
        Assert.False(mode.CanTarget(new GridPos(13, 2))); // out of range as well
        Assert.True(mode.CanTarget(new GridPos(2, 2)));
        Assert.All(mode.Throwable, t => Assert.True(t.DistanceTo(thrower.Position) <= 10));
    }

    [Fact]
    public void HoveringATilePreviewsTheFootprintVictimsAndTheBreakage()
    {
        var state = Yard();
        state.Map.SetTile(new GridPos(7, 2), TileType.SmallCrate);
        var thrower = Soldier(state, 2, "frag_grenade");
        var friend = Soldier(state, 8);
        var enemy = Husk(state, 6, 3);
        var engine = new TacticalEngine(state, Data);
        var mode = TileTargetingMode.TryBegin(engine, thrower, "frag_grenade", out _)!;

        var preview = mode.PreviewAt(new GridPos(7, 2))!;

        Assert.Equal(9, preview.Tiles.Count);
        Assert.Equal(3, preview.Damage);
        Assert.Contains(preview.Victims, v => v.Unit == enemy && !v.Friendly);
        Assert.Contains(preview.Victims, v => v.Unit == friend && v.Friendly);
        Assert.Equal(new GridPos(7, 2), Assert.Single(preview.Breakage).Pos);
        Assert.Equal(
            [
                "Affects: 1 enemy, 1 ally",
                "Damage: 3",
                "Cover destroyed: 1",
                "Friendly fire: 1 ally",
            ],
            ItemPanelText.Lines(preview)
        );
        Assert.Null(mode.PreviewAt(new GridPos(15, 2))); // not throwable: no preview
        Assert.Null(mode.PreviewAt(null));
    }

    [Fact]
    public void TheModeGoesStaleWhenTheMapChanges()
    {
        var state = Yard();
        var thrower = Soldier(state, 2, "frag_grenade");
        Husk(state, 19, 4);
        var engine = new TacticalEngine(state, Data);
        var mode = TileTargetingMode.TryBegin(engine, thrower, "frag_grenade", out _)!;
        Assert.False(mode.IsStale);

        state.Map.SetTile(new GridPos(10, 2), TileType.Floor);

        Assert.True(mode.IsStale);
    }

    [Fact]
    public void NoChargesOrNoApSaysWhyInsteadOfEnteringTargeting()
    {
        var state = Yard();
        var thrower = Soldier(state, 2, "frag_grenade");
        Husk(state, 19, 4);
        var engine = new TacticalEngine(state, Data);
        thrower.Items[0].Charges = 0;

        var mode = TileTargetingMode.TryBegin(engine, thrower, "frag_grenade", out var reason);

        Assert.Null(mode);
        Assert.Contains("no charges", reason);
    }

    [Fact]
    public void CommandsThrowAtTheChosenTile()
    {
        var state = Yard();
        var thrower = Soldier(state, 2, "smoke_grenade");
        Husk(state, 19, 4);
        var engine = new TacticalEngine(state, Data);
        var mode = TileTargetingMode.TryBegin(engine, thrower, "smoke_grenade", out _)!;

        var cmd = mode.CommandFor(new GridPos(6, 2));

        Assert.True(engine.Validate(cmd).IsValid);
        Assert.Equal(new TileTarget(new GridPos(6, 2)), cmd.Target);
    }

    // ------------------------------------------------------------------ unit targeting

    [Fact]
    public void CarryTargetsDownedSquadmatesAndMedkitIncludesTheUser()
    {
        var state = Yard();
        var medic = Soldier(state, 3, "medkit");
        var downed = Soldier(state, 4);
        downed.Hp = 0;
        Casualties.Drop(downed, new Pcg32Random(1), []);
        downed.Statuses.BleedingOutTurnsLeft = 3;
        downed.IsAlive = true;
        medic.Hp = 5;
        Husk(state, 19, 4);
        var engine = new TacticalEngine(state, Data);

        var carry = TargetingMode.TryBegin(engine, medic, _ => true, out _, "carry")!;
        var kit = TargetingMode.TryBegin(engine, medic, _ => true, out _, "medkit")!;

        Assert.Equal(downed, Assert.Single(carry.Candidates).Unit);
        Assert.True(carry.TargetsAnyUnit);
        Assert.Contains(kit.Candidates, c => c.Unit == medic);
        Assert.Contains(kit.Candidates, c => c.Unit == downed);
    }

    // ------------------------------------------------------------------ playback

    private static (EventPlayer Player, ViewModel Vm, List<GameEvent> Seen) Playback(
        TacticalState state
    )
    {
        var vm = ViewModel.FromState(state, Data);
        var seen = new List<GameEvent>();
        var ctx = new EventPlayerContext { SceneEvent = seen.Add };
        return (new EventPlayer(vm, ctx, AnimationFactory.Create), vm, seen);
    }

    private static void Run(EventPlayer player)
    {
        for (var i = 0; i < 1000 && player.IsBusy; i++)
            player.Update(0.016f);
        Assert.False(player.IsBusy);
    }

    [Fact]
    public void CarriedUnitsRideOnTheirCarrierUntilDropped()
    {
        var state = Yard();
        var carrier = Soldier(state, 3);
        var load = Soldier(state, 4);
        Husk(state, 19, 4);
        var (player, vm, _) = Playback(state);

        player.Enqueue([new UnitCarried(carrier.Id, load.Id)]);
        Run(player);
        Assert.Equal(carrier.Id, vm.Units[load.Id].CarriedBy);

        player.Enqueue([new UnitDropped(carrier.Id, load.Id, new GridPos(5, 3))]);
        Run(player);
        Assert.Null(vm.Units[load.Id].CarriedBy);
        Assert.Equal(new GridPos(5, 3), vm.Units[load.Id].Tile);
    }

    [Fact]
    public void SmokeAppearsAndExpiresInTheViewModel()
    {
        var state = Yard();
        Soldier(state, 3);
        Husk(state, 19, 4);
        var (player, vm, _) = Playback(state);
        GridPos[] tiles = [new(5, 2), new(6, 2)];

        player.Enqueue([new SmokeCreated(tiles, 2)]);
        Run(player);
        Assert.Single(vm.Smoke);

        player.Enqueue([new SmokeExpired(tiles)]);
        Run(player);
        Assert.Empty(vm.Smoke);
    }

    [Fact]
    public void ATileChangeReachesTheSceneSoTheTilemapsRedrawWhenPlaybackGetsThere()
    {
        var state = Yard();
        Soldier(state, 3);
        Husk(state, 19, 4);
        var (player, _, seen) = Playback(state);
        var change = new TileChanged(new GridPos(5, 2), TileType.SmallCrate, TileType.Floor);

        player.Enqueue([change]);
        Run(player);

        Assert.Contains(change, seen);
    }

    [Fact]
    public void HealingUpdatesHpAndRevivingStandsTheSoldierUp()
    {
        var state = Yard();
        var patient = Soldier(state, 3);
        patient.Hp = 4;
        Husk(state, 19, 4);
        var (player, vm, _) = Playback(state);

        player.Enqueue([new UnitHealed(patient.Id, 4, 8)]);
        Run(player);
        Assert.Equal(8, vm.Units[patient.Id].Hp);

        vm.Units[patient.Id].Statuses.BleedingOutTurnsLeft = 2;
        vm.Units[patient.Id].Rotation = 1f;
        player.Enqueue([new UnitRevived(patient.Id, 1)]);
        Run(player);
        Assert.False(vm.Units[patient.Id].Statuses.IsBleedingOut);
        Assert.Equal(1, vm.Units[patient.Id].Hp);
        Assert.True(vm.Units[patient.Id].Rotation < 0f);
    }

    [Fact]
    public void TheViewModelStartsWithTheSmokeAndCarriersOfTheState()
    {
        var state = Yard();
        var carrier = Soldier(state, 3);
        var load = Soldier(state, 4);
        load.Hp = 0;
        load.Statuses.Set(StatusFlags.Stabilized);
        Husk(state, 19, 4);
        carrier.Carrying = load;
        load.CarriedBy = carrier;
        state.AddSmoke([new GridPos(6, 2)], 2, 20);

        var vm = ViewModel.FromState(state, Data);

        Assert.Equal(carrier.Id, vm.Units[load.Id].CarriedBy);
        Assert.Single(vm.Smoke);
    }

    [Fact]
    public void TheTooltipSaysWhoIsCapturableOrCarrying()
    {
        var state = Yard();
        var carrier = Soldier(state, 3);
        var load = Soldier(state, 4);
        carrier.Carrying = load;
        var proxy = Husk(state, 8);
        proxy.Statuses.Set(StatusFlags.Stunned);
        proxy.Statuses.Set(StatusFlags.Capturable);

        Assert.Contains("Carrying Test", UnitPresentation.TooltipLines(carrier, null));
        Assert.Contains("Stunned: can be captured", UnitPresentation.TooltipLines(proxy, null));
    }
}
