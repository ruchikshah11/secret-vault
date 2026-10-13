using System.Security.Cryptography;
using System.Text;

namespace SecretVault.Crypto;

// Checks passwords against Have I Been Pwned's "Pwned Passwords" range API using k-anonymity:
// only the first 5 hex characters of the password's SHA-1 hash are sent, never the password or full hash.
public class BreachChecker : IDisposable
{
    private const string RangeUrl = "https://api.pwnedpasswords.com/range/";

    private readonly HttpClient _http;
    private readonly Dictionary<string, Dictionary<string, int>> _rangeCache = new();

    public BreachChecker()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SecretVault-BreachWatch/1.0");
        // Padding hides the real number of matching suffixes from anyone observing response sizes.
        _http.DefaultRequestHeaders.Add("Add-Padding", "true");
    }

    // Returns how many times the password appears in known breaches (0 = not found).
    public async Task<int> GetBreachCountAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password)) return 0;

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var prefix = hash[..5];
        var suffix = hash[5..];

        if (!_rangeCache.TryGetValue(prefix, out var suffixes))
        {
            var body = await _http.GetStringAsync(RangeUrl + prefix, cancellationToken);
            suffixes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in body.Split('\n'))
            {
                var parts = line.Trim().Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out var count) && count > 0)
                    suffixes[parts[0]] = count;
            }
            _rangeCache[prefix] = suffixes;
        }

        return suffixes.TryGetValue(suffix, out var seen) ? seen : 0;
    }

    // Checks every record's password (each distinct password once). Progress reports (done, total).
    public async Task<Dictionary<Models.VaultEntry, int>> ScanAsync(IEnumerable<Models.VaultEntry> entries,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var withPassword = entries.Where(e => !string.IsNullOrEmpty(e.Secret)).ToList();
        var passwords = withPassword.Select(e => e.Secret).Distinct().ToList();
        var counts = new Dictionary<string, int>();
        progress?.Report((0, passwords.Count));
        foreach (var password in passwords)
        {
            counts[password] = await GetBreachCountAsync(password, cancellationToken);
            progress?.Report((counts.Count, passwords.Count));
        }
        return withPassword.ToDictionary(e => e, e => counts[e.Secret]);
    }

    public void Dispose() => _http.Dispose();
}
