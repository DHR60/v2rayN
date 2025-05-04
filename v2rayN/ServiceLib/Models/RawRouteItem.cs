using SQLite;

namespace ServiceLib.Models;

[Serializable]
public class RawRouteItem
{
    [PrimaryKey]
    public string Id { get; set; }

    public string Remarks { get; set; }
    public bool Enabled { get; set; } = false;
    public ECoreType CoreType { get; set; }
    public string? Route { get; set; }
    public string? TunRoute { get; set; }
}
