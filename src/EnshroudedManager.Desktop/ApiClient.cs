using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnshroudedManager.Shared;

namespace EnshroudedManager.Desktop;

public class ApiException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>Talks to the EnshroudedManager service, exactly like the web page does.</summary>
public class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;

    public ApiClient(string baseUrl, string? token)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        // Updates can keep a request open for a long time.
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/api/"), Timeout = TimeSpan.FromMinutes(70) };
        Token = token;
    }

    public string BaseUrl { get; }

    public string? Token
    {
        get => _http.DefaultRequestHeaders.Authorization?.Parameter;
        set => _http.DefaultRequestHeaders.Authorization = value == null ? null : new AuthenticationHeaderValue("Bearer", value);
    }

    public Task<T> Get<T>(string path) => Send<T>(HttpMethod.Get, path, null);
    public Task<T> Post<T>(string path, object? body = null) => Send<T>(HttpMethod.Post, path, body);
    public Task<T> Put<T>(string path, object body) => Send<T>(HttpMethod.Put, path, body);

    public async Task<byte[]> GetBytes(string path)
    {
        using var res = await _http.GetAsync(path);
        await EnsureOk(res);
        return await res.Content.ReadAsByteArrayAsync();
    }

    private async Task<T> Send<T>(HttpMethod method, string path, object? body)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body != null) req.Content = JsonContent.Create(body, body.GetType(), options: Json);
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req); }
        catch (HttpRequestException ex) { throw new ApiException(L.T("Service injoignable : ", "Service unreachable: ") + ex.Message); }
        catch (TaskCanceledException) { throw new ApiException(L.T("Le service ne répond pas.", "The service is not responding.")); }
        using (res)
        {
            await EnsureOk(res);
            if (typeof(T) == typeof(object) || res.Content.Headers.ContentLength == 0) return default!;
            return (await res.Content.ReadFromJsonAsync<T>(Json))!;
        }
    }

    private static async Task EnsureOk(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        string? message = null;
        try
        {
            var m = await res.Content.ReadFromJsonAsync<MessageDto>(Json);
            if (!string.IsNullOrEmpty(m?.Message)) message = m.Message;
        }
        catch { }
        if (message == null && res.StatusCode == HttpStatusCode.Unauthorized) message = L.T("Session expirée, reconnecte-toi.", "Session expired, please sign in again.");
        throw new ApiException(message ?? L.T($"Erreur {(int)res.StatusCode}", $"Error {(int)res.StatusCode}"), res.StatusCode);
    }
}
