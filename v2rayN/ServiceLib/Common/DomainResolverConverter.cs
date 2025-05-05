using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;

namespace ServiceLib.Common;

public class DomainResolverConverter : JsonConverter<Rule4Sbox>
{
    public override Rule4Sbox Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => new Rule4Sbox { server = reader.GetString() },
            JsonTokenType.StartObject => DeserializeObject(ref reader, options),
            _ => null
        };
    }

    public override void Write(Utf8JsonWriter writer, Rule4Sbox value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.server != null && IsOnlyServerPropertySet(value))
        {
            writer.WriteStringValue(value.server);
            return;
        }

        var newOptions = RemoveThisConverter(options);
        JsonSerializer.Serialize(writer, value, newOptions);
    }

    private Rule4Sbox DeserializeObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var newOptions = RemoveThisConverter(options);
        using var doc = JsonDocument.ParseValue(ref reader);
        return JsonSerializer.Deserialize<Rule4Sbox>(doc.RootElement.GetRawText(), newOptions);
    }

    private JsonSerializerOptions RemoveThisConverter(JsonSerializerOptions options)
    {
        var newOptions = new JsonSerializerOptions(options);
        
        for (var i = newOptions.Converters.Count - 1; i >= 0; i--)
        {
            if (newOptions.Converters[i] is DomainResolverConverter)
            {
                newOptions.Converters.RemoveAt(i);
            }
        }
        
        return newOptions;
    }

    private bool IsOnlyServerPropertySet(Rule4Sbox rule)
    {
        foreach (var property in typeof(Rule4Sbox).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == "server")
            {
                continue;
            }

            if (property.GetValue(rule) != null)
            {
                return false;
            }
        }
        
        return true;
    }
}
