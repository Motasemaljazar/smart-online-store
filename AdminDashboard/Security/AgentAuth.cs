using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AdminDashboard.Security;

public static class AgentAuth
{
    public static string IssueToken(int agentId, IOptions<AppSecurityOptions> opts)
    {
        var secret = opts.Value.DriverTokenSecret; 
        var minutes = opts.Value.DriverTokenMinutes;

        var header = Base64Url(JsonSerializer.Serialize(new { alg = "HS256", typ = "AGENT" }));
        var payload = Base64Url(JsonSerializer.Serialize(new
        {
            sub = agentId,
            exp = DateTimeOffset.UtcNow.AddMinutes(minutes).ToUnixTimeSeconds()
        }));

        var data = $"{header}.{payload}";
        var sig = Base64Url(HmacSha256(data, secret));
        return $"{data}.{sig}";
    }

    public static bool TryValidate(string token, IOptions<AppSecurityOptions> opts, out int agentId)
    {
        agentId = 0;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Split('.');
        if (parts.Length != 3) return false;

        var secret = opts.Value.DriverTokenSecret;
        var expected = Base64Url(HmacSha256($"{parts[0]}.{parts[1]}", secret));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(parts[2])))
            return false;

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var exp = root.GetProperty("exp").GetInt64();
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return false;

            agentId = root.GetProperty("sub").GetInt32();
            return agentId > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string Base64Url(string input) => Base64Url(Encoding.UTF8.GetBytes(input));
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }

    private static byte[] HmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }
}
