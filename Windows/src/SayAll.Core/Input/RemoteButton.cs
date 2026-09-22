using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SayAll.Core.Input;

[JsonConverter(typeof(RemoteButtonJsonConverter))]
public readonly record struct RemoteButton : IComparable<RemoteButton>
{
    public string Id { get; }
    public RemoteButton(string id)
    {
        if (!Regex.IsMatch(id ?? "", "^[A-Za-z][A-Za-z0-9]{0,47}$"))
            throw new ArgumentException("Invalid control ID.");
        Id = id!;
    }
    public int CompareTo(RemoteButton other) => string.CompareOrdinal(Id, other.Id);
    public override string ToString() => Id;
    public static bool TryParse(string? id, out RemoteButton button)
    {
        if (id is not null && Regex.IsMatch(id, "^[A-Za-z][A-Za-z0-9]{0,47}$"))
        { button = new(id); return true; }
        button = default; return false;
    }
    public static readonly RemoteButton Power = new("Power");
    public static readonly RemoteButton Up = new("Up");
    public static readonly RemoteButton Left = new("Left");
    public static readonly RemoteButton Ok = new("Ok");
    public static readonly RemoteButton Right = new("Right");
    public static readonly RemoteButton Down = new("Down");
    public static readonly RemoteButton Back = new("Back");
    public static readonly RemoteButton VolumeUp = new("VolumeUp");
    public static readonly RemoteButton Home = new("Home");
    public static readonly RemoteButton VolumeDown = new("VolumeDown");
    public static readonly RemoteButton Menu = new("Menu");
    public static readonly RemoteButton Tv = new("Tv");
}
public sealed class RemoteButtonJsonConverter : JsonConverter<RemoteButton>
{
    public override RemoteButton Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && RemoteButton.TryParse(reader.GetString(), out var button)
            ? button : throw new JsonException("Invalid control ID.");
    public override void Write(Utf8JsonWriter writer, RemoteButton value, JsonSerializerOptions options) => writer.WriteStringValue(value.Id);
}
public static class RemoteButtonCatalog
{
    public static IReadOnlyList<RemoteButton> StandardButtons { get; } =
        [RemoteButton.Power, RemoteButton.Up, RemoteButton.Left, RemoteButton.Ok, RemoteButton.Right,
         RemoteButton.Down, RemoteButton.Back, RemoteButton.VolumeUp, RemoteButton.Home,
         RemoteButton.VolumeDown, RemoteButton.Menu, RemoteButton.Tv];
    private static readonly IReadOnlyDictionary<ushort, RemoteButton> UsageMap =
        new Dictionary<ushort, RemoteButton>
        {
            [0x0066] = RemoteButton.Power,
            [0x0052] = RemoteButton.Up,
            [0x0050] = RemoteButton.Left,
            [0x0028] = RemoteButton.Ok,
            [0x004F] = RemoteButton.Right,
            [0x0051] = RemoteButton.Down,
            [0x00F1] = RemoteButton.Back,
            [0x0080] = RemoteButton.VolumeUp,
            [0x004A] = RemoteButton.Home,
            [0x0081] = RemoteButton.VolumeDown,
            [0x0065] = RemoteButton.Menu,
            [0x0035] = RemoteButton.Tv,
        };

    public static RemoteButton? FromUsage(ushort usage)
    {
        return UsageMap.TryGetValue(usage, out var button) ? button : null;
    }
}
