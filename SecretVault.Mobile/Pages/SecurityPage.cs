using Microsoft.Maui.Controls.Shapes;
using SecretVault.Crypto;
using SecretVault.Models;

namespace SecretVault.Mobile;

// Security Audit (same score as the desktop: SecretVault.Core's SecurityReport) and BreachWatch.
public sealed class SecurityPage : AppPage
{
    private enum View_ { Overview, Weak, Reused }

    private View_ _view = View_.Overview;
    private Dictionary<VaultEntry, int>? _breaches;
    private DateTime? _lastScan;
    private CancellationTokenSource? _scan;
    private (int Done, int Total) _progress;
    private string? _scanError;

    public SecurityPage() : base("Security") { }

    protected override void Refresh()
    {
        if (VaultSession.Data is not { } data || VaultSession.MasterPassword is not { } password) return;
        var report = SecurityReport.Create(data, password);
        Body = new ScrollView { Content = _view == View_.Overview ? Overview(report) : List(report) };
    }

    private View Overview(SecurityReport report)
    {
        var color = report.Score >= 80 ? Forms.StrengthColor(StrengthLevel.Strong) : report.Score >= 60 ? Forms.StrengthColor(StrengthLevel.Fair) : Forms.StrengthColor(StrengthLevel.Weak);
        var score = Ui.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Ui.Caption("Security score"),
                new HorizontalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        new Label { Text = $"{report.Score}", FontSize = 52, FontAttributes = FontAttributes.Bold, TextColor = color },
                        new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { Ui.Text("/ 100", 16, sub: true), new Label { Text = report.ScoreLabel, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = color } } }
                    }
                },
                new ProgressBar { Progress = report.Score / 100.0, ProgressColor = color, HeightRequest = 6 },
                Check(report.MasterStrong, "Strong master password", "Use a long, unique master password."),
                Check(report.Has2Fa, "Two-factor codes in use", "Add one-time (TOTP) secrets to important accounts."),
                Check(report.Weak.Count == 0, "No weak passwords", $"{report.Weak.Count} weak or fair password(s)."),
                Check(report.Reused.Count == 0, "No reused passwords", $"{report.Reused.Count} record(s) share a password.")
            }
        });

        var weak = IssueCard("Weak passwords", report.Weak.Count, "\"Weak\" and \"Fair\" passwords are easy to guess. Change them on the affected accounts.", View_.Weak);
        var reused = IssueCard("Reused passwords", report.Reused.Count, "If one site leaks, every account with the same password is at risk.", View_.Reused);

        return new VerticalStackLayout { Padding = new Thickness(16, 16, 16, 40), Spacing = 14, Children = { score, weak, reused, BreachWatch() } };
    }

    private static View Check(bool ok, string good, string bad) => new HorizontalStackLayout
    {
        Spacing = 8,
        Children =
        {
            new Label { Text = ok ? "✓" : "!", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = ok ? Forms.StrengthColor(StrengthLevel.Strong) : Forms.StrengthColor(StrengthLevel.Weak), WidthRequest = 16 },
            Ui.Text(ok ? good : bad, 14)
        }
    };

    private View IssueCard(string title, int count, string detail, View_ target)
    {
        var review = Ui.PillButton(count == 0 ? "All good" : "Review");
        review.IsEnabled = count > 0;
        review.Clicked += (_, _) => { _view = target; Refresh(); };
        return Ui.Card(new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 10,
            Children =
            {
                new VerticalStackLayout { Spacing = 3, Children = { Ui.Text($"{title}: {count}", 17, bold: true), Ui.Text(detail, 13, sub: true) } },
                review.Column(1)
            }
        });
    }

    private View List(SecurityReport report)
    {
        var entries = _view == View_.Weak ? report.Weak : report.Entries.Where(report.Reused.Contains).OrderBy(e => e.Secret).ToList();
        var back = Ui.PillButton("‹ Overview");
        back.HorizontalOptions = LayoutOptions.Start;
        back.Clicked += (_, _) => { _view = View_.Overview; Refresh(); };
        var stack = new VerticalStackLayout
        {
            Padding = new Thickness(16, 12, 16, 40), Spacing = 10,
            Children = { back, Ui.Text(_view == View_.Weak ? "Weak passwords" : "Reused passwords", 22, bold: true), Ui.Text("Tap a record, then Edit to change its password.", 13, sub: true) }
        };
        foreach (var entry in entries.OrderBy(e => PasswordStrength.Evaluate(e.Secret)))
            stack.Children.Add(EntryRow(entry, Forms.StrengthName(PasswordStrength.Evaluate(entry.Secret)), Forms.StrengthColor(PasswordStrength.Evaluate(entry.Secret))));
        return stack;
    }

    private Border EntryRow(VaultEntry entry, string badge, Color badgeColor)
    {
        var row = Ui.Card(new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12,
            Children =
            {
                Ui.Avatar(entry.Name, Ui.TypeColor(RecordTypes.Of(entry)), 40),
                new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { Ui.Text(entry.Name, 15, bold: true), Ui.Text(entry.Username, 12, sub: true) } }.Column(1),
                new Label { Text = badge, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = badgeColor, VerticalOptions = LayoutOptions.Center }.Column(2)
            }
        }, padding: 12);
        row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PushAsync(new EntryPage(entry))) });
        return row;
    }

    // ---------- BreachWatch ----------

    private View BreachWatch()
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Ui.Caption("BreachWatch"),
                Ui.Text("Checks your passwords against Have I Been Pwned. Only the first 5 characters of each password's SHA-1 hash are sent — never the password.", 13, sub: true)
            }
        };

        if (_scan is not null)
        {
            stack.Children.Add(new ProgressBar { Progress = _progress.Total == 0 ? 0 : (double)_progress.Done / _progress.Total, ProgressColor = Ui.Primary, HeightRequest = 6 });
            stack.Children.Add(Ui.Text($"Checking {_progress.Done} of {_progress.Total}…", 13, sub: true));
            var cancel = Ui.SecondaryButton("Cancel");
            cancel.Clicked += (_, _) => _scan?.Cancel();
            stack.Children.Add(cancel);
        }
        else
        {
            var scan = Ui.PrimaryButton(_breaches is null ? "Scan my passwords" : "Scan again");
            scan.Clicked += async (_, _) => await ScanAsync();
            stack.Children.Add(scan);
        }

        if (_scanError is not null) stack.Children.Add(Ui.Text(_scanError, 13).Themed(Label.TextColorProperty, Ui.DangerLight, Ui.DangerDark));
        if (_breaches is not null)
        {
            var exposed = _breaches.Where(p => p.Value > 0 && VaultSession.Vault.Entries.Contains(p.Key)).OrderByDescending(p => p.Value).ToList();
            stack.Children.Add(Ui.Text(exposed.Count == 0
                ? $"✓ None of your passwords appear in known breaches (checked {_lastScan:HH:mm})."
                : $"{exposed.Count} record(s) use a password found in breaches. Change them.", 14, bold: true));
            foreach (var (entry, count) in exposed)
                stack.Children.Add(EntryRow(entry, $"Seen {count:N0}×", Ui.DangerLight));
        }
        return Ui.Card(stack);
    }

    private async Task ScanAsync()
    {
        _scan = new CancellationTokenSource();
        _scanError = null;
        _progress = (0, 0);
        Refresh();
        try
        {
            using var checker = new BreachChecker();
            var progress = new Progress<(int Done, int Total)>(p => { _progress = p; Refresh(); });
            _breaches = await checker.ScanAsync(VaultSession.LiveEntries, progress, _scan.Token);
            _lastScan = DateTime.Now;
        }
        catch (OperationCanceledException) when (_scan.IsCancellationRequested)
        {
            _scanError = "Scan cancelled.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _scanError = "Couldn't reach haveibeenpwned.com. Check your internet connection and try again.";
        }
        finally
        {
            _scan.Dispose();
            _scan = null;
        }
        Refresh();
    }
}
