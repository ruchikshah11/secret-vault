# SecretVault

Offline, single-file encrypted secret manager for Windows. The vault (passwords, custom fields,
personal info, payment cards) is stored as one AES-256-GCM encrypted file, keyed off a master
password via Argon2id. There is no sync, no server, and no account system - everything lives in a
local `vault.db`.

The solution has three projects:

- **SecretVault.Core** - class library with all the shared logic: `VaultCrypto` (Argon2id key
  derivation + AES-GCM encrypt/decrypt), `PasswordGenerator`, `PasswordStrength` (entropy-based
  strength estimate), `TotpGenerator` (RFC 6238 TOTP, HMAC-SHA1), the `VaultEntry` /
  `CustomField` / `PersonalInfo` / `PaymentCard` / `VaultData` models, and `FileVaultStore`
  (the binary file format reader/writer). Both the UI and the CLI reference this project and share
  the same vault file.
- **SecretVault** - the WinForms desktop app (Keeper-style dark/light UI). `LoginForm` creates or
  unlocks the vault, `MainForm` is the main shell (folder list, entry list/search, entry detail
  panel with TOTP codes and copy-to-clipboard), `EntryEditForm`/`CustomFieldRow` handle
  add/edit of entries and their custom fields, `IdentityPaymentsPanel`/`PaymentCardEditForm` cover
  the "Identity & Payments" tab (personal info + stored payment cards), and `SecurityAuditPanel`
  computes a security score and lists weak/reused passwords. `AppTheme`, `DarkTitleBar`,
  `ThemeSettings`, `ThemeToggleButton`, `IconButton`, `GoldCircleButton`, `Icons` are UI/theming
  plumbing (dark-mode title bar, hand-drawn icon set, persisted theme choice).
- **SecretVault.Cli** - a console front end to the same vault file, for scripting/terminal use.
  `ClipboardHelper` wraps the Win32 clipboard API so the CLI can copy secrets without pulling in
  WinForms.

## Run it

### Desktop UI

```
dotnet run --project SecretVault
```

First run prompts to create a master password (this creates `vault.db`); subsequent runs prompt
to unlock. There's no command-line configuration - it always uses the default vault location.

### CLI

```
dotnet run --project SecretVault.Cli -- <command> [args]
```

or, after building, run the produced `SecretVault.exe` in `SecretVault.Cli`'s output directory
directly. Commands (from `SecretVault.Cli/Program.cs`):

```
vault init                                     Create a new vault
vault add <name> [--folder <f>] [--generate [length]]
                                                Add a new entry
vault get <name> [--show]                      Copy secret to clipboard (or print with --show)
vault list [folder]                            List entries, optionally filtered by folder
vault search <term>                            Search entries by name/username/folder/notes
vault remove <name>                             Delete an entry
vault generate [length] [--no-symbols]         Print a random password without saving it
vault totp <name>                              Show the current TOTP code for an entry
vault totp-set <name> <secret>                 Attach/update a TOTP secret on an entry
vault field-set <name> <label> <value> [--secret]
                                                Add/update a custom field on an entry
vault field-get <name> <label>                 Print a field, or copy it to clipboard if --secret
vault path                                     Show the vault file location
```

Every command that touches the vault prompts for the master password interactively (masked input,
or read from stdin when redirected). `get` and `field-get --secret` copy the value to the clipboard
and clear it again after 20 seconds.

The UI and CLI both use `FileVaultStore.GetDefaultPath()`, which is
`%LOCALAPPDATA%\SecretVault\vault.db` - so entries added from one are visible in the other.

## Notes

- **Encryption**: the master password is run through Argon2id (`Konscious.Security.Cryptography.Argon2`,
  64 MB memory, 4 iterations, parallelism = processor count) with a random 16-byte salt to derive a
  32-byte key. The serialized vault (JSON) is encrypted with AES-256-GCM (12-byte nonce, 16-byte
  tag). The file format is a simple `SVLT` magic + version byte + salt + nonce + tag + ciphertext.
  Wrong password / corrupted file surfaces as `VaultAuthenticationException`.
- **Known issue - plaintext debug dump**: `FileVaultStore.SaveAsync` (in
  `SecretVault.Core/Storage/FileVaultStore.cs`) unconditionally calls
  `WriteDebugReadableCopyAsync`, which writes an **unencrypted** `vault.readable.json` next to
  `vault.db` containing the full vault contents **and the master password in plaintext**. The code
  comments mark this as temporary debug-only ("TEMP: comment out to stop writing plaintext
  alongside vault.db") but the call is still active. This defeats the encryption entirely as long
  as it remains enabled and should be removed/disabled before treating this app as secure.
- Clipboard contents (from both the UI and CLI) are cleared automatically 20 seconds after a
  secret is copied.
- The security audit score in the desktop app is a simple heuristic (weak/reused password counts,
  master password strength, presence of any TOTP secret) - it is not a breach-checking or
  external-data feature. The "BreachWatch" nav item in `MainForm` is present but disabled
  (`isActive: () => false, onClick: null`) - it is not implemented.
- No import/export, backup, or multi-vault support exists - there's a single fixed vault path per
  machine/user profile.
- Targets `net10.0` (`net10.0-windows` for the WinForms project) with nullable reference types
  enabled.
