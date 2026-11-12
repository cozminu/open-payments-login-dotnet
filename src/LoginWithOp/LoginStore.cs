using System.Buffers.Text;
using System.Security.Cryptography;

namespace LoginWithOp;

/// <summary>A logged-in browser session, bound to the wallet address the user proved access to.</summary>
public sealed record Session(string Id, string WalletAddress, DateTimeOffset ExpiresAt);

/// <summary>A grant waiting for the user to finish interacting with their wallet's authorization server.</summary>
public sealed record PendingLogin(
    string Id,
    string WalletAddress,
    string AuthServerUrl,
    string ClientNonce,
    string InteractNonce,
    Uri ContinueUrl,
    string ContinueAccessToken,
    DateTimeOffset ExpiresAt);

/// <summary>In-memory pending logins and sessions with expiry; everything is lost on restart.</summary>
public sealed class LoginStore(TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan PendingLoginTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(1);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, PendingLogin> _pendingLogins = [];
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly Lock _lock = new();

    /// <summary>Returns an unguessable id; used for login ids, session ids and grant nonces.</summary>
    public static string CreateId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));

    /// <summary>Stores a pending login under a fresh id. <c>Id</c> and <c>ExpiresAt</c> on the argument are ignored.</summary>
    public PendingLogin SavePendingLogin(PendingLogin details)
    {
        lock (_lock)
        {
            var now = Prune();
            var pendingLogin = details with { Id = CreateId(), ExpiresAt = now + PendingLoginTtl };
            _pendingLogins[pendingLogin.Id] = pendingLogin;
            return pendingLogin;
        }
    }

    /// <summary>Removes and returns a pending login; single use so a finish redirect can't be replayed.</summary>
    public PendingLogin? TakePendingLogin(string id)
    {
        lock (_lock)
        {
            Prune();
            return _pendingLogins.Remove(id, out var pendingLogin) ? pendingLogin : null;
        }
    }

    /// <summary>Starts a session for a verified wallet address.</summary>
    public Session CreateSession(string walletAddress)
    {
        lock (_lock)
        {
            var now = Prune();
            var session = new Session(CreateId(), walletAddress, now + SessionTtl);
            _sessions[session.Id] = session;
            return session;
        }
    }

    /// <summary>Returns the session when it exists and hasn't expired.</summary>
    public Session? FindSession(string id)
    {
        lock (_lock)
        {
            Prune();
            return _sessions.GetValueOrDefault(id);
        }
    }

    public void DeleteSession(string id)
    {
        lock (_lock)
        {
            _sessions.Remove(id);
        }
    }

    private DateTimeOffset Prune()
    {
        var now = _time.GetUtcNow();
        foreach (var (id, pendingLogin) in _pendingLogins)
        {
            if (pendingLogin.ExpiresAt <= now)
            {
                _pendingLogins.Remove(id);
            }
        }
        foreach (var (id, session) in _sessions)
        {
            if (session.ExpiresAt <= now)
            {
                _sessions.Remove(id);
            }
        }
        return now;
    }
}
