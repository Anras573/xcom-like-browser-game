using System.Text;
using System.Text.Json;

namespace Firewall.Rules.Tactical;

/// <summary>The seed and every command the engine accepted: enough to replay a mission exactly.</summary>
public sealed class CommandLog(ulong seed)
{
    private readonly List<ICommand> _commands = [];

    public ulong Seed { get; } = seed;

    public IReadOnlyList<ICommand> Commands => _commands;

    internal void Add(ICommand cmd) => _commands.Add(cmd);

    /// <summary>Hand-written JSON (no reflection, so it survives trimming) for bug reports.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("seed", Seed);
            w.WriteStartArray("commands");
            foreach (var cmd in _commands)
            {
                w.WriteStartObject();
                switch (cmd)
                {
                    case MoveCommand m:
                        w.WriteString("type", "move");
                        w.WriteNumber("unit", m.UnitId.Value);
                        w.WriteNumber("x", m.Dest.X);
                        w.WriteNumber("y", m.Dest.Y);
                        break;
                    case UseAbilityCommand a:
                        w.WriteString("type", "ability");
                        w.WriteNumber("unit", a.UnitId.Value);
                        w.WriteString("ability", a.AbilityId);
                        switch (a.Target)
                        {
                            case UnitTarget u:
                                w.WriteNumber("targetUnit", u.Id.Value);
                                break;
                            case TileTarget t:
                                w.WriteNumber("targetX", t.Pos.X);
                                w.WriteNumber("targetY", t.Pos.Y);
                                break;
                        }
                        break;
                    case EndTurnCommand:
                        w.WriteString("type", "endTurn");
                        break;
                    default:
                        w.WriteString("type", cmd.GetType().Name);
                        break;
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
