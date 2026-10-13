using System.Text;
using SecretVault.Models;

namespace SecretVault.Storage;

// CSV import (Chrome / Edge / Bitwarden / Keeper / SecretVault exports) and plain-text CSV export.
public static class CsvTransfer
{
    private static readonly string[] ExportHeader = { "name", "folder", "url", "username", "password", "notes", "totp", "tags", "favorite" };

    // Header aliases, matched case-insensitively; first match wins.
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["name"] = new[] { "name", "title" },
        ["folder"] = new[] { "folder", "grouping", "group" },
        ["url"] = new[] { "url", "login_uri", "uri", "website", "web site" },
        ["username"] = new[] { "username", "login_username", "user", "email", "login" },
        ["password"] = new[] { "password", "login_password" },
        ["notes"] = new[] { "notes", "note", "extra", "comments" },
        ["totp"] = new[] { "totp", "login_totp", "otp", "otpauth" },
        ["tags"] = new[] { "tags", "tag" },
        ["favorite"] = new[] { "favorite", "fav", "favourite" },
        ["type"] = new[] { "type" },
    };

    public record ImportResult(List<VaultEntry> Entries, int SkippedRows);

    public static ImportResult Import(string csvText)
    {
        var rows = Parse(csvText);
        if (rows.Count == 0) return new ImportResult(new List<VaultEntry>(), 0);

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(string key) => Aliases[key].Select(a => header.IndexOf(a)).FirstOrDefault(i => i >= 0, -1);
        var cols = Aliases.Keys.ToDictionary(k => k, Col);
        if (cols["name"] < 0 && cols["url"] < 0)
        {
            if (LooksLikeKeeper(rows)) return ImportKeeper(rows);
            throw new FormatException("Unrecognised CSV: expected a 'name' or 'url' column in the first row.");
        }

        string Get(List<string> row, string key) =>
            cols[key] >= 0 && cols[key] < row.Count ? row[cols[key]].Trim() : string.Empty;

        var now = DateTimeOffset.UtcNow;
        var entries = new List<VaultEntry>();
        var skipped = 0;
        foreach (var row in rows.Skip(1))
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            // Bitwarden exports cards/identities too; only logins and notes map onto vault records.
            var type = Get(row, "type");
            if (type.Length > 0 && type is not ("login" or "note"))
            {
                skipped++;
                continue;
            }

            var url = Get(row, "url");
            var name = Get(row, "name");
            if (name.Length == 0) name = HostOf(url) ?? "Imported item";

            var folder = Get(row, "folder");
            entries.Add(new VaultEntry
            {
                Name = name,
                Folder = folder.Length > 0 ? folder : "Imported",
                Url = url,
                Username = Get(row, "username"),
                Secret = Get(row, "password"),
                Notes = Get(row, "notes"),
                TotpSecret = NormalizeTotp(Get(row, "totp")),
                Tags = Get(row, "tags").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                Favorite = Get(row, "favorite") is "1" or "true" or "True" or "TRUE" or "yes",
                CreatedAt = now,
                ModifiedAt = now
            });
        }

        return new ImportResult(entries, skipped);
    }

    // Keeper exports have no header: Folder, Title, Login, Password, Website Address, Notes, Shared Folder,
    // followed by custom fields as name/value pairs (a 2FA secret appears as an otpauth:// value).
    private static bool LooksLikeKeeper(List<List<string>> rows) =>
        rows.Count > 0 && rows.All(r => r.All(string.IsNullOrWhiteSpace) || (r.Count >= 7 && (r.Count - 7) % 2 == 0));

    private static ImportResult ImportKeeper(List<List<string>> rows)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new List<VaultEntry>();
        foreach (var row in rows)
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            var folder = row[0].Trim();
            if (folder.Length == 0) folder = row[6].Trim();
            var url = row[4].Trim();
            var entry = new VaultEntry
            {
                Name = row[1].Trim().Length > 0 ? row[1].Trim() : HostOf(url) ?? "Imported item",
                Folder = folder.Length > 0 ? folder : "Imported",
                Username = row[2].Trim(),
                Secret = row[3],
                Url = url,
                Notes = row[5].Trim(),
                CreatedAt = now,
                ModifiedAt = now
            };

            for (var i = 7; i + 1 < row.Count; i += 2)
            {
                var label = row[i].Trim();
                var value = row[i + 1];
                if (label.Length == 0 && value.Length == 0) continue;

                if (value.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase) && entry.TotpSecret is null)
                {
                    entry.TotpSecret = NormalizeTotp(value);
                    continue;
                }
                entry.CustomFields.Add(new CustomField { Label = label, Value = value, Type = GuessFieldType(label, value) });
            }

            entries.Add(entry);
        }
        return new ImportResult(entries, 0);
    }

    private static readonly string[] SecretWords = { "secret", "password", "passcode", "pin", "key", "token", "answer", "connection string", "private" };

    private static CustomFieldType GuessFieldType(string label, string value)
    {
        var l = label.ToLowerInvariant();
        // "Client Secret Expiry Date" is a date, not a secret.
        var isDateLabel = l.Contains("expir") || l.Contains("date") || l.EndsWith(" on") || l.EndsWith(" on:");
        if (!isDateLabel && SecretWords.Any(w => l.Contains(w))) return CustomFieldType.Password;
        if (l.Contains("email") || (value.Contains('@') && !value.Contains(' ') && value.IndexOf('@') > 0 && value.Contains('.'))) return CustomFieldType.Email;
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return CustomFieldType.Url;
        if (l.Contains("phone") || l.Contains("mobile")) return CustomFieldType.Phone;
        if (value.Contains('\n')) return CustomFieldType.MultiLineText;
        return CustomFieldType.Text;
    }

    public static string Export(IEnumerable<VaultEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", ExportHeader));
        foreach (var e in entries)
        {
            var fields = new[]
            {
                e.Name, e.Folder, e.Url, e.Username, e.Secret, e.Notes, e.TotpSecret ?? string.Empty,
                string.Join(",", e.Tags), e.Favorite ? "1" : "0"
            };
            sb.AppendLine(string.Join(",", fields.Select(Quote)));
        }
        return sb.ToString();
    }

    public static string? HostOf(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var candidate = url.Contains("://") ? url : "https://" + url;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host
            : null;
    }

    // Accepts a bare base32 secret or an otpauth:// URI (as Bitwarden and others export).
    private static string? NormalizeTotp(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!value.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase)) return value.Replace(" ", "");

        var query = value.Contains('?') ? value[(value.IndexOf('?') + 1)..] : string.Empty;
        foreach (var pair in query.Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("secret", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    private static string Quote(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    // RFC 4180 parser: quoted fields, escaped quotes, embedded newlines.
    private static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                    break;
                default: field.Append(c); break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        // Strip a UTF-8 BOM from the first header cell.
        if (rows.Count > 0 && rows[0].Count > 0) rows[0][0] = rows[0][0].TrimStart('﻿');
        return rows;
    }
}
