using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Firewall.Rules.Data;

/// <summary>Definitions of one kind with lookup by id.</summary>
public sealed class DefTable<T> : IReadOnlyList<T>
    where T : IDef
{
    private readonly IReadOnlyList<T> _items;
    private readonly Dictionary<string, T> _byId = new(StringComparer.Ordinal);

    public DefTable(IEnumerable<T> items)
    {
        _items = items.ToList();
        // Duplicates are reported by GameData.Validate(); the first one wins here.
        foreach (var item in _items)
            _byId.TryAdd(item.Id, item);
    }

    public int Count => _items.Count;
    public T this[int index] => _items[index];
    public T this[string id] =>
        _byId.TryGetValue(id, out var item)
            ? item
            : throw new KeyNotFoundException($"Unknown {typeof(T).Name} '{id}'.");

    public bool Contains(string id) => _byId.ContainsKey(id);

    public bool TryGet(string id, out T item) => _byId.TryGetValue(id, out item!);

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

/// <summary>The files <see cref="GameData.Load"/> reads, relative to the <c>data/</c> folder.</summary>
public static class GameDataFiles
{
    public const string Weapons = "weapons.json";
    public const string Armor = "armor.json";
    public const string Items = "items.json";
    public const string Abilities = "abilities.json";
    public const string Classes = "classes.json";
    public const string Soldier = "soldier.json";
    public const string Enemies = "enemies.json";
    public const string Elites = "elites.json";
    public const string Research = "research.json";
    public const string Facilities = "facilities.json";
    public const string Workshop = "workshop.json";
    public const string Missions = "missions.json";
    public const string Districts = "districts.json";
    public const string Names = "names.json";

    public static IReadOnlyList<string> All { get; } =
    [
        Weapons,
        Armor,
        Items,
        Abilities,
        Classes,
        Soldier,
        Enemies,
        Elites,
        Research,
        Facilities,
        Workshop,
        Missions,
        Districts,
        Names,
    ];
}

/// <summary>Every content definition, loaded from JSON, with id lookup and validation.</summary>
public sealed class GameData
{
    public static readonly GameData Empty = new(
        [],
        [],
        [],
        [],
        [],
        new SoldierDef
        {
            Hp = 1,
            HpBonusRanks = [],
            Aim = 0,
            AimPerRank = 0,
            Mobility = 0,
            Defense = 0,
            Sight = 0,
            Ranks = [],
            StartingWeaponClass = WeaponClass.Rifle,
            AppearanceSets = [],
        },
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        new NameLists
        {
            FirstNames = [],
            LastNames = [],
            Nicknames = [],
        }
    );

    public GameData(
        IEnumerable<WeaponDef> weapons,
        IEnumerable<ArmorDef> armor,
        IEnumerable<ItemDef> items,
        IEnumerable<AbilityDef> abilities,
        IEnumerable<ClassDef> classes,
        SoldierDef soldier,
        IEnumerable<EnemyDef> enemies,
        IEnumerable<EliteDef> elites,
        IEnumerable<ResearchDef> research,
        IEnumerable<FacilityDef> facilities,
        IEnumerable<WorkshopItemDef> workshop,
        IEnumerable<MissionTypeDef> missions,
        IEnumerable<DistrictDef> districts,
        NameLists names
    )
    {
        Weapons = new(weapons);
        Armor = new(armor);
        Items = new(items);
        Abilities = new(abilities);
        Classes = new(classes);
        Soldier = soldier;
        Enemies = new(enemies);
        Elites = new(elites);
        Research = new(research);
        Facilities = new(facilities);
        Workshop = new(workshop);
        Missions = new(missions);
        Districts = new(districts);
        Names = names;
    }

    public DefTable<WeaponDef> Weapons { get; }
    public DefTable<ArmorDef> Armor { get; }
    public DefTable<ItemDef> Items { get; }
    public DefTable<AbilityDef> Abilities { get; }
    public DefTable<ClassDef> Classes { get; }
    public SoldierDef Soldier { get; }
    public DefTable<EnemyDef> Enemies { get; }
    public DefTable<EliteDef> Elites { get; }
    public DefTable<ResearchDef> Research { get; }
    public DefTable<FacilityDef> Facilities { get; }
    public DefTable<WorkshopItemDef> Workshop { get; }
    public DefTable<MissionTypeDef> Missions { get; }
    public DefTable<DistrictDef> Districts { get; }
    public NameLists Names { get; }

    /// <summary>The weapon of a class at a tier (1 = Ballistic, 2 = Coil, 3 = Arc).</summary>
    public WeaponDef Weapon(WeaponClass weaponClass, int tier) =>
        Weapons.First(w => w.Class == weaponClass && w.Tier == tier);

    /// <summary>
    /// Parses every data file. <paramref name="readFile"/> maps a file name from
    /// <see cref="GameDataFiles"/> to its JSON text.
    /// </summary>
    public static GameData Load(Func<string, string> readFile)
    {
        var ctx = RulesJsonContext.Default;
        return new GameData(
            Read(readFile, GameDataFiles.Weapons, ctx.ListWeaponDef),
            Read(readFile, GameDataFiles.Armor, ctx.ListArmorDef),
            Read(readFile, GameDataFiles.Items, ctx.ListItemDef),
            Read(readFile, GameDataFiles.Abilities, ctx.ListAbilityDef),
            Read(readFile, GameDataFiles.Classes, ctx.ListClassDef),
            Read(readFile, GameDataFiles.Soldier, ctx.SoldierDef),
            Read(readFile, GameDataFiles.Enemies, ctx.ListEnemyDef),
            Read(readFile, GameDataFiles.Elites, ctx.ListEliteDef),
            Read(readFile, GameDataFiles.Research, ctx.ListResearchDef),
            Read(readFile, GameDataFiles.Facilities, ctx.ListFacilityDef),
            Read(readFile, GameDataFiles.Workshop, ctx.ListWorkshopItemDef),
            Read(readFile, GameDataFiles.Missions, ctx.ListMissionTypeDef),
            Read(readFile, GameDataFiles.Districts, ctx.ListDistrictDef),
            Read(readFile, GameDataFiles.Names, ctx.NameLists)
        );
    }

    private static T Read<T>(Func<string, string> readFile, string file, JsonTypeInfo<T> info)
    {
        try
        {
            return JsonSerializer.Deserialize(readFile(file), info)
                ?? throw new InvalidDataException("file is empty");
        }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"{file}: {e.Message}", e);
        }
    }

    /// <summary>Checks references, ids and values. An empty list means the data is consistent.</summary>
    public IReadOnlyList<string> Validate() => GameDataValidator.Validate(this);
}
