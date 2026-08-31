using System.Text;
using System.Text.Json;

namespace FleetStream.Application.Shared.Pagination;

public static class CursorEncoder
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public sealed record TruckCursor(string Id);

    public sealed record AlertCursor(string Id, string Ts);

    public static string Encode<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        return Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    public static T? Decode<T>(string? cursor) where T : class
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var bytes = Base64UrlDecode(cursor);
            return JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch
        {
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string encoded)
    {
        var padded = encoded.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
