using System.Text.Json;
using System.Text.Json.Serialization;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Data;

/// <summary>
/// Source-generated JSON metadata. Reflection-based serialization breaks under Blazor trimming,
/// so every (de)serialization in the rules goes through this context.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(List<WeaponDef>))]
[JsonSerializable(typeof(List<ArmorDef>))]
[JsonSerializable(typeof(List<ItemDef>))]
[JsonSerializable(typeof(List<AbilityDef>))]
[JsonSerializable(typeof(List<ClassDef>))]
[JsonSerializable(typeof(SoldierDef))]
[JsonSerializable(typeof(List<EnemyDef>))]
[JsonSerializable(typeof(List<EliteDef>))]
[JsonSerializable(typeof(List<ResearchDef>))]
[JsonSerializable(typeof(List<FacilityDef>))]
[JsonSerializable(typeof(List<WorkshopItemDef>))]
[JsonSerializable(typeof(List<MissionTypeDef>))]
[JsonSerializable(typeof(List<DistrictDef>))]
[JsonSerializable(typeof(NameLists))]
[JsonSerializable(typeof(Pcg32State))]
public sealed partial class RulesJsonContext : JsonSerializerContext;
