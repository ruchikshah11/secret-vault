namespace SecretVault.Models;

public class VaultEntry
{
    // Stable identity used to merge copies of the vault (PC <-> phone). Empty until the record is first saved;
    // records from vaults written before Ids existed get a deterministic Id when loaded (see FileVaultStore).
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Folder { get; set; } = "General";
    public string Username { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? TotpSecret { get; set; }
    public string Url { get; set; } = string.Empty;
    // Explicit record type (see RecordTypes); null/empty = detected from the record's contents.
    public string? Type { get; set; }
    public bool Favorite { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<CustomField> CustomFields { get; set; } = new();
    public List<PasswordHistoryItem> PasswordHistory { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public const int MaxPasswordHistory = 10;

    // Records the outgoing password when it changes; newest first, capped.
    public void RememberPreviousPassword(string previous, DateTimeOffset changedAt)
    {
        if (string.IsNullOrEmpty(previous) || previous == Secret) return;
        PasswordHistory.Insert(0, new PasswordHistoryItem { Password = previous, ChangedAt = changedAt });
        if (PasswordHistory.Count > MaxPasswordHistory)
            PasswordHistory.RemoveRange(MaxPasswordHistory, PasswordHistory.Count - MaxPasswordHistory);
    }
}

public class PasswordHistoryItem
{
    public string Password { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
}

public enum CustomFieldType { Text, Password, Date, Url, Email, Phone, PinCode, Name, Address, MultiLineText }

public class CustomField
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public CustomFieldType Type { get; set; } = CustomFieldType.Text;

    public bool IsSecret => Type is CustomFieldType.Password or CustomFieldType.PinCode;
    public bool IsMultiLine => Type is CustomFieldType.Address or CustomFieldType.MultiLineText;
}

public class PersonalInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = string.Empty;
    public List<string> PhoneNumbers { get; set; } = new();
    public List<string> Addresses { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}

public class PaymentCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CardholderName { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public string ExpirationDate { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    // Empty = detect from the card number; otherwise the user's explicit choice (e.g. "Visa").
    public string Brand { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}

public class VaultData
{
    public List<VaultEntry> Entries { get; set; } = new();
    public List<PersonalInfo> PersonalInfoRecords { get; set; } = new();
    public List<PaymentCard> PaymentCards { get; set; } = new();
    // Folders created explicitly (they show even when empty). Folders that records use don't need to be listed.
    public List<string> Folders { get; set; } = new();

    // Sync tombstones, so something removed on one device isn't brought back by another device's copy:
    // Ids of records/cards/identities deleted forever, and folder paths deleted or renamed away.
    public List<string> RemovedIds { get; set; } = new();
    public List<string> RemovedFolders { get; set; } = new();
}
