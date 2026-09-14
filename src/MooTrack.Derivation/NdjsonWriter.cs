using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MooTrack.Derivation;

public static class NdjsonWriter
{
    // The record is read by humans as well as the engine; the default encoder
    // escapes the offset's plus sign, which makes a durable log unreadable.
    private static readonly JsonWriterOptions Readable =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Line(Observation observation)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Readable))
        {
            json.WriteStartObject();
            json.WriteString("tsUtc", observation.Timestamp.UtcDateTime
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            json.WriteString("tsOffset", Offset(observation.Timestamp.Offset));
            json.WriteNumber("unbiasedMs", observation.UnbiasedMs);
            json.WriteString("host", observation.Host);
            json.WriteString("user", observation.User);
            json.WriteString("event", observation.Event.ToString());
            json.WriteString("source", observation.Source);
            json.WriteString("dedupeKey", DedupeKey(observation));
            json.WriteString("detail", observation.Detail);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static string DedupeKey(Observation observation)
    {
        var seed = String.Join('|',
            observation.Host,
            observation.User,
            observation.Event.ToString(),
            observation.Timestamp.UtcDateTime.ToString(
                "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
    }

    private static string Offset(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+")
        + offset.Duration().ToString("hh\\:mm", CultureInfo.InvariantCulture);
}
