using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Filer.Ui.Auth;

/// <summary>
/// Reads claims from a JWT access token's payload for display/authorization state.
/// The client does not validate the signature - the API is the authority (05-security.md);
/// this only decodes the already-trusted token so components can read who is signed in.
/// </summary>
internal static class JwtClaims
{
    /// <summary>Builds an authenticated identity from the token, or <c>null</c> if it can't be read.</summary>
    public static ClaimsIdentity? ToIdentity(string accessToken)
    {
        Dictionary<string, JsonElement>? payload = DecodePayload(accessToken);
        if (payload is null || payload.Count == 0)
        {
            return null;
        }

        var claims = new List<Claim>();
        foreach ((string name, JsonElement value) in payload)
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    claims.Add(new Claim(name, item.ToString()));
                }
            }
            else
            {
                claims.Add(new Claim(name, value.ToString()));
            }
        }

        // "email"/"role" name+role types match the claims the API issues (05-security.md).
        return new ClaimsIdentity(claims, authenticationType: "jwt", nameType: "email", roleType: "role");
    }

    private static Dictionary<string, JsonElement>? DecodePayload(string jwt)
    {
        string[] parts = jwt.Split('.');
        if (parts.Length < 2 || string.IsNullOrEmpty(parts[1]))
        {
            return null;
        }

        try
        {
            byte[] bytes = Base64UrlDecode(parts[1]);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded,
        };
        return Convert.FromBase64String(padded);
    }
}
