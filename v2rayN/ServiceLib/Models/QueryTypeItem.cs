using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceLib.Models;
[JsonConverter(typeof(QueryTypeItemConverter))]
public class QueryTypeItem
{
    public string? StringValue { get; set; }
    public int? IntValue { get; set; }

    public override string ToString()
    {
        return StringValue ?? IntValue?.ToString() ?? "";
    }
}
