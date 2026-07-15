using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BattleGame.Cli.Ai;

/// <summary>
/// DeepSeek JSON 调用适配器。密钥只从运行环境注入，绝不写入源码或发布包。
/// </summary>
public sealed class DeepSeekJsonClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public DeepSeekJsonClient(HttpClient httpClient, string apiKey, string model)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException(nameof(apiKey))
            : apiKey;
        _model = string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException(nameof(model))
            : model;
    }

    public async Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            response_format = new { type = "json_object" },
            stream = false,
            temperature = 0.4
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.deepseek.com/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(responseJson);
        return document.RootElement
                   .GetProperty("choices")[0]
                   .GetProperty("message")
                   .GetProperty("content")
                   .GetString()
               ?? throw new InvalidOperationException("DeepSeek 返回了空内容。");
    }
}
