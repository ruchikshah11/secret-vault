using SecretVault.Models;

namespace SecretVault.Storage;

public interface IVaultStore
{
    bool Exists();
    Task<VaultData> LoadAsync(string masterPassword);
    Task SaveAsync(VaultData data, string masterPassword);
}

public class VaultAuthenticationException : Exception
{
    public VaultAuthenticationException() : base("Incorrect master password or corrupted vault.") { }
}
