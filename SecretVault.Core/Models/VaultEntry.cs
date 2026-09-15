namespace SecretVault.Models;

public class VaultEntry
{
    public string Name { get; set; } = string.Empty;
    public string Folder { get; set; } = "General";
    public string Username { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? TotpSecret { get; set; }
    public List<CustomField> CustomFields { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
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
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}

public class VaultData
{
    public List<VaultEntry> Entries { get; set; } = new();
    public List<PersonalInfo> PersonalInfoRecords { get; set; } = new();
    public List<PaymentCard> PaymentCards { get; set; } = new();
}
