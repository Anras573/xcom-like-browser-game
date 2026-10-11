namespace Firewall.Rules.Data;

/// <summary>A definition with a unique string id.</summary>
public interface IDef
{
    string Id { get; }
}

internal static class Maps
{
    public static readonly IReadOnlyDictionary<string, double> Empty =
        new Dictionary<string, double>();
}
