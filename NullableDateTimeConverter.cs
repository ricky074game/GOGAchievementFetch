using System.Text.Json;
using System.Text.Json.Serialization;

namespace GOGAchievementFetch;

public class NullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String && DateTime.TryParse(reader.GetString(), out var date))
        {
            return date;
        }

        throw new JsonException($"Unable to convert \"{reader.GetString()}\" to DateTime.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString("o")); // Use ISO 8601 format
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}