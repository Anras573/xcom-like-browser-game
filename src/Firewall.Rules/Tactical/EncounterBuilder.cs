using Firewall.Rules.Data;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Tactical;

/// <summary>
/// Picks the pods of a mission from <c>data/encounters.json</c> (GDD §5.6). The result depends only
/// on the Force Level, the mission type and the RNG, so a seed always gives the same enemies.
/// </summary>
public static class EncounterBuilder
{
    public const int MinForceLevel = 1;
    public const int MaxForceLevel = 10;

    /// <summary>How many pods a mission type brings (inclusive range): Purge 3-4, Rescue 3, Sabotage 3, Capture 2-3.</summary>
    public static (int Min, int Max) PodCountRange(string missionType) =>
        missionType switch
        {
            "rescue" or "sabotage" => (3, 3),
            "capture" => (2, 3),
            _ => (3, 4),
        };

    public static int PodCount(string missionType, IRandom rng)
    {
        var (min, max) = PodCountRange(missionType);
        return rng.Fork("pod-count").Next(min, max + 1);
    }

    /// <summary>
    /// <paramref name="podCount"/> pods for <paramref name="forceLevel"/> (clamped to 1-10). Rescue
    /// leans on Husks, and Capture always has a pod with a Proxy, first in the list.
    /// </summary>
    public static IReadOnlyList<PodSpec> Build(
        GameData data,
        int forceLevel,
        int podCount,
        IRandom rng,
        string missionType = "purge"
    )
    {
        var level = Math.Clamp(forceLevel, MinForceLevel, MaxForceLevel);
        var templates = data.Encounters[level.ToString()].Pods;
        var pick = rng.Fork($"encounter:{level}:{missionType}");

        var pods = new List<PodSpec>();
        if (missionType == "capture" && podCount > 0)
            pods.Add(CaptureBait(data, templates, pick));
        while (pods.Count < podCount)
        {
            var weights = templates.Select(t => Weight(data, t, missionType)).ToList();
            pods.Add(ToSpec(data, templates[PickIndex(weights, pick)]));
        }
        return pods;
    }

    /// <summary>The templates' share of the roll for <paramref name="missionType"/>; Rescue favours Husk-heavy pods.</summary>
    public static double Weight(GameData data, PodTemplateDef template, string missionType)
    {
        if (missionType != "rescue")
            return template.Weight;
        var husks = template.Units.Count(u => u == "husk");
        return template.Weight * (1 + 3.0 * husks / Math.Max(1, template.Units.Count));
    }

    private static PodSpec CaptureBait(
        GameData data,
        IReadOnlyList<PodTemplateDef> templates,
        IRandom rng
    )
    {
        var withProxy = templates.Where(t => t.Units.Contains("proxy")).ToList();
        if (withProxy.Count == 0)
            return ToSpec(
                data,
                new PodTemplateDef { Weight = 1, Units = ["proxy", "husk", "husk"] }
            );
        return ToSpec(
            data,
            withProxy[PickIndex(withProxy.Select(t => (double)t.Weight).ToList(), rng)]
        );
    }

    /// <summary>The index picked by a weighted roll.</summary>
    public static int PickIndex(IReadOnlyList<double> weights, IRandom rng)
    {
        var roll = rng.NextDouble() * weights.Sum();
        for (var i = 0; i < weights.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0)
                return i;
        }
        return weights.Count - 1;
    }

    /// <summary>The pod of a template: elite tiers swap in variant ids; the banner is the strongest member's.</summary>
    public static PodSpec ToSpec(GameData data, PodTemplateDef template)
    {
        var ids = template
            .Units.Select(id =>
                template.Tier > 0
                    ? EliteTransform.VariantId(id, data.Elites[template.Tier - 1])
                    : id
            )
            .ToList();
        var name =
            template.Name
            ?? ids.Select(id => data.Enemies[id])
                .OrderByDescending(d => d.Hp)
                .Select(d => d.Banner ?? d.Name.ToUpperInvariant())
                .First();
        return new PodSpec(name, ids);
    }
}
