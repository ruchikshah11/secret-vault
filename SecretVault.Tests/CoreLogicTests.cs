using SecretVault.Crypto;
using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Tests;

public class CsvImportTests
{
    [Fact]
    public void Chrome_export_with_quotes_commas_and_newlines()
    {
        var csv = "name,url,username,password,note\r\nGitHub,https://github.com/login,ruchik,\"p,a\"\"ss\",\"multi\nline\"\r\n,https://www.example.com,u,p,\r\n";
        var result = CsvTransfer.Import(csv);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("p,a\"ss", result.Entries[0].Secret);
        Assert.Equal("multi\nline", result.Entries[0].Notes);
        Assert.Equal("example.com", result.Entries[1].Name);       // name from website when blank
        Assert.Equal("Imported", result.Entries[0].Folder);
    }

    [Fact]
    public void Bitwarden_export_skips_cards_and_reads_otpauth()
    {
        var csv = "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\n" +
                  "Work,1,login,AWS,,,0,https://aws.amazon.com,me,pw,otpauth://totp/AWS?secret=JBSWY3DPEHPK3PXP&issuer=AWS\n" +
                  ",,card,Visa,,,0,,,,\n";
        var result = CsvTransfer.Import(csv);
        var aws = Assert.Single(result.Entries);
        Assert.Equal(1, result.SkippedRows);
        Assert.True(aws.Favorite);
        Assert.Equal("Work", aws.Folder);
        Assert.Equal("JBSWY3DPEHPK3PXP", aws.TotpSecret);
    }

    [Fact]
    public void Keeper_headerless_export_maps_columns_and_custom_fields()
    {
        // Folder, Title, Login, Password, Website, Notes, Shared Folder, then name/value pairs.
        var csv = "Azure,Graph app,svc,pw,https://portal.azure.com,note,,Client ID,abc,Client Secret,xyz,Client Secret Expiry Date,2027-01-01\n" +
                  ",Prod VM,admin,pw2,,,Shared IT,Hostname or IP Address,10.0.0.4,TFC:Keeper,otpauth://totp/x?secret=ABCDEF\n";
        var entries = CsvTransfer.Import(csv).Entries;
        Assert.Equal(2, entries.Count);

        var app = entries[0];
        Assert.Equal(("Azure", "Graph app", "svc", "pw", "https://portal.azure.com"), (app.Folder, app.Name, app.Username, app.Secret, app.Url));
        Assert.Equal(CustomFieldType.Text, app.CustomFields.Single(f => f.Label == "Client ID").Type);
        Assert.Equal(CustomFieldType.Password, app.CustomFields.Single(f => f.Label == "Client Secret").Type);
        Assert.Equal(CustomFieldType.Text, app.CustomFields.Single(f => f.Label == "Client Secret Expiry Date").Type); // a date, not a secret

        var vm = entries[1];
        Assert.Equal("Shared IT", vm.Folder);                 // falls back to the shared folder
        Assert.Equal("ABCDEF", vm.TotpSecret);                // 2FA becomes the one-time code, not a custom field
        Assert.DoesNotContain(vm.CustomFields, f => f.Label.StartsWith("TFC"));
    }

    [Fact]
    public void Export_then_import_round_trips_everything()
    {
        var entry = new VaultEntry { Name = "N", Folder = "F", Url = "u", Username = "x", Secret = "a,\"b\"", Notes = "n\nn", Tags = { "t1", "t2" }, Favorite = true };
        var back = Assert.Single(CsvTransfer.Import(CsvTransfer.Export(new[] { entry })).Entries);
        Assert.Equal((entry.Secret, entry.Notes, entry.Url, true), (back.Secret, back.Notes, back.Url, back.Favorite));
        Assert.Equal(entry.Tags, back.Tags);
    }

    [Fact]
    public void Unrecognised_csv_is_rejected()
    {
        Assert.Throws<FormatException>(() => CsvTransfer.Import("foo,bar\n1,2"));
    }

    [Theory]
    [InlineData("https://www.github.com/login", "github.com")]
    [InlineData("portal.azure.com", "portal.azure.com")]
    [InlineData("", null)]
    public void Host_is_extracted_from_urls(string url, string? host) => Assert.Equal(host, CsvTransfer.HostOf(url));
}

public class PasswordGeneratorTests
{
    [Fact]
    public void Characters_respect_options_and_length()
    {
        var pw = PasswordGenerator.Generate(new PasswordOptions { Length = 32, Symbols = false, Digits = false });
        Assert.Equal(32, pw.Length);
        Assert.All(pw, c => Assert.True(char.IsLetter(c)));
    }

    [Fact]
    public void Every_enabled_character_class_is_present()
    {
        for (var i = 0; i < 50; i++)
        {
            var pw = PasswordGenerator.Generate(new PasswordOptions { Length = 8 });
            Assert.Contains(pw, char.IsUpper);
            Assert.Contains(pw, char.IsLower);
            Assert.Contains(pw, char.IsDigit);
            Assert.Contains(pw, c => !char.IsLetterOrDigit(c));
        }
    }

    [Fact]
    public void Passphrase_has_requested_words_and_a_number()
    {
        var phrase = PasswordGenerator.Generate(new PasswordOptions { Passphrase = true, Words = 5, Separator = "-" });
        Assert.Equal(5, phrase.Split('-').Length);
        Assert.Contains(phrase, char.IsDigit);
    }

    [Fact]
    public void Generated_passwords_are_not_repeated()
    {
        var set = Enumerable.Range(0, 200).Select(_ => PasswordGenerator.Generate(20)).ToHashSet();
        Assert.Equal(200, set.Count);
    }

    [Fact]
    public void Legacy_overload_still_works() => Assert.Equal(16, PasswordGenerator.Generate(16).Length);
}

public class ModelTests
{
    [Fact]
    public void Password_history_is_newest_first_capped_and_ignores_unchanged()
    {
        var entry = new VaultEntry { Secret = "current" };
        for (var i = 0; i < 12; i++) entry.RememberPreviousPassword("old" + i, DateTimeOffset.UtcNow);
        entry.RememberPreviousPassword("current", DateTimeOffset.UtcNow);
        entry.RememberPreviousPassword("", DateTimeOffset.UtcNow);

        Assert.Equal(VaultEntry.MaxPasswordHistory, entry.PasswordHistory.Count);
        Assert.Equal("old11", entry.PasswordHistory[0].Password);
    }

    [Theory]
    [InlineData("Hostname or IP Address", RecordTypes.Server)]
    [InlineData("Client Secret", RecordTypes.AppCredentials)]
    [InlineData("Thumbprint", RecordTypes.Certificate)]
    [InlineData("Database Name", RecordTypes.Database)]
    [InlineData("License Key", RecordTypes.SoftwareLicense)]
    public void Record_type_is_detected_from_fields(string label, string expected)
    {
        var entry = new VaultEntry { Username = "u", Secret = "p", CustomFields = { new CustomField { Label = label, Value = "x" } } };
        Assert.Equal(expected, RecordTypes.Of(entry));
    }

    [Fact]
    public void Record_type_defaults_and_explicit_choice_wins()
    {
        Assert.Equal(RecordTypes.Login, RecordTypes.Of(new VaultEntry { Username = "u" }));
        Assert.Equal(RecordTypes.SecureNote, RecordTypes.Of(new VaultEntry { Notes = "just a note" }));
        Assert.Equal(RecordTypes.Certificate, RecordTypes.Of(new VaultEntry { Username = "u", Type = "certificate" }));
        Assert.Equal(RecordTypes.Login, RecordTypes.Of(new VaultEntry { Username = "u", Type = "nonsense" })); // unknown => detect
    }

    [Fact]
    public void Recovery_keys_have_the_expected_shape_and_no_lookalike_characters()
    {
        var key = RecoveryKey.Generate();
        Assert.Matches("^[A-Z2-9]{5}(-[A-Z2-9]{5}){5}$", key);
        Assert.DoesNotContain(key, c => "IO01".Contains(c));
        Assert.True(RecoveryKey.LooksValid(key.ToLowerInvariant().Replace("-", " ")));
        Assert.False(RecoveryKey.LooksValid("too-short"));
        Assert.NotEqual(RecoveryKey.Generate(), RecoveryKey.Generate());
    }
}

[Trait("Category", "Network")]
public class BreachWatchTests
{
    [Fact]
    public async Task Known_breached_password_is_found_and_random_one_is_not()
    {
        using var checker = new BreachChecker();
        Assert.True(await checker.GetBreachCountAsync("password") > 1000);
        Assert.Equal(0, await checker.GetBreachCountAsync(PasswordGenerator.Generate(32)));
    }
}
