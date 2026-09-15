using SecretVault.Crypto;
using SecretVault.Models;
using SecretVault.Native;
using SecretVault.Storage;

var store = new FileVaultStore();

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "init":
            return await InitAsync();
        case "add":
            return await AddAsync(args);
        case "get":
            return await GetAsync(args);
        case "list":
            return await ListAsync(args);
        case "search":
            return await SearchAsync(args);
        case "remove":
            return await RemoveAsync(args);
        case "generate":
            RunGenerate(args);
            return 0;
        case "totp":
            return await TotpAsync(args);
        case "totp-set":
            return await TotpSetAsync(args);
        case "field-set":
            return await FieldSetAsync(args);
        case "field-get":
            return await FieldGetAsync(args);
        case "path":
            Console.WriteLine(FileVaultStore.GetDefaultPath());
            return 0;
        default:
            PrintUsage();
            return 1;
    }
}
catch (VaultAuthenticationException)
{
    Console.Error.WriteLine("Incorrect master password or corrupted vault.");
    return 1;
}
catch (FileNotFoundException)
{
    Console.Error.WriteLine("Vault not found. Run 'vault init' first.");
    return 1;
}

async Task<int> InitAsync()
{
    if (store.Exists())
    {
        Console.Error.WriteLine("Vault already exists at: " + FileVaultStore.GetDefaultPath());
        return 1;
    }

    var password = ReadPassword("Create master password: ");
    var confirm = ReadPassword("Confirm master password: ");
    if (password != confirm)
    {
        Console.Error.WriteLine("Passwords do not match.");
        return 1;
    }

    await store.SaveAsync(new VaultData(), password);
    Console.WriteLine("Vault created at: " + FileVaultStore.GetDefaultPath());
    return 0;
}

async Task<int> AddAsync(string[] cmdArgs)
{
    if (cmdArgs.Length < 2)
    {
        Console.Error.WriteLine("Usage: vault add <name> [--folder <folder>] [--generate [length]]");
        return 1;
    }

    var name = cmdArgs[1];
    var folder = GetOption(cmdArgs, "--folder") ?? "General";
    var generateOption = GetOption(cmdArgs, "--generate");
    var shouldGenerate = cmdArgs.Contains("--generate");

    var password = ReadPassword("Master password: ");
    var data = await store.LoadAsync(password);

    if (data.Entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine($"An entry named '{name}' already exists.");
        return 1;
    }

    Console.Write("Username: ");
    var username = Console.ReadLine() ?? string.Empty;

    string secret;
    if (shouldGenerate)
    {
        var length = int.TryParse(generateOption, out var len) ? len : 20;
        secret = PasswordGenerator.Generate(length);
        Console.WriteLine($"Generated secret: {secret}  [{PasswordStrength.Evaluate(secret)}]");
    }
    else
    {
        secret = ReadPassword("Secret: ");
        Console.WriteLine($"Strength: {PasswordStrength.Evaluate(secret)}");
    }

    Console.Write("Notes (optional): ");
    var notes = Console.ReadLine() ?? string.Empty;

    var now = DateTimeOffset.UtcNow;
    data.Entries.Add(new VaultEntry
    {
        Name = name,
        Folder = folder,
        Username = username,
        Secret = secret,
        Notes = notes,
        CreatedAt = now,
        ModifiedAt = now
    });

    await store.SaveAsync(data, password);
    Console.WriteLine($"Entry '{name}' saved in folder '{folder}'.");
    return 0;
}

async Task<int> GetAsync(string[] cmdArgs)
{
    if (cmdArgs.Length < 2)
    {
        Console.Error.WriteLine("Usage: vault get <name> [--show]");
        return 1;
    }

    var name = cmdArgs[1];
    var show = cmdArgs.Contains("--show");

    var password = ReadPassword("Master password: ");
    var data = await store.LoadAsync(password);

    var entry = data.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (entry is null)
    {
        Console.Error.WriteLine($"No entry named '{name}'.");
        return 1;
    }

    Console.WriteLine($"Name:     {entry.Name}");
    Console.WriteLine($"Folder:   {entry.Folder}");
    Console.WriteLine($"Username: {entry.Username}");
    if (!string.IsNullOrEmpty(entry.Notes))
        Console.WriteLine($"Notes:    {entry.Notes}");

    foreach (var field in entry.CustomFields)
    {
        var value = field.IsSecret && !show ? "•••• (use 'field-get' to reveal)" : field.Value;
        Console.WriteLine($"{field.Label,-9} {value}");
    }

    if (show)
    {
        Console.WriteLine($"Secret:   {entry.Secret}");
        return 0;
    }

    const int clearAfterSeconds = 20;
    ClipboardHelper.SetText(entry.Secret);
    Console.WriteLine($"Secret copied to clipboard. Clearing in {clearAfterSeconds}s...");
    await Task.Delay(TimeSpan.FromSeconds(clearAfterSeconds));

    if (ClipboardHelper.GetText() == entry.Secret)
        ClipboardHelper.SetText(string.Empty);

    return 0;
}

async Task<int> ListAsync(string[] cmdArgs)
{
    var folderFilter = cmdArgs.Length > 1 ? cmdArgs[1] : null;

    var password = ReadPassword("Master password: ");
    var data = await store.LoadAsync(password);

    var entries = data.Entries.AsEnumerable();
    if (folderFilter is not null)
        entries = entries.Where(e => e.Folder.Equals(folderFilter, StringComparison.OrdinalIgnoreCase));

    var grouped = entries
        .OrderBy(e => e.Folder, StringComparer.OrdinalIgnoreCase)
        .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .GroupBy(e => e.Folder);

    var any = false;
    foreach (var group in grouped)
    {
        any = true;
        Console.WriteLine($"[{group.Key}]");
        foreach (var entry in group)
        {
            var totpMark = entry.TotpSecret is not null ? " (TOTP)" : "";
            Console.WriteLine($"  {entry.Name,-24} {entry.Username}{totpMark}");
        }
    }

    if (!any)
        Console.WriteLine(folderFilter is null ? "Vault is empty." : $"No entries in folder '{folderFilter}'.");

    return 0;
}

async Task<int> SearchAsync(string[] cmdArgs)
{
    if (cmdArgs.Length < 2)
    {
        Console.Error.WriteLine("Usage: vault search <term>");
        return 1;
    }

    var term = cmdArgs[1];
    var password = ReadPassword("Master password: ");
    var data = await store.LoadAsync(password);

    var matches = data.Entries
        .Where(e =>
            e.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.Username.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.Folder.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.Notes.Contains(term, StringComparison.OrdinalIgnoreCase))
        .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (matches.Count == 0)
    {
        Console.WriteLine($"No entries match '{term}'.");
        return 0;
    }

    foreach (var entry in matches)
        Console.WriteLine($"[{entry.Folder}] {entry.Name,-24} {entry.Username}");
    return 0;
}

async Task<int> RemoveAsync(string[] cmdArgs)
{
    if (cmdArgs.Length < 2)
    {
        Console.Error.WriteLine("Usage: vault remove <name>");
        return 1;
    }

    var name = cmdArgs[1];
    var password = ReadPassword("Master password: ");
    var data = await store.LoadAsync(password);

    var removed = data.Entries.RemoveAll(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (removed == 0)
    {
        Console.Error.WriteLine($"No entry named '{name}'.");
        return 1;
    }

    await store.SaveAsync(data, password);
    Console.WriteLine($"Entry '{name}' removed.");
    return 0;
}

void RunGenerate(string[] cmdArgs)
{
    var length = cmdArgs.Length > 1 && int.TryParse(cmdArgs[1], out var len) ? len : 20;
    var useSymbols = !cmdArgs.Contains("--no-symbols");
    var password = PasswordGenerator.Generate(length, useSymbols);
    Console.WriteLine($"{password}  [{PasswordStrength.Evaluate(password)}]");
}

async Task<int> TotpAsync(string[] cmdArgs)
{
    if (cmdArgs.Length < 2)
    {
        Console.Error.WriteLine("Usage: vault totp <name>");
        return 1;
    }

    var name = cmdArgs[1];
    var password = ReadPassword("Master password: ");
    var data = await store.LoadAsync(password);

    var entry = data.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (entry is null)
    {
        Console.Error.WriteLine($"No entry named '{name}'.");
        return 1;
    }

    if (entry.TotpSecret is null)
    {
        Console.Error.WriteLine($"Entry '{name}' has no TOTP secret. Use 'vault totp-set {name} <secret>' first.");
        return 1;
    }

    var code = TotpGenerator.GenerateCode(entry.TotpSecret);
    var remaining = TotpGenerator.SecondsRemaining();
    Console.WriteLine($"{code}  (valid for {remaining}s)");
    return 0;
}

async Task<int> TotpSetAsync(string[] cmdArgs)
{
    if (cmdArgs.Length < 3)
    {
        Console.Error.WriteLine("Usage: vault totp-set <name> <base32-secret>");
        return 1;
    }

    var name = cmdArgs[1];
    var secret = cmdArgs[2];
    var password = ReadPassword("Master password: ");
    var data = await store.LoadAsync(password);

    var entry = data.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (entry is null)
    {
        Console.Error.WriteLine($"No entry named '{name}'.");
        return 1;
    }

    entry.TotpSecret = secret;
    entry.ModifiedAt = DateTimeOffset.UtcNow;

    await store.SaveAsync(data, password);
    Console.WriteLine($"TOTP secret set for '{name}'.");
    return 0;
}

async Task<int> FieldSetAsync(string[] cmdArgs)
{
    if (cmdArgs.Length < 4)
    {
        Console.Error.WriteLine("Usage: vault field-set <name> <label> <value> [--secret]");
        return 1;
    }

    var name = cmdArgs[1];
    var label = cmdArgs[2];
    var value = cmdArgs[3];
    var isSecret = cmdArgs.Contains("--secret");

    var password = ReadPassword("Master password: ");
    var data = await store.LoadAsync(password);

    var entry = data.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (entry is null)
    {
        Console.Error.WriteLine($"No entry named '{name}'.");
        return 1;
    }

    var field = entry.CustomFields.FirstOrDefault(f => f.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
    if (field is null)
    {
        field = new CustomField { Label = label };
        entry.CustomFields.Add(field);
    }
    field.Value = value;
    field.Type = isSecret ? CustomFieldType.Password : CustomFieldType.Text;
    entry.ModifiedAt = DateTimeOffset.UtcNow;

    await store.SaveAsync(data, password);
    Console.WriteLine($"Field '{label}' set on '{name}'.");
    return 0;
}

async Task<int> FieldGetAsync(string[] cmdArgs)
{
    if (cmdArgs.Length < 3)
    {
        Console.Error.WriteLine("Usage: vault field-get <name> <label>");
        return 1;
    }

    var name = cmdArgs[1];
    var label = cmdArgs[2];

    var password = ReadPassword("Master password: ");
    var data = await store.LoadAsync(password);

    var entry = data.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (entry is null)
    {
        Console.Error.WriteLine($"No entry named '{name}'.");
        return 1;
    }

    var field = entry.CustomFields.FirstOrDefault(f => f.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
    if (field is null)
    {
        Console.Error.WriteLine($"No field '{label}' on entry '{name}'.");
        return 1;
    }

    if (!field.IsSecret)
    {
        Console.WriteLine(field.Value);
        return 0;
    }

    const int clearAfterSeconds = 20;
    ClipboardHelper.SetText(field.Value);
    Console.WriteLine($"'{label}' copied to clipboard. Clearing in {clearAfterSeconds}s...");
    await Task.Delay(TimeSpan.FromSeconds(clearAfterSeconds));

    if (ClipboardHelper.GetText() == field.Value)
        ClipboardHelper.SetText(string.Empty);

    return 0;
}

string? GetOption(string[] cmdArgs, string flag)
{
    var idx = Array.IndexOf(cmdArgs, flag);
    if (idx < 0 || idx + 1 >= cmdArgs.Length) return null;
    var value = cmdArgs[idx + 1];
    return value.StartsWith("--") ? null : value;
}

string ReadPassword(string prompt)
{
    Console.Write(prompt);

    if (Console.IsInputRedirected)
        return Console.ReadLine() ?? string.Empty;

    var sb = new System.Text.StringBuilder();
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            if (sb.Length > 0)
            {
                sb.Length--;
                Console.Write("\b \b");
            }
            continue;
        }
        if (!char.IsControl(key.KeyChar))
        {
            sb.Append(key.KeyChar);
            Console.Write("*");
        }
    }
    Console.WriteLine();
    return sb.ToString();
}

void PrintUsage()
{
    Console.WriteLine("""
        SecretVault - offline encrypted secret store

        Usage:
          vault init                        Create a new vault
          vault add <name> [--folder <f>] [--generate [length]]
                                             Add a new entry
          vault get <name> [--show]         Copy secret to clipboard (or print with --show)
          vault list [folder]               List entries, optionally filtered by folder
          vault search <term>               Search entries by name/username/folder/notes
          vault remove <name>               Delete an entry
          vault generate [length] [--no-symbols]
                                             Print a random password without saving it
          vault totp <name>                 Show the current TOTP code for an entry
          vault totp-set <name> <secret>    Attach/update a TOTP secret on an entry
          vault field-set <name> <label> <value> [--secret]
                                             Add/update a custom field on an entry
          vault field-get <name> <label>    Print a field, or copy it to clipboard if --secret
          vault path                        Show the vault file location
        """);
}
