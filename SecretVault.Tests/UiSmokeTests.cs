using System.Reflection;
using SecretVault.Gui;
using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Tests;

// Opens the real windows off-screen and checks nothing throws. WinForms needs an STA thread with a message loop.
// The app reads/writes the user's preferences file, so it is backed up and restored around every test.
public sealed class UiSmokeTests : IDisposable
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecretVault", "settings.json");
    private readonly byte[]? _settingsBackup = File.Exists(SettingsPath) ? File.ReadAllBytes(SettingsPath) : null;

    public UiSmokeTests()
    {
        // Tests must not depend on the user's own layout (e.g. a collapsed RECORD TYPES section). In memory only;
        // the settings file itself is restored in Dispose.
        AppSettings.Current.CollapsedSections.Clear();
        AppSettings.Current.ExpandedFolders.Clear();
        AppSettings.Current.FailedUnlockAttempts = 0;
        AppSettings.Current.UnlockBlockedUntilUtc = null;
    }

    public void Dispose()
    {
        if (_settingsBackup is not null) File.WriteAllBytes(SettingsPath, _settingsBackup);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application.ThreadException += (_, e) => failure ??= e.Exception;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            try { action(); }
            catch (Exception ex) { failure ??= ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("UI failure: " + failure.Message, failure);
    }

    // Shows a form off-screen, runs `whenShown`, then closes it.
    private static void ShowBriefly(Form form, Action<Form>? whenShown = null)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-5000, 0);
        form.ShowInTaskbar = false;
        form.Shown += (_, _) =>
        {
            try
            {
                whenShown?.Invoke(form);
                Application.DoEvents();
            }
            finally
            {
                // Always close — even when an assertion fails — so a failing test can't hang the run.
                form.DialogResult = DialogResult.OK; // close without confirmation prompts
                form.Close();
            }
        };
        form.ShowDialog();
    }

    private static VaultData SampleVault()
    {
        var now = DateTimeOffset.Now;
        var data = new VaultData();
        foreach (var folder in new[] { "Application", @"bsdev\Aditi", @"bsdev\Nikhil", @"bs one\SaaS" })
            data.Entries.Add(new VaultEntry { Name = "R " + folder, Folder = folder, Username = "u", Secret = "x", Url = "example.com", Tags = { "t" }, CreatedAt = now, ModifiedAt = now });
        data.Entries.Add(new VaultEntry { Name = "VM", Folder = "Azure VM", CustomFields = { new CustomField { Label = "Hostname", Value = "10.0.0.4" } }, CreatedAt = now, ModifiedAt = now });
        data.PaymentCards.Add(new PaymentCard { CardholderName = "A", CardNumber = "4111111111111111" });
        return data;
    }

    [Fact]
    public void Main_window_opens_every_page_and_selects_records()
    {
        RunSta(() =>
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var form = new MainForm(SampleVault(), "test-password");
            ShowBriefly(form, f =>
            {
                var switchView = typeof(MainForm).GetMethod("SwitchView", flags)!;
                var viewType = typeof(MainForm).GetNestedType("MainView", BindingFlags.NonPublic)!;
                foreach (var view in Enum.GetNames(viewType))
                {
                    switchView.Invoke(f, new[] { Enum.Parse(viewType, view) });
                    Application.DoEvents();
                }
                switchView.Invoke(f, new[] { Enum.Parse(viewType, "Vault") });

                var nav = (NavListView)typeof(MainForm).GetField("_folderList", flags)!.GetValue(f)!;
                var records = (NavListView)typeof(MainForm).GetField("_entryListBox", flags)!.GetValue(f)!;
                Assert.Contains(nav.Items, i => i.ToString() == "RECORD TYPES");
                Assert.Contains(nav.Items, i => i.ToString() == "FOLDERS");
                Assert.Equal(5, records.Items.Count);

                records.SelectedIndex = 0;                          // opens the details card
                Application.DoEvents();
                var server = nav.Items.FindIndex(i => i.ToString() == RecordTypes.Server);
                nav.SelectedIndex = server;                         // filter by record type
                Application.DoEvents();
                Assert.Equal("VM", ((VaultEntry)records.Items.Single()).Name);

                // After the list is rebuilt nothing is selected, and clicking the record opens its details.
                var details = (Control)typeof(MainForm).GetField("_detailPanel", flags)!.GetValue(f)!;
                Assert.Equal(-1, records.SelectedIndex);
                Assert.False(details.Visible);
                records.SelectedIndex = 0;
                Application.DoEvents();
                Assert.True(details.Visible);
            });
        });
    }

    [Fact]
    public void Dialogs_open_without_errors()
    {
        RunSta(() =>
        {
            var data = SampleVault();
            ShowBriefly(new EntryEditForm(new[] { "Application" }));
            ShowBriefly(new EntryEditForm(new[] { "Application" }, data.Entries[0]));
            ShowBriefly(new PaymentCardEditForm(data.PaymentCards[0]));
            ShowBriefly(new GeneratorForm());
            ShowBriefly(new ChangePasswordForm("current"));
            ShowBriefly(new NewPasswordForm("Title", "Message"));
            ShowBriefly(new RecoveryKeyForm(RecoveryKey.Generate()));
            ShowBriefly(new ForgotPasswordForm(new FileVaultStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db"))));
            ShowBriefly(new PasswordPromptForm("Title", "Message"));
            ShowBriefly(new TextPromptForm("New folder", "Name of the new folder."));
            ShowBriefly(new FolderPickerForm(new[] { @"Work\Servers", "Personal" }, @"Work\Servers"));

            // Every style of the app dialog, including destructive ones (which focus the safe button).
            var ctor = typeof(AppDialog).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            foreach (var (kind, danger, secondary) in new (AppDialog.Kind, bool, string?)[]
                     { (AppDialog.Kind.Danger, true, "Cancel"), (AppDialog.Kind.Question, false, "Cancel"), (AppDialog.Kind.Success, false, null), (AppDialog.Kind.Warning, false, null) })
                ShowBriefly((Form)ctor.Invoke(new object?[] { "Title", "A message that is long enough to wrap onto a second line in the dialog.", kind, "Confirm", secondary, danger }));
        });
    }

    [Fact]
    public void Empty_folders_show_in_the_tree_and_new_record_button_follows_the_view()
    {
        RunSta(() =>
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var data = SampleVault();
            data.Folders.Add("Projects"); // created by the user, no records yet
            var form = new MainForm(data, "test-password");
            ShowBriefly(form, f =>
            {
                var nav = (NavListView)typeof(MainForm).GetField("_folderList", flags)!.GetValue(f)!;
                var projects = nav.Items.Single(i => i.ToString() == "Projects");
                Assert.Equal(0, (int)projects.GetType().GetProperty("Count")!.GetValue(projects)!);

                var newRecord = (Control)typeof(MainForm).GetField("_newRecordButton", flags)!.GetValue(f)!;
                var switchView = typeof(MainForm).GetMethod("SwitchView", flags)!;
                var viewType = typeof(MainForm).GetNestedType("MainView", BindingFlags.NonPublic)!;
                Assert.True(newRecord.Visible);
                switchView.Invoke(f, new[] { Enum.Parse(viewType, "Deleted") });
                Assert.False(newRecord.Visible);
            });
        });
    }

    [Fact]
    public void New_record_prefills_the_selected_folder_and_record_type()
    {
        RunSta(() =>
        {
            var form = new EntryEditForm(new[] { "Work" }, defaultFolder: @"Infrastructure\Servers", defaultType: RecordTypes.Server);
            ShowBriefly(form, f =>
            {
                var folder = (TextBox)typeof(EntryEditForm).GetField("_folderBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f)!;
                var type = (string?)typeof(EntryEditForm).GetField("_chosenType", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f);
                Assert.Equal(@"Infrastructure\Servers", folder.Text);
                Assert.Equal(RecordTypes.Server, type);
            });
        });
    }

    [Fact]
    public void Folder_picker_shows_a_collapsed_tree_searches_and_returns_the_choice()
    {
        RunSta(() =>
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var folders = new[] { "Work", @"bsdev\Aditi", @"bsdev\Nikhil", @"bsdev\Ruchik", @"bs one\SaaS", @"bs one\Non SaaS" };
            var picker = new FolderPickerForm(folders, @"bs one\SaaS", "Move", "Move");
            ShowBriefly(picker, f =>
            {
                var list = (NavListView)typeof(FolderPickerForm).GetField("_list", flags)!.GetValue(f)!;
                var search = (TextBox)typeof(FolderPickerForm).GetField("_searchBox", flags)!.GetValue(f)!;
                var path = (TextBox)typeof(FolderPickerForm).GetField("_pathBox", flags)!.GetValue(f)!;
                List<string> Rows() => list.Items.Select(i => i.GetType().GetProperty("Path")!.GetValue(i)!.ToString()!).ToList();

                // Top level plus the opened branch of the current folder; other branches stay collapsed.
                Assert.Equal(new[] { "bs one", @"bs one\Non SaaS", @"bs one\SaaS", "bsdev", "General", "Work" }, Rows());
                Assert.Equal(@"bs one\SaaS", path.Text);

                search.Text = "nik";                                 // flat search across all levels
                Assert.Equal(new[] { @"bsdev\Nikhil" }, Rows());

                list.SelectedIndex = 0;
                Assert.Equal(@"bsdev\Nikhil", path.Text);
                typeof(FolderPickerForm).GetMethod("Accept", flags)!.Invoke(f, null);
                Assert.Equal(@"bsdev\Nikhil", ((FolderPickerForm)f).SelectedFolder);
            });
        });
    }

    [Theory]
    [InlineData("4111111111111111", CardBrand.Visa)]
    [InlineData("5500000000005678", CardBrand.Mastercard)]
    [InlineData("2221000000000009", CardBrand.Mastercard)]
    [InlineData("378282246310005", CardBrand.Amex)]
    [InlineData("6521500000003213", CardBrand.RuPay)]
    [InlineData("6011000000001117", CardBrand.Discover)]
    [InlineData("3530111333300000", CardBrand.Jcb)]
    [InlineData("30569309025904", CardBrand.Diners)]
    [InlineData("6200000000000005", CardBrand.UnionPay)]
    [InlineData("1223000000003213", CardBrand.Unknown)]
    public void Card_brand_detection(string number, CardBrand expected) => Assert.Equal(expected, CardBrands.Detect(number));
}
