using System.Text.Json.Nodes;

public static class JsonBodyReader
{
    public static async Task<JsonNode?> ReadJsonBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is null or <= 0)
        {
            return null;
        }

        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var content = await reader.ReadToEndAsync();
        return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    }

    public static string? ReadString(JsonNode? node, string key)
    {
        return node?[key]?.GetValue<string>();
    }
}