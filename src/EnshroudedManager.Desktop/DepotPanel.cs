using EnshroudedManager.Shared;

namespace EnshroudedManager.Desktop;

/// <summary>"Depot" tab: master world, reset schedule and actions of a resource depot instance.</summary>
public class DepotPanel : UserControl
{
    private readonly Label _master = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 12f) };
    private readonly Label _last = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 12f) };
    private readonly Label _next = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 12f) };
    private readonly Label _result = new() { AutoSize = true, ForeColor = Ui.Muted, MaximumSize = new Size(800, 0) };
    private readonly CheckBox _enabled = new() { Text = L.T("Reset automatique", "Automatic reset"), AutoSize = true };
    private readonly NumericUpDown _every = new() { Minimum = 5, Maximum = 1440, Width = 80 };
    private readonly CheckBox _onlyEmpty = new() { Text = L.T("Seulement si personne n'est connecté", "Only when nobody is online"), AutoSize = true };
    private readonly List<Button> _buttons = new();
    private ApiClient? _api;

    public event Action? Changed;

    public DepotPanel()
    {
        Padding = new Padding(12);
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Row(Control left, Control? right = null)
        {
            var r = grid.RowCount++;
            grid.Controls.Add(left, 0, r);
            if (right != null) grid.Controls.Add(right, 1, r);
            else grid.SetColumnSpan(left, 2);
        }
        Label Lbl(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 8) };
        Label Head(string t) => new() { Text = t, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 14, 0, 6) };

        Row(new Label
        {
            AutoSize = true, ForeColor = Ui.Muted, MaximumSize = new Size(800, 0),
            Text = L.T("Le monde est remis à l'identique du monde modèle à intervalle régulier : les visiteurs peuvent vider les coffres, tout revient au reset suivant.",
                       "The world is put back to the master world at regular intervals: visitors can empty the chests, everything comes back at the next reset."),
        });
        Row(Lbl(L.T("Monde modèle", "Master world")), _master);
        Row(Lbl(L.T("Dernier reset", "Last reset")), _last);
        Row(Lbl(L.T("Prochain reset", "Next reset")), _next);

        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        Button Action(string text, EventHandler click) { var b = Ui.Button(text, click); _buttons.Add(b); actions.Controls.Add(b); return b; }
        Action(L.T("Reset maintenant", "Reset now"), async (_, _) => await RunAsync("depot/reset",
            L.T("Remettre le monde modèle maintenant ? Les joueurs connectés seront déconnectés.", "Put the master world back now? Connected players will be disconnected.")));
        Action(L.T("Définir le monde actuel comme modèle", "Set the current world as master"), async (_, _) => await RunAsync("depot/master/from-current",
            L.T("Le monde actuel remplacera le monde modèle (le serveur redémarre). Continuer ?", "The current world will replace the master world (the server restarts). Continue?")));
        Action(L.T("Importer un monde (.zip)…", "Import a world (.zip)…"), async (_, _) => await ImportAsync());
        Row(actions);
        Row(_result);

        Row(Head(L.T("Réglages du dépôt", "Depot settings")));
        Row(_enabled);
        Row(Lbl(L.T("Toutes les (minutes)", "Every (minutes)")), _every);
        Row(_onlyEmpty);
        Row(Ui.Button(L.T("Enregistrer", "Save"), async (_, _) =>
        {
            if (_api == null) return;
            try
            {
                await _api.Put<DepotDto>("depot", new DepotSettingsDto(_enabled.Checked, (int)_every.Value, _onlyEmpty.Checked));
                Show(L.T("Réglages enregistrés.", "Settings saved."), Ui.Ok);
                await LoadAsync();
            }
            catch (Exception ex) { Show(ex.Message, Ui.Err); }
        }));
        Controls.Add(grid);
    }

    public void Attach(ApiClient api) => _api = api;

    private void Show(string text, Color color)
    {
        _result.Text = text;
        _result.ForeColor = color;
    }

    public async Task LoadAsync()
    {
        if (_api == null) return;
        try
        {
            var d = await _api.Get<DepotDto>("depot");
            _master.Text = d.HasMaster ? L.T("Présent", "Present") + " (" + Ui.FormatDate(d.MasterTime) + ")"
                                       : L.T("Aucun — importez un monde ou définissez le monde actuel comme modèle", "None — import a world or set the current world as master");
            _master.ForeColor = d.HasMaster ? SystemColors.ControlText : Ui.Warn;
            _last.Text = Ui.FormatDate(d.LastReset);
            _next.Text = d.Enabled ? d.NextReset?.ToLocalTime().ToString("HH:mm") ?? "—" : L.T("Reset automatique désactivé", "Automatic reset disabled");
            _enabled.Checked = d.Enabled;
            _every.Value = Math.Clamp(d.ResetEveryMinutes, 5, 1440);
            _onlyEmpty.Checked = d.OnlyWhenEmpty;
        }
        catch (Exception ex) { Show(ex.Message, Ui.Err); }
    }

    public void SetBusy(bool busy)
    {
        foreach (var b in _buttons) b.Enabled = !busy;
    }

    private async Task RunAsync(string path, string confirm)
    {
        if (_api == null || !Ui.Confirm(FindForm(), confirm, L.T("Dépôt", "Depot"))) return;
        Show(L.T("En cours…", "Working…"), Ui.Muted);
        try { Show((await _api.Post<MessageDto>(path)).Message, Ui.Ok); }
        catch (Exception ex) { Show(ex.Message, Ui.Err); }
        await LoadAsync();
        Changed?.Invoke();
    }

    private async Task ImportAsync()
    {
        if (_api == null) return;
        using var dlg = new OpenFileDialog { Filter = L.T("Archive zip|*.zip", "Zip archive|*.zip") };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (!Ui.Confirm(FindForm(), L.T($"Importer « {Path.GetFileName(dlg.FileName)} » comme monde modèle ? Le monde actuel du dépôt sera remplacé.",
                                         $"Import “{Path.GetFileName(dlg.FileName)}” as master world? The current depot world will be replaced."), L.T("Dépôt", "Depot"))) return;
        Show(L.T("Envoi et import en cours…", "Uploading and importing…"), Ui.Muted);
        try { Show((await _api.PostFile<MessageDto>("depot/master/import", dlg.FileName)).Message, Ui.Ok); }
        catch (Exception ex) { Show(ex.Message, Ui.Err); }
        await LoadAsync();
        Changed?.Invoke();
    }
}
