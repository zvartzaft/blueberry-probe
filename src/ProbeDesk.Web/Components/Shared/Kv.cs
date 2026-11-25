namespace ProbeDesk.Web.Components.Shared;

/// <summary>Mutable name/value pair used by the key/value editor (the domain model uses immutable records).</summary>
public sealed class Kv
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}
