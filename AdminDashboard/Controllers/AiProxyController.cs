using AdminDashboard.Security;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AdminDashboard.Controllers;

[ApiController]
[Route("api/ai")]
public class AiProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AiProxyController> _logger;

    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _rateLimitStore = new();
    private const int MaxRequestsPerMinute = 20;
    private const string GroqBaseUrl = "https://api.groq.com/openai/v1/chat/completions";
    private const string GroqModel = "llama-3.3-70b-versatile";

    public AiProxyController(IHttpClientFactory httpFactory, IConfiguration config, ILogger<AiProxyController> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    [HttpPost("chat")]
    [AdminAuth]
    public async Task<IActionResult> AdminChat([FromBody] AiChatRequest req)
        => await ForwardToGroq(req, "admin");

    [HttpPost("customer-chat")]
    [CustomerAuth]
    public async Task<IActionResult> CustomerChat([FromBody] AiChatRequest req)
    {
        var customerId = HttpContext.Items["customerId"]?.ToString() ?? "anon";
        if (!IsAllowed(customerId))
            return StatusCode(429, new { error = "لقد تجاوزت الحد المسموح به. يرجى الانتظار دقيقة." });
        return await ForwardToGroq(req, "customer");
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var apiKey = _config["Groq:ApiKey"] ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
            return Ok(new { available = false, provider = "groq", message = "❌ Groq API Key غير مضبوط" });
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            var res = await client.GetAsync("https://api.groq.com/openai/v1/models");
            if (res.IsSuccessStatusCode)
                return Ok(new { available = true, provider = "groq", model = GroqModel, message = "✅ Groq جاهز" });
            var body = await res.Content.ReadAsStringAsync();
            return Ok(new { available = false, message = $"❌ خطأ {(int)res.StatusCode}", details = body[..Math.Min(200, body.Length)] });
        }
        catch (Exception ex)
        {
            return Ok(new { available = false, message = $"❌ {ex.Message}" });
        }
    }

    private static bool IsAllowed(string key)
    {
        var now = DateTime.UtcNow;
        var entry = _rateLimitStore.GetOrAdd(key, _ => (0, now));
        if ((now - entry.WindowStart).TotalMinutes >= 1) { _rateLimitStore[key] = (1, now); return true; }
        if (entry.Count >= MaxRequestsPerMinute) return false;
        _rateLimitStore[key] = (entry.Count + 1, entry.WindowStart);
        return true;
    }

    private async Task<IActionResult> ForwardToGroq(AiChatRequest req, string caller)
    {
        if (req.Messages == null || req.Messages.Count == 0)
            return BadRequest(new { error = "messages required" });

        var apiKey = _config["Groq:ApiKey"] ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
            return StatusCode(503, new { error = "Groq API Key غير مضبوط في appsettings.json" });

        var maxTokens = Math.Clamp(req.MaxTokens > 0 ? req.MaxTokens : (caller == "customer" ? 800 : 1200), 100, 4096);

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(req.System))
            messages.Add(new { role = "system", content = req.System });

        foreach (var msg in req.Messages)
        {
            if (string.IsNullOrWhiteSpace(msg.Role) || string.IsNullOrWhiteSpace(msg.Content)) continue;
            messages.Add(new { role = msg.Role.ToLower(), content = msg.Content });
        }

        var payloadObj = new
        {
            model = GroqModel,
            messages,
            max_tokens = maxTokens,
            temperature = caller == "customer" ? 0.65 : 0.7,
            top_p = 0.9,
            stream = false
        };

        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(caller == "customer" ? 30 : 45);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var content = new StringContent(JsonSerializer.Serialize(payloadObj), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(GroqBaseUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseBody);
                var text = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "";

                _logger.LogInformation("Groq response OK for {Caller}", caller);
                return Ok(new
                {
                    id = $"groq-{Guid.NewGuid():N}",
                    type = "message", role = "assistant",
                    content = new[] { new { type = "text", text } },
                    model = GroqModel, stop_reason = "end_turn",
                    usage = new { input_tokens = 0, output_tokens = 0 },
                    provider = "groq"
                });
            }
            _logger.LogWarning("Groq error {Status}: {Body}", response.StatusCode, responseBody[..Math.Min(300, responseBody.Length)]);
            return StatusCode(503, new { error = $"خطأ من Groq: {(int)response.StatusCode}" });
        }
        catch (TaskCanceledException) { return StatusCode(503, new { error = "انتهت مهلة الاتصال بـ AI." }); }
        catch (Exception ex) { return StatusCode(503, new { error = $"خطأ: {ex.Message}" }); }
    }
}

public class AiChatRequest
{
    public string? System { get; set; }
    public List<AiMessage> Messages { get; set; } = new();
    public int MaxTokens { get; set; } = 1200;
}

public class AiMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}
