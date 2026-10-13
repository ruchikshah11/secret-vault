using System.Text;
using Android.Security.Keystore;
using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace SecretVault.Mobile;

// Fingerprint / face unlock. The master password is encrypted with an AES key that lives in the Android
// Keystore (hardware-backed where available) and can only be used right after a strong biometric check.
// Enrolling a new fingerprint on the phone invalidates the key, so the password must be entered again.
// Same idea as Windows Hello in the desktop app.
public static class BiometricUnlock
{
    private const string KeyAlias = "secretvault.biometric";
    private const string IvKey = "bio_iv";
    private const string DataKey = "bio_data";
    private const string DeclinedKey = "bio_declined";
    private const int Strong = BiometricManager.Authenticators.BiometricStrong;

    public static bool IsAvailable =>
        BiometricManager.From(Platform.AppContext).CanAuthenticate(Strong) == BiometricManager.BiometricSuccess;

    public static bool IsEnabled => Preferences.ContainsKey(DataKey) && Preferences.ContainsKey(IvKey);

    // The user said "Not now" to the offer after unlocking; don't ask again (they can still turn it on in Settings).
    public static bool Declined
    {
        get => Preferences.Get(DeclinedKey, false);
        set => Preferences.Set(DeclinedKey, value);
    }

    // Asks for a fingerprint, then stores the encrypted master password. Returns false if cancelled.
    public static async Task<bool> EnableAsync(string masterPassword)
    {
        DeleteKey(); // always start from a fresh key
        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, CreateKey());
        var authorized = await AuthenticateAsync(cipher, "Turn on fingerprint / face unlock", "Confirm it's you to unlock SecretVault with your fingerprint or face");
        if (authorized is null) return false;

        var encrypted = authorized.DoFinal(Encoding.UTF8.GetBytes(masterPassword))!;
        Preferences.Set(IvKey, Convert.ToBase64String(authorized.GetIV()!));
        Preferences.Set(DataKey, Convert.ToBase64String(encrypted));
        Declined = false;
        return true;
    }

    // Returns the master password after a successful biometric check, or null if cancelled / no longer valid.
    public static async Task<string?> UnlockAsync()
    {
        if (!IsEnabled) return null;
        Cipher cipher;
        try
        {
            var key = LoadKey() ?? throw new KeyPermanentlyInvalidatedException();
            cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.DecryptMode, key, new GCMParameterSpec(128, Convert.FromBase64String(Preferences.Get(IvKey, ""))));
        }
        catch (KeyPermanentlyInvalidatedException)
        {
            // A fingerprint was added or removed on the phone: the stored password can't be used any more.
            Disable();
            throw new InvalidOperationException("Fingerprints or face data on this phone changed. Unlock with your master password to turn fingerprint / face unlock on again.");
        }

        var authorized = await AuthenticateAsync(cipher, "Unlock SecretVault", "Use your fingerprint or face");
        if (authorized is null) return null;
        return Encoding.UTF8.GetString(authorized.DoFinal(Convert.FromBase64String(Preferences.Get(DataKey, "")))!);
    }

    public static void Disable()
    {
        Preferences.Remove(IvKey);
        Preferences.Remove(DataKey);
        DeleteKey();
    }

    // ---------- Keystore ----------

    private static KeyStore OpenKeyStore()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        return store;
    }

    private static IKey? LoadKey() => OpenKeyStore().GetKey(KeyAlias, null);

    private static void DeleteKey()
    {
        var store = OpenKeyStore();
        if (store.ContainsAlias(KeyAlias)) store.DeleteEntry(KeyAlias);
    }

    private static IKey CreateKey()
    {
        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        generator.Init(new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .SetUserAuthenticationRequired(true)          // every use needs a fresh biometric check
            .SetInvalidatedByBiometricEnrollment(true)
            .Build());
        return generator.GenerateKey()!;
    }

    // ---------- prompt ----------

    private static Task<Cipher?> AuthenticateAsync(Cipher cipher, string title, string subtitle)
    {
        var done = new TaskCompletionSource<Cipher?>();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var activity = (FragmentActivity)Platform.CurrentActivity!;
                var prompt = new BiometricPrompt(activity, ContextCompat.GetMainExecutor(activity)!, new Callback(done));
                var info = new BiometricPrompt.PromptInfo.Builder()
                    .SetTitle(title)
                    .SetSubtitle(subtitle)
                    .SetNegativeButtonText("Use password")
                    .SetAllowedAuthenticators(Strong)
                    .Build();
                prompt.Authenticate(info, new BiometricPrompt.CryptoObject(cipher));
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        });
        return done.Task;
    }

    private sealed class Callback(TaskCompletionSource<Cipher?> done) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result) =>
            done.TrySetResult(result.CryptoObject?.Cipher);

        // Cancelled, "Use password", too many attempts…
        public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString) => done.TrySetResult(null);

        // A finger that didn't match: the prompt stays open so the user can try again.
        public override void OnAuthenticationFailed() { }
    }
}
