using System.Security.Cryptography;
using System.Text;
using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class ApiTokenService
{
    public const string TokenPrefix = "svnhub_app_";

    private const string LegacyTokenPrefix = "svnhub_mcp_";

    private readonly IPortalStore _store;

    public ApiTokenService(IPortalStore store)
    {
        _store = store;
    }

    public IReadOnlyList<ApiToken> ListForUser(Guid userId)
    {
        var state = _store.Read();
        return state.ApiTokens
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToArray();
    }

    public OperationResult<IssuedApiToken> Create(Guid actorUserId, string name, int? expiresInDays)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0)
        {
            return OperationResult<IssuedApiToken>.Fail("Token name is required.");
        }

        if (name.Length > 80)
        {
            return OperationResult<IssuedApiToken>.Fail("Token name is too long.");
        }

        if (expiresInDays is < 1 or > 3650)
        {
            return OperationResult<IssuedApiToken>.Fail("Invalid expiration.");
        }

        var state = _store.Read();
        var user = state.Users.FirstOrDefault(u => u.Id == actorUserId && u.IsActive);
        if (user is null)
        {
            return OperationResult<IssuedApiToken>.Fail("User not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var plainTextToken = GenerateToken();
        var token = new ApiToken(
            Id: Guid.NewGuid(),
            UserId: actorUserId,
            Name: name,
            TokenHash: ComputeHash(plainTextToken),
            TokenPrefix: plainTextToken[..Math.Min(plainTextToken.Length, 22)],
            CreatedAt: now,
            ExpiresAt: expiresInDays is null ? null : now.AddDays(expiresInDays.Value),
            RevokedAt: null,
            LastUsedAt: null);

        var newState = state with
        {
            ApiTokens = [..state.ApiTokens, token],
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: now,
                    ActorUserId: actorUserId,
                    Action: "api_token.create",
                    Target: token.Id.ToString("D"),
                    Success: true,
                    Details: token.Name
                ),
            ],
        };

        _store.Write(newState);
        return OperationResult<IssuedApiToken>.Ok(new IssuedApiToken(token, plainTextToken));
    }

    public OperationResult Revoke(Guid actorUserId, Guid tokenId)
    {
        var state = _store.Read();
        var token = state.ApiTokens.FirstOrDefault(t => t.Id == tokenId && t.UserId == actorUserId);
        if (token is null)
        {
            return OperationResult.Fail("Token not found.");
        }

        if (token.RevokedAt is not null)
        {
            return OperationResult.Ok();
        }

        var now = DateTimeOffset.UtcNow;
        var updated = token with { RevokedAt = now };
        var newState = state with
        {
            ApiTokens = state.ApiTokens.Select(t => t.Id == token.Id ? updated : t).ToList(),
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: now,
                    ActorUserId: actorUserId,
                    Action: "api_token.revoke",
                    Target: token.Id.ToString("D"),
                    Success: true,
                    Details: token.Name
                ),
            ],
        };

        _store.Write(newState);
        return OperationResult.Ok();
    }

    public PortalUser? AuthenticateBearerToken(string token)
    {
        token = token.Trim();
        if (string.IsNullOrWhiteSpace(token) || !HasAcceptedPrefix(token))
        {
            return null;
        }

        var hash = ComputeHash(token);
        var now = DateTimeOffset.UtcNow;
        var state = _store.Read();
        var apiToken = state.ApiTokens.FirstOrDefault(t =>
            t.IsActive(now) &&
            FixedTimeEquals(t.TokenHash, hash));

        if (apiToken is null)
        {
            return null;
        }

        var user = state.Users.FirstOrDefault(u => u.Id == apiToken.UserId && u.IsActive);
        if (user is null)
        {
            return null;
        }

        if (apiToken.LastUsedAt is null || now - apiToken.LastUsedAt.Value > TimeSpan.FromMinutes(5))
        {
            var updated = apiToken with { LastUsedAt = now };
            _store.Write(state with
            {
                ApiTokens = state.ApiTokens.Select(t => t.Id == apiToken.Id ? updated : t).ToList(),
            });
        }

        return user;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return TokenPrefix + Base64UrlEncode(bytes);
    }

    private static bool HasAcceptedPrefix(string token) =>
        token.StartsWith(TokenPrefix, StringComparison.Ordinal) ||
        token.StartsWith(LegacyTokenPrefix, StringComparison.Ordinal);

    private static string ComputeHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length &&
               CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

public sealed record IssuedApiToken(ApiToken Token, string PlainTextToken);
