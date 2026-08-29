using System.Text.Json;
using Server.Accounting.Security;

namespace ShardAccountKeeper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly string _bridge = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Distribution", "Data", "AccountKeeper");
    private readonly ListView _accounts = new() { Dock = DockStyle.Fill, FullRowSelect = true, View = View.Details };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Width = 230 };
    private readonly Label _selected = new() { AutoSize = true, Text = "Select an account" };

    public MainForm()
    {
        Text = "Shard Account Keeper";
        Width = 760; Height = 520; StartPosition = FormStartPosition.CenterScreen;
        _accounts.Columns.AddRange([new ColumnHeader { Text = "Account", Width = 180 }, new ColumnHeader { Text = "Access", Width = 110 }, new ColumnHeader { Text = "Characters", Width = 90 }, new ColumnHeader { Text = "Last Login", Width = 190 }, new ColumnHeader { Text = "Banned", Width = 70 }]);
        _accounts.SelectedIndexChanged += (_, _) => _selected.Text = _accounts.SelectedItems.Count == 1 ? $"Reset password for: {_accounts.SelectedItems[0].Text}" : "Select an account";
        var reset = new Button { Text = "Set New Password", AutoSize = true };
        reset.Click += (_, _) => ResetPassword();
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => RefreshAccounts();
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(12) };
        footer.Controls.AddRange([_selected, new Label { Text = "   New password:" }, _password, reset, refresh]);
        Controls.Add(_accounts); Controls.Add(footer);
        RefreshAccounts();
    }

    private void RefreshAccounts()
    {
        var path = Path.GetFullPath(Path.Combine(_bridge, "accounts.json"));
        if (!File.Exists(path)) { Text = "Shard Account Keeper — start ModernUO first"; return; }
        var data = JsonSerializer.Deserialize<List<AccountSummary>>(File.ReadAllText(path)) ?? [];
        _accounts.Items.Clear();
        foreach (var a in data) _accounts.Items.Add(new ListViewItem([a.Username, a.AccessLevel, a.Characters.ToString(), a.LastLogin.ToLocalTime().ToString("g"), a.Banned ? "Yes" : "No"]));
        Text = $"Shard Account Keeper — {data.Count} accounts";
    }

    private void ResetPassword()
    {
        if (_accounts.SelectedItems.Count != 1 || string.IsNullOrWhiteSpace(_password.Text)) { MessageBox.Show("Select one account and enter a new password."); return; }
        var username = _accounts.SelectedItems[0].Text;
        var hash = Argon2PasswordProtection.Instance.EncryptPassword(_password.Text);
        _password.Clear();
        Directory.CreateDirectory(_bridge);
        var request = JsonSerializer.Serialize(new PasswordResetRequest(username, hash));
        File.WriteAllText(Path.Combine(_bridge, $"reset-{Guid.NewGuid():N}.json"), request);
        MessageBox.Show("Reset request sent. ModernUO applies it within five seconds.", "Shard Account Keeper");
    }

    private sealed record AccountSummary(string Username, string AccessLevel, int Characters, DateTime LastLogin, bool Banned);
    private sealed record PasswordResetRequest(string Username, string Argon2Hash);
}
