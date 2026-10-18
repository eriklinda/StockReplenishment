using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockReplenishment.Web.Services;

public class RequestsApiClient(HttpClient client)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<T> Get<T>(string path, CancellationToken ct = default)
    {
        using var response = await client.GetAsync(path, ct);
        return await Read<T>(response, ct);
    }

    public async Task<T> Post<T>(string path, object? body = null)
    {
        using var response = await client.PostAsJsonAsync(path, body, Json);
        return await Read<T>(response, default);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await GetErrorMessage(response, ct), null, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new HttpRequestException("The server returned an empty response.");
    }

    /// <summary>Uses the Problem Details title and any validation errors, if the body has them.</summary>
    private static async Task<string> GetErrorMessage(HttpResponseMessage response, CancellationToken ct)
    {
        var message = $"Request failed ({(int)response.StatusCode}).";
        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            if (problem.RootElement.TryGetProperty("title", out var title))
                message = title.GetString() ?? message;

            if (problem.RootElement.TryGetProperty("errors", out var errors))
            {
                var details = errors.EnumerateObject().SelectMany(error => error.Value.EnumerateArray()).Select(x => x.GetString());
                message += " " + string.Join(" ", details);
            }
        }
        catch (JsonException)
        {
        }

        return message;
    }
}
