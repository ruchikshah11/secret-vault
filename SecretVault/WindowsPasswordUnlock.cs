using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecretVault.Crypto;

namespace SecretVault.Gui;

// Unlock with the Windows account password.
//
// The password is collected with the native "Windows Security" dialog and verified with Windows (LogonUser) when the
// feature is turned on. The master password is then encrypted with a key derived from the Windows password (Argon2id,
// same as the vault) and the result is wrapped with DPAPI for the current user. Unlocking needs the Windows password
// again; if it has since changed, the stored copy simply no longer opens and the user falls back to the master password.
public static class WindowsPasswordUnlock
{
    private static string DataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecretVault", "winpass.dat");

    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("SecretVault.WindowsPassword");

    public enum Outcome { Success, Canceled, WrongPassword, PasswordChanged, Failed }

    private sealed class Envelope
    {
        public string Salt { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Cipher { get; set; } = "";
    }

    public static bool IsEnabled => File.Exists(DataPath);

    public static string CurrentAccount => $@"{Environment.UserDomainName}\{Environment.UserName}";

    public static async Task<Outcome> EnableAsync(IWin32Window owner, string masterPassword)
    {
        var error = 0;
        while (true)
        {
            var windowsPassword = Prompt(owner, "Confirm your Windows password to use it for unlocking SecretVault.", error);
            if (windowsPassword is null) return Outcome.Canceled;
            if (!VerifyWithWindows(windowsPassword))
            {
                error = ErrorLogonFailure; // re-prompt with Windows' own "incorrect password" message
                continue;
            }

            var json = await Task.Run(() =>
            {
                var salt = VaultCrypto.GenerateSalt();
                var key = VaultCrypto.DeriveKey(windowsPassword, salt);
                var (nonce, cipher, tag) = VaultCrypto.Encrypt(key, Encoding.UTF8.GetBytes(masterPassword));
                CryptographicOperations.ZeroMemory(key);
                return JsonSerializer.SerializeToUtf8Bytes(new Envelope
                {
                    Salt = Convert.ToBase64String(salt),
                    Nonce = Convert.ToBase64String(nonce),
                    Tag = Convert.ToBase64String(tag),
                    Cipher = Convert.ToBase64String(cipher)
                });
            });

            Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
            await File.WriteAllBytesAsync(DataPath, ProtectedData.Protect(json, DpapiEntropy, DataProtectionScope.CurrentUser));
            return Outcome.Success;
        }
    }

    public static async Task<(Outcome Outcome, string? MasterPassword)> UnlockAsync(IWin32Window owner)
    {
        Envelope envelope;
        try
        {
            var json = ProtectedData.Unprotect(await File.ReadAllBytesAsync(DataPath), DpapiEntropy, DataProtectionScope.CurrentUser);
            envelope = JsonSerializer.Deserialize<Envelope>(json) ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or FormatException)
        {
            return (Outcome.Failed, null);
        }

        var error = 0;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var windowsPassword = Prompt(owner, "Enter your Windows password to unlock SecretVault.", error);
            if (windowsPassword is null) return (Outcome.Canceled, null);

            var master = await Task.Run(() =>
            {
                var key = VaultCrypto.DeriveKey(windowsPassword, Convert.FromBase64String(envelope.Salt));
                try
                {
                    var plain = VaultCrypto.Decrypt(key, Convert.FromBase64String(envelope.Nonce), Convert.FromBase64String(envelope.Cipher), Convert.FromBase64String(envelope.Tag));
                    return Encoding.UTF8.GetString(plain);
                }
                catch (CryptographicException)
                {
                    return null;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            });
            if (master is not null) return (Outcome.Success, master);

            // Correct Windows password but it no longer opens the stored copy => the Windows password was changed.
            if (VerifyWithWindows(windowsPassword)) return (Outcome.PasswordChanged, null);
            error = ErrorLogonFailure;
        }
        return (Outcome.WrongPassword, null);
    }

    public static void Disable()
    {
        try { if (File.Exists(DataPath)) File.Delete(DataPath); }
        catch (IOException) { }
    }

    // ---------- Windows Security dialog + verification ----------

    private const int ErrorLogonFailure = 1326;
    private const int ErrorCancelled = 1223;
    private const int CreduiwinGeneric = 0x1;
    private const int CreduiwinInCredOnly = 0x20;
    private const int CredPackGenericCredentials = 0x4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CredUiInfo
    {
        public int Size;
        public IntPtr Parent;
        public string Message;
        public string Caption;
        public IntPtr Banner;
    }

    [DllImport("credui.dll", CharSet = CharSet.Unicode)]
    private static extern int CredUIPromptForWindowsCredentials(ref CredUiInfo info, int authError, ref uint authPackage,
        IntPtr inAuthBuffer, uint inAuthBufferSize, out IntPtr outAuthBuffer, out uint outAuthBufferSize, ref bool save, int flags);

    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredPackAuthenticationBuffer(int flags, string userName, string password, IntPtr packedCredentials, ref int packedSize);

    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredUnPackAuthenticationBuffer(int flags, IntPtr authBuffer, uint authBufferSize,
        StringBuilder userName, ref int maxUserName, StringBuilder domainName, ref int maxDomainName, StringBuilder password, ref int maxPassword);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LogonUser(string user, string domain, string password, int logonType, int provider, out IntPtr token);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);

    // Shows the native Windows Security password dialog for the current account. Returns null if cancelled.
    private static string? Prompt(IWin32Window owner, string message, int previousError)
    {
        var info = new CredUiInfo
        {
            Size = Marshal.SizeOf<CredUiInfo>(),
            Parent = owner.Handle,
            Caption = "SecretVault",
            Message = message
        };

        // Pre-fill (and lock) the user name so only the password is asked for.
        var packedSize = 0;
        CredPackAuthenticationBuffer(CredPackGenericCredentials, CurrentAccount, "", IntPtr.Zero, ref packedSize);
        var packed = Marshal.AllocCoTaskMem(packedSize);
        try
        {
            if (!CredPackAuthenticationBuffer(CredPackGenericCredentials, CurrentAccount, "", packed, ref packedSize))
                return null;

            uint package = 0;
            var save = false;
            var result = CredUIPromptForWindowsCredentials(ref info, previousError, ref package, packed, (uint)packedSize,
                out var outBuffer, out var outSize, ref save, CreduiwinGeneric | CreduiwinInCredOnly);
            if (result == ErrorCancelled || result != 0) return null;

            try
            {
                int userLen = 256, domainLen = 256, passLen = 256;
                var user = new StringBuilder(userLen);
                var domain = new StringBuilder(domainLen);
                var password = new StringBuilder(passLen);
                return CredUnPackAuthenticationBuffer(0, outBuffer, outSize, user, ref userLen, domain, ref domainLen, password, ref passLen)
                    ? password.ToString()
                    : null;
            }
            finally
            {
                if (outBuffer != IntPtr.Zero)
                {
                    // Wipe the returned credential buffer before freeing it.
                    Marshal.Copy(new byte[outSize], 0, outBuffer, (int)outSize);
                    CoTaskMemFree(outBuffer);
                }
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(packed);
        }
    }

    // Asks Windows whether this is the current account's password (works offline via cached credentials).
    private static bool VerifyWithWindows(string password)
    {
        const int logonInteractive = 2, logonNetwork = 3, providerDefault = 0;
        foreach (var type in new[] { logonInteractive, logonNetwork })
        {
            if (LogonUser(Environment.UserName, Environment.UserDomainName, password, type, providerDefault, out var token))
            {
                CloseHandle(token);
                return true;
            }
        }
        return false;
    }
}
