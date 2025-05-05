using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceLib.Common;
public class QueryTypeItemConverter : JsonConverter<QueryTypeItem>
{
    public override QueryTypeItem? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var item = new QueryTypeItem();

        if (reader.TokenType == JsonTokenType.String)
        {
            item.StringValue = reader.GetString();
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            item.IntValue = reader.GetInt32();
        }

        return item;
    }

    public override void Write(Utf8JsonWriter writer, QueryTypeItem value, JsonSerializerOptions options)
    {
        if (value.StringValue != null)
        {
            writer.WriteStringValue(value.StringValue);
        }
        else if (value.IntValue.HasValue)
        {
            writer.WriteNumberValue(value.IntValue.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
