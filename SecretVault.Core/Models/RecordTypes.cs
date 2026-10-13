namespace SecretVault.Models;

// Record types shown in the "Record Types" section. A record can have an explicit type (VaultEntry.Type);
// otherwise it is inferred from what it contains, so imported records are classified automatically.
public static class RecordTypes
{
    public const string Login = "Login";
    public const string Server = "Server";
    public const string Database = "Database";
    public const string AppCredentials = "App credentials";
    public const string Certificate = "Certificate";
    public const string SoftwareLicense = "Software license";
    public const string SecureNote = "Secure note";

    public static readonly string[] All = { Login, Server, Database, AppCredentials, Certificate, SoftwareLicense, SecureNote };

    public static string Of(VaultEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Type) && All.Contains(entry.Type, StringComparer.OrdinalIgnoreCase)
            ? All.First(t => t.Equals(entry.Type, StringComparison.OrdinalIgnoreCase))
            : Detect(entry);

    // Most specific first: a server record often also has a password, an app registration often a certificate.
    public static string Detect(VaultEntry entry)
    {
        var labels = entry.CustomFields.Select(f => f.Label.ToLowerInvariant()).ToList();
        bool Has(params string[] words) => labels.Any(l => words.Any(l.Contains));

        if (Has("license key", "licence key", "product key", "serial number")) return SoftwareLicense;
        if (Has("client id", "client secret", "tenant id", "api key", "app id", "application id")) return AppCredentials;
        if (Has("thumbprint", "certificate", "pfx")) return Certificate;
        if (Has("database", "connection string", "storage account")) return Database;
        if (Has("hostname", "ip address", "host name", "server")) return Server;

        var hasLogin = !string.IsNullOrEmpty(entry.Username) || !string.IsNullOrEmpty(entry.Secret);
        if (!hasLogin && (!string.IsNullOrWhiteSpace(entry.Notes) || entry.CustomFields.Count > 0)) return SecureNote;
        return Login;
    }
}
