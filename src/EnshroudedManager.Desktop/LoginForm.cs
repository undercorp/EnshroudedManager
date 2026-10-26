using EnshroudedManager.Shared;

namespace EnshroudedManager.Desktop;

/// <summary>Asks for the service address and password; creates the password on first use.</summary>
public class LoginForm : Form
{
    private readonly TextBox _url = new() { Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _confirm = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly Label _confirmLabel = new() { Text = L.T("Confirmer", "Confirm"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _intro = new() { AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(0, 0, 0, 10) };
    private readonly Label _error = new() { AutoSize = true, ForeColor = Ui.Err, MaximumSize = new Size(380, 0) };
    private readonly Button _ok = new() { Text = L.T("Se connecter", "Sign in"), AutoSize = true, Padding = new Padding(10, 3, 10, 3) };
    private bool _setup;

    public ApiClient? Client { get; private set; }

    public LoginForm(string url, string? message = null)
    {
        Text = L.T("Connexion — Enshrouded Manager", "Sign in — Enshrouded Manager");
        Icon = Ui.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = new Font("Segoe UI", 9.5f);

        _url.Text = url;
        _intro.Text = message ?? L.T("Connexion au service EnshroudedManager.", "Connect to the EnshroudedManager service.");

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Width = 400 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        grid.Controls.Add(_intro, 0, 0);
        grid.SetColumnSpan(_intro, 2);
        grid.Controls.Add(new Label { Text = L.T("Adresse du service", "Service address"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(_url, 1, 1);
        grid.Controls.Add(new Label { Text = L.T("Mot de passe", "Password"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        grid.Controls.Add(_password, 1, 2);
        grid.Controls.Add(_confirmLabel, 0, 3);
        grid.Controls.Add(_confirm, 1, 3);
        grid.Controls.Add(_error, 0, 4);
        grid.SetColumnSpan(_error, 2);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(_ok);
        grid.Controls.Add(buttons, 0, 5);
        grid.SetColumnSpan(buttons, 2);
        Controls.Add(grid);

        AcceptButton = _ok;
        _ok.Click += async (_, _) => await SubmitAsync();
        _url.Leave += async (_, _) => await ProbeAsync();
        Shown += async (_, _) => { await ProbeAsync(); _password.Focus(); };
        SetSetup(false);
    }

    private void SetSetup(bool setup)
    {
        _setup = setup;
        _confirm.Visible = _confirmLabel.Visible = setup;
        _ok.Text = setup ? L.T("Définir le mot de passe", "Set the password") : L.T("Se connecter", "Sign in");
        if (setup) _intro.Text = L.T("Première connexion : choisis le mot de passe qui protégera l'outil (8 caractères minimum). Il servira aussi pour l'interface web.", "First sign-in: choose the password that will protect the tool (at least 8 characters). It is also used by the web interface.");
    }

    private async Task ProbeAsync()
    {
        try
        {
            var state = await new ApiClient(_url.Text, null).Get<AuthStateDto>("auth/state");
            SetSetup(!state.Configured);
            _error.Text = "";
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private async Task SubmitAsync()
    {
        _error.Text = "";
        if (_setup && _password.Text != _confirm.Text) { _error.Text = L.T("Les deux mots de passe sont différents.", "The two passwords do not match."); return; }
        _ok.Enabled = false;
        try
        {
            var client = new ApiClient(_url.Text, null);
            var r = await client.Post<TokenDto>(_setup ? "auth/setup" : "auth/login", new PasswordDto(_password.Text));
            client.Token = r.Token;
            Client = client;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) { _error.Text = ex.Message; }
        finally { _ok.Enabled = true; }
    }
}
