using System.Text.Json.Serialization;

namespace Firewall.Rules.Data;

/// <summary>Ability data. The behaviour is a code handler keyed by <see cref="Id"/> (#30).</summary>
public sealed record AbilityDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    /// <summary>
    /// Tooltip text. <c>{key}</c> placeholders take the number from <see cref="Parameters"/>
    /// (see <see cref="Describe"/>), so the numbers live in one place.
    /// </summary>
    [JsonRequired]
    public string Description { get; init; } = "";

    /// <summary>UI atlas region for the ability bar and squad builder, such as <c>icon.target</c>.</summary>
    public string? Icon { get; init; }

    /// <summary>Uses per mission; 0 means unlimited.</summary>
    public int Charges { get; init; }

    /// <summary>Using it shows its name as floating text ("RUN &amp; GUN", "HEADSHOT").</summary>
    public bool Callout { get; init; }

    [JsonRequired]
    public int ApCost { get; init; }

    [JsonRequired]
    public bool EndsTurn { get; init; }
    public int Cooldown { get; init; }

    [JsonRequired]
    public TargetingType Targeting { get; init; }
    private readonly IReadOnlyDictionary<string, double>? _parameters;
    public IReadOnlyDictionary<string, double> Parameters
    {
        get => _parameters ?? Maps.Empty;
        init => _parameters = value;
    }

    /// <summary>
    /// <see cref="Description"/> with every <c>{key}</c> replaced by that parameter's value;
    /// <c>{apCost}</c>, <c>{cooldown}</c> and <c>{charges}</c> are the ability's own fields.
    /// </summary>
    public string Describe() => AbilityText.Fill(Description, AbilityText.Values(this));
}

/// <summary>Fills <c>{key}</c> placeholders from an ability's parameters.</summary>
public static class AbilityText
{
    private static readonly System.Text.RegularExpressions.Regex Placeholder = new(
        @"\{(\w+)\}",
        System.Text.RegularExpressions.RegexOptions.Compiled
    );

    /// <summary>The numbers a description can use: the parameters and the ability's own cost fields.</summary>
    public static IReadOnlyDictionary<string, double> Values(AbilityDef def)
    {
        var values = new Dictionary<string, double>
        {
            ["apCost"] = def.ApCost,
            ["cooldown"] = def.Cooldown,
            ["charges"] = def.Charges,
        };
        foreach (var (key, value) in def.Parameters)
            values[key] = value;
        return values;
    }

    /// <summary>The placeholder keys in <paramref name="template"/>.</summary>
    public static IEnumerable<string> Keys(string template) =>
        Placeholder.Matches(template).Select(m => m.Groups[1].Value);

    /// <summary>A key with no parameter is left as written, so a typo is visible.</summary>
    public static string Fill(string template, IReadOnlyDictionary<string, double> parameters) =>
        Placeholder.Replace(
            template,
            m =>
                parameters.TryGetValue(m.Groups[1].Value, out var value)
                    ? value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    : m.Value
        );
}
