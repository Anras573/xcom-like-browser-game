namespace Firewall.Rules.Data;

public enum WeaponClass
{
    Rifle,
    Shotgun,
    Sniper,
    Lmg,
}

public enum WeaponFlag
{
    /// <summary>Cannot fire after moving this turn.</summary>
    NoFireAfterMove,
}

public enum Rank
{
    Rookie,
    Corporal,
    Sergeant,
    Lieutenant,
    Captain,
}

public enum TargetingType
{
    /// <summary>Always on; no activation.</summary>
    Passive,
    Self,
    Enemy,
    Ally,
    Tile,
}

public enum ItemKind
{
    Grenade,
    Medkit,
    Melee,
    Passive,
}

public enum Biome
{
    Industrial,
    Urban,
    Suburban,

    /// <summary>The Spire has no regular biome.</summary>
    None,
}

public enum EnemyKind
{
    Unit,

    /// <summary>Map objects with HP, such as the Relay Node.</summary>
    Object,
}

public enum AiKind
{
    /// <summary>Ignores cover, paths to the nearest target and melees.</summary>
    Charger,
    CoverShooter,
    Flanker,
    Suppressor,
    Boss,

    /// <summary>Does not act on its own (objects).</summary>
    Stationary,
}

public enum Resource
{
    Credits,
    Salvage,
    Cores,
}

public enum RequirementKind
{
    Research,
    EnemyKilled,
    EnemyCaptured,
    MissionWon,
    Salvage,
    Cores,

    /// <summary>A named story item, such as the Relay Core. Not checked against any table.</summary>
    KeyItem,
}

public enum WorkshopKind
{
    Item,
    Armor,
    TierUpgrade,
}
