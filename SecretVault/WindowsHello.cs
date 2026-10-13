using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;

namespace SecretVault.Gui;

// Windows Hello (face / fingerprint / PIN) unlock.
//
// Enabling creates a Windows Hello key for SecretVault; its private half lives in the TPM and is only usable after
// the user passes Windows Hello. We sign a random challenge with it (RSA PKCS#1 signatures are deterministic), hash the
// signature into an AES-256 key, and encrypt the master password with that key. The result is additionally wrapped with
// DPAPI (current Windows user) and saved next to the vault. Unlocking repeats the signature, which shows the Hello prompt.
public static class WindowsHello
{
    private const string CredentialName = "SecretVault.Unlock";

    private static string DataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecretVault", "hello.dat");

    public enum Outcome { Success, Canceled, NotAvailable, Failed }

    private sealed class Envelope
    {
        public string Challenge { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Cipher { get; set; } = "";
    }

    // True when Windows Hello is set up on this device (face, fingerprint or at least a Hello PIN).
    public static async Task<bool> IsAvailableAsync()
    {
        try { return await KeyCredentialManager.IsSupportedAsync(); }
        catch (Exception ex) when (ex is COMException or PlatformNotSupportedException or TypeLoadException) { return false; }
    }

    public static bool IsEnabled => File.Exists(DataPath);

    public static async Task<Outcome> EnableAsync(string masterPassword)
    {
        if (!await IsAvailableAsync()) return Outcome.NotAvailable;

        BringPromptToFront();
        var created = await KeyCredentialManager.RequestCreateAsync(CredentialName, KeyCredentialCreationOption.ReplaceExisting);
        if (created.Status != KeyCredentialStatus.Success)
            return created.Status == KeyCredentialStatus.UserCanceled ? Outcome.Canceled : Outcome.Failed;

        var challenge = RandomNumberGenerator.GetBytes(32);
        var (outcome, key) = await DeriveKeyAsync(created.Credential, challenge);
        if (outcome != Outcome.Success) return outcome;

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(masterPassword);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, plaintext, cipher, tag);
        CryptographicOperations.ZeroMemory(plaintext);
        CryptographicOperations.ZeroMemory(key);

        var json = JsonSerializer.SerializeToUtf8Bytes(new Envelope
        {
            Challenge = Convert.ToBase64String(challenge),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Cipher = Convert.ToBase64String(cipher)
        });
        var protectedBytes = ProtectedData.Protect(json, Encoding.UTF8.GetBytes(CredentialName), DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
        await File.WriteAllBytesAsync(DataPath, protectedBytes);
        return Outcome.Success;
    }

    // Shows the Windows Hello prompt and returns the stored master password on success.
    public static async Task<(Outcome Outcome, string? Password)> UnlockAsync()
    {
        if (!IsEnabled) return (Outcome.NotAvailable, null);
        if (!await IsAvailableAsync()) return (Outcome.NotAvailable, null);

        Envelope envelope;
        try
        {
            var json = ProtectedData.Unprotect(await File.ReadAllBytesAsync(DataPath), Encoding.UTF8.GetBytes(CredentialName), DataProtectionScope.CurrentUser);
            envelope = JsonSerializer.Deserialize<Envelope>(json) ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or FormatException)
        {
            return (Outcome.Failed, null);
        }

        var opened = await KeyCredentialManager.OpenAsync(CredentialName);
        if (opened.Status != KeyCredentialStatus.Success) return (Outcome.Failed, null);

        BringPromptToFront();
        var (outcome, key) = await DeriveKeyAsync(opened.Credential, Convert.FromBase64String(envelope.Challenge));
        if (outcome != Outcome.Success) return (outcome, null);

        try
        {
            var cipher = Convert.FromBase64String(envelope.Cipher);
            var plaintext = new byte[cipher.Length];
            using (var aes = new AesGcm(key, 16))
                aes.Decrypt(Convert.FromBase64String(envelope.Nonce), cipher, Convert.FromBase64String(envelope.Tag), plaintext);
            var password = Encoding.UTF8.GetString(plaintext);
            CryptographicOperations.ZeroMemory(plaintext);
            return (Outcome.Success, password);
        }
        catch (CryptographicException)
        {
            return (Outcome.Failed, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static async Task DisableAsync()
    {
        try { if (File.Exists(DataPath)) File.Delete(DataPath); }
        catch (IOException) { }
        try { await KeyCredentialManager.DeleteAsync(CredentialName); }
        catch (Exception ex) when (ex is COMException or FileNotFoundException) { }
    }

    private static async Task<(Outcome, byte[])> DeriveKeyAsync(KeyCredential credential, byte[] challenge)
    {
        var signed = await credential.RequestSignAsync(CryptographicBuffer.CreateFromByteArray(challenge));
        if (signed.Status != KeyCredentialStatus.Success)
            return (signed.Status == KeyCredentialStatus.UserCanceled ? Outcome.Canceled : Outcome.Failed, Array.Empty<byte>());

        CryptographicBuffer.CopyToByteArray(signed.Result, out var signature);
        var key = SHA256.HashData(signature);
        CryptographicOperations.ZeroMemory(signature);
        return (Outcome.Success, key);
    }

    // Desktop apps often get the Windows Hello dialog *behind* their window; pull it to the front once it appears.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static void BringPromptToFront() => _ = Task.Run(async () =>
    {
        for (var i = 0; i < 40; i++)
        {
            var hwnd = FindWindow("Credential Dialog Xaml Host", null);
            if (hwnd != IntPtr.Zero)
            {
                SetForegroundWindow(hwnd);
                return;
            }
            await Task.Delay(100);
        }
    });
}
