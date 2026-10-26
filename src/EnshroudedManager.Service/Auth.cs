using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace EnshroudedManager.Service;

/// <summary>Single admin password (PBKDF2) and bearer tokens shared by the web page and the desktop app.</summary>
public class Auth
{
    private const int Iterations = 200_000;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);
    private readonly SettingsStore _settings;
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Since)> _failures = new();

    public Auth(SettingsStore settings) => _settings = settings;

    public bool Configured => _settings.Current.PasswordHash != null;

    public void SetPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        _settings.Update(s =>
        {
            s.PasswordHash = $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
            s.Tokens.Clear(); // a new password logs everybody out
        });
    }

    public void Reset() => _settings.Update(s => { s.PasswordHash = null; s.Tokens.Clear(); });

    public bool CheckPassword(string password)
    {
        var stored = _settings.Current.PasswordHash;
        if (stored == null) return false;
        var parts = stored.Split('.');
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[1]), int.Parse(parts[0]), HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(parts[2]));
    }

    /// <summary>Returns a token, or null. Locks an address out for 5 minutes after 5 failures.</summary>
    public async Task<string?> LoginAsync(string password, string client)
    {
        if (_failures.TryGetValue(client, out var f) && f.Count >= 5 && DateTimeOffset.Now - f.Since < TimeSpan.FromMinutes(5))
            return null;
        if (!CheckPassword(password))
        {
            _failures.AddOrUpdate(client, _ => (1, DateTimeOffset.Now),
                (_, old) => DateTimeOffset.Now - old.Since > TimeSpan.FromMinutes(5) ? (1, DateTimeOffset.Now) : (old.Count + 1, old.Since));
            await Task.Delay(1000);
            return null;
        }
        _failures.TryRemove(client, out _);
        return IssueToken();
    }

    public string IssueToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _settings.Update(s =>
        {
            s.Tokens.RemoveAll(t => t.Expires < DateTimeOffset.Now);
            s.Tokens.Add(new StoredToken { Hash = HashToken(token), Expires = DateTimeOffset.Now + TokenLifetime });
        });
        return token;
    }

    public bool ValidateToken(string? token) =>
        !string.IsNullOrEmpty(token)
        && _settings.Current.Tokens.Any(t => t.Hash == HashToken(token) && t.Expires > DateTimeOffset.Now);

    public void Revoke(string token) => _settings.Update(s => s.Tokens.RemoveAll(t => t.Hash == HashToken(token)));

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
