using EnshroudedManager.Shared;

namespace EnshroudedManager.Desktop;

public class MainForm : Form
{
    private readonly DesktopSettings _settings;
    private ApiClient _api;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly NotifyIcon _tray = new() { Text = "Enshrouded Manager", Visible = true };
    private bool _exiting;
    private bool _polling;
    private long _lastLogId = -1;
    private StatusDto? _status;

    // header
    private readonly Label _name = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 14f), Margin = new Padding(0, 0, 12, 0) };
    private readonly Label _state = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0, 4, 12, 0) };
    private readonly Label _busy = new() { AutoSize = true, ForeColor = Ui.Warn, Margin = new Padding(0, 6, 0, 0) };
    private readonly ComboBox _instance = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Visible = false, Margin = new Padding(0, 4, 12, 0) };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
    private readonly DepotPanel _depot = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly TabPage _depotPage = new(L.T("Dépôt", "Depot"));
    private InstanceDto[] _instances = [];
    private bool _fillingInstances;

    // dashboard
    private readonly Label _sState = Value(), _sPlayers = Value(), _sLocal = Value(), _sAuto = Value();
    private readonly Label _sUptime = Sub(), _sMem = Sub(), _sRemote = Sub(), _sCheck = Sub();
    private readonly Label _actionResult = new() { AutoSize = true, ForeColor = Ui.Muted, MaximumSize = new Size(900, 0) };
    private readonly RichTextBox _recentLog = LogBox();
    private readonly List<Button> _actionButtons = new();

    private readonly ConfigEditor _config = new() { Dock = DockStyle.Fill };
    private readonly ListView _backups = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private readonly RichTextBox _fullLog = LogBox();

    // tool settings
    private readonly CheckBox _setAuto = new() { Text = L.T("Mise à jour automatique", "Automatic updates"), AutoSize = true };
    private readonly NumericUpDown _setInterval = new() { Minimum = 1, Maximum = 1440, Width = 80 };
    private readonly CheckBox _setRestart = new() { Text = L.T("Relancer le serveur s'il plante", "Restart the server if it crashes"), AutoSize = true };
    private readonly NumericUpDown _setKeep = new() { Minimum = 3, Maximum = 500, Width = 80 };
    private readonly ComboBox _setLanguage = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Items = { "Français", "English" } };
    private readonly ProgressBar _progress = new() { Width = 220, Height = 14, Visible = false, Margin = new Padding(12, 8, 0, 0) };
    private readonly CheckBox _setNotify = new() { Text = L.T("Notifications Windows (mises à jour, plantages)", "Windows notifications (updates, crashes)"), AutoSize = true };

    public MainForm(DesktopSettings settings, ApiClient api)
    {
        _settings = settings;
        _api = api;
        Text = "Enshrouded Manager";
        Icon = Ui.AppIcon;
        _tray.Icon = Ui.TrayIcon;
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(1100, 760);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(14, 10, 14, 8), WrapContents = false };
        header.Controls.AddRange([_name, _instance, _state, _busy, _progress]);

        var tabs = _tabs;
        _depotPage.Controls.Add(_depot);
        tabs.TabPages.Add(BuildDashboard());
        var configPage = new TabPage(L.T("Paramètres", "Settings"));
        configPage.Controls.Add(_config);
        tabs.TabPages.Add(configPage);
        tabs.TabPages.Add(BuildBackups());
        var logPage = new TabPage(L.T("Journal", "Log")) { Padding = new Padding(8) };
        logPage.Controls.Add(_fullLog);
        tabs.TabPages.Add(logPage);
        tabs.TabPages.Add(BuildTool());
        tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (tabs.SelectedTab?.Text == L.T("Sauvegardes", "Backups")) await LoadBackupsAsync();
            if (tabs.SelectedTab?.Text == L.T("Outil", "Tool")) await LoadToolSettingsAsync();
            if (tabs.SelectedTab == _depotPage) await _depot.LoadAsync();
        };
        Controls.Add(tabs);
        Controls.Add(header);

        _config.Attach(_api);
        _config.Applied += m => { _actionResult.Text = m; _ = PollAsync(); };
        _depot.Attach(_api);
        _depot.Changed += () => _ = PollAsync();
        _instance.SelectedIndexChanged += async (_, _) => await SelectInstanceAsync();
        _api.Instance = _settings.Instance;

        var menu = new ContextMenuStrip();
        menu.Items.Add(L.T("Ouvrir", "Open"), null, (_, _) => ShowWindow());
        menu.Items.Add(L.T("Redémarrer le serveur", "Restart the server"), null, async (_, _) => await RunActionAsync("server/restart", L.T("Redémarrer le serveur ? Les joueurs connectés seront déconnectés.", "Restart the server? Connected players will be disconnected.")));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.T("Quitter", "Exit"), null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowWindow();
        _tray.BalloonTipClicked += (_, _) => ShowWindow();

        _timer.Tick += async (_, _) => await PollAsync();
        Shown += async (_, _) =>
        {
            await PollAsync();
            try { await _config.LoadAsync(); } catch (Exception ex) { _actionResult.Text = ex.Message; }
            _timer.Start();
        };
        FormClosing += (_, e) =>
        {
            // Closing the window only hides it (unsaved edits are kept); "Quitter" in the tray menu exits.
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _tray.ShowBalloonTip(3000, "Enshrouded Manager", L.T("L'application reste dans la zone de notification.", "The app keeps running in the notification area."), ToolTipIcon.Info);
                return;
            }
            _timer.Stop();
            _tray.Visible = false;
        };
    }

    private static Label Value() => new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 16f), Margin = new Padding(0, 2, 0, 2) };
    private static Label Sub() => new() { AutoSize = true, ForeColor = Ui.Muted, MaximumSize = new Size(240, 0) };
    private static RichTextBox LogBox() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Window, BorderStyle = BorderStyle.None,
        Font = new Font("Consolas", 9f), WordWrap = true, DetectUrls = false,
    };

    private void ShowWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private TabPage BuildDashboard()
    {
        var page = new TabPage(L.T("Tableau de bord", "Dashboard")) { Padding = new Padding(12) };
        var stats = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
        for (var i = 0; i < 4; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        void Stat(int col, string title, Label value, Label sub)
        {
            var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Height = 105, Margin = new Padding(0, 0, 10, 10) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            flow.Controls.AddRange([value, sub]);
            box.Controls.Add(flow);
            stats.Controls.Add(box, col, 0);
        }
        Stat(0, L.T("État", "Status"), _sState, _sUptime);
        Stat(1, L.T("Joueurs", "Players"), _sPlayers, _sMem);
        Stat(2, L.T("Version installée", "Installed version"), _sLocal, _sRemote);
        Stat(3, L.T("Mises à jour", "Updates"), _sAuto, _sCheck);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        void Action(string text, string path, string? confirm = null)
        {
            var b = Ui.Button(text, async (_, _) => await RunActionAsync(path, confirm));
            _actionButtons.Add(b);
            actions.Controls.Add(b);
        }
        Action(L.T("Démarrer", "Start"), "server/start");
        Action(L.T("Arrêter", "Stop"), "server/stop", L.T("Arrêter le serveur ? Les joueurs connectés seront déconnectés.", "Stop the server? Connected players will be disconnected."));
        Action(L.T("Redémarrer", "Restart"), "server/restart", L.T("Redémarrer le serveur ? Les joueurs connectés seront déconnectés.", "Restart the server? Connected players will be disconnected."));
        Action(L.T("Vérifier les mises à jour", "Check for updates"), "update/check");
        Action(L.T("Mettre à jour maintenant", "Update now"), "update/run", L.T("Forcer la mise à jour maintenant ?\nLe serveur sera arrêté, sauvegardé, mis à jour (validation des fichiers) puis relancé.", "Force an update now?\nThe server will be stopped, backed up, updated (file validation) and restarted."));
        Action(L.T("Sauvegarder maintenant", "Back up now"), "backups");

        var resultPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(2, 4, 0, 0) };
        resultPanel.Controls.Add(_actionResult);
        var logBox = new GroupBox { Text = L.T("Derniers événements", "Recent events"), Dock = DockStyle.Fill, Padding = new Padding(8) };
        logBox.Controls.Add(_recentLog);

        page.Controls.Add(logBox);
        page.Controls.Add(resultPanel);
        page.Controls.Add(actions);
        page.Controls.Add(stats);
        return page;
    }

    private TabPage BuildBackups()
    {
        var page = new TabPage(L.T("Sauvegardes", "Backups")) { Padding = new Padding(12) };
        _backups.Columns.Add(L.T("Date", "Date"), 170);
        _backups.Columns.Add(L.T("Motif", "Reason"), 260);
        _backups.Columns.Add(L.T("Taille", "Size"), 100, HorizontalAlignment.Right);
        _backups.Columns.Add(L.T("Fichier", "File"), 330);

        var info = new Label
        {
            Dock = DockStyle.Top, AutoSize = true, ForeColor = Ui.Muted, MaximumSize = new Size(900, 0), Padding = new Padding(0, 0, 0, 8),
            Text = L.T("Une sauvegarde est faite automatiquement avant chaque mise à jour, chaque modification des paramètres et chaque restauration. Chaque archive contient le dossier savegame et enshrouded_server.json.", "A backup is made automatically before every update, every settings change and every restore. Each archive holds the savegame folder and enshrouded_server.json."),
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var backupNow = Ui.Button(L.T("Sauvegarder maintenant", "Back up now"), async (_, _) => { await RunActionAsync("backups"); await LoadBackupsAsync(); });
        _actionButtons.Add(backupNow);
        bar.Controls.Add(backupNow);
        bar.Controls.Add(Ui.Button(L.T("Restaurer…", "Restore…"), async (_, _) => await RestoreAsync()));
        bar.Controls.Add(Ui.Button(L.T("Télécharger…", "Download…"), async (_, _) => await DownloadAsync()));
        bar.Controls.Add(Ui.Button(L.T("Actualiser", "Refresh"), async (_, _) => await LoadBackupsAsync()));

        page.Controls.Add(_backups);
        page.Controls.Add(bar);
        page.Controls.Add(info);
        return page;
    }

    private TabPage BuildTool()
    {
        var page = new TabPage(L.T("Outil", "Tool")) { Padding = new Padding(12), AutoScroll = true };
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Row(Control left, Control? right = null)
        {
            var r = grid.RowCount++;
            grid.Controls.Add(left, 0, r);
            if (right != null) grid.Controls.Add(right, 1, r);
            else grid.SetColumnSpan(left, 2);
        }
        Label Lbl(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 8) };
        Label Head(string t) => new() { Text = t, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 16, 0, 6) };

        Row(Head(L.T("Réglages du service", "Service settings")));
        Row(_setAuto);
        Row(Lbl(L.T("Vérifier toutes les (minutes)", "Check every (minutes)")), _setInterval);
        Row(_setRestart);
        Row(Lbl(L.T("Nombre de sauvegardes à garder", "Backups to keep")), _setKeep);
        Row(Lbl(L.T("Langue", "Language")), _setLanguage);
        var toolResult = new Label { AutoSize = true, ForeColor = Ui.Muted, MaximumSize = new Size(520, 0) };
        Row(Ui.Button(L.T("Enregistrer", "Save"), async (_, _) =>
        {
            try
            {
                var language = _setLanguage.SelectedIndex == 1 ? "en" : "fr";
                var r = await _api.Put<MessageDto>("settings", new ManagerSettingsDto((int)_setInterval.Value, _setAuto.Checked, _setRestart.Checked, (int)_setKeep.Value, language));
                toolResult.ForeColor = Ui.Ok;
                toolResult.Text = r.Message;
                if (language != L.Language)
                    toolResult.Text += L.T(" La nouvelle langue s'appliquera au prochain lancement de l'application.", " The new language applies the next time the app starts.");
            }
            catch (Exception ex) { toolResult.ForeColor = Ui.Err; toolResult.Text = ex.Message; }
        }), toolResult);

        Row(Head(L.T("Application bureau", "Desktop app")));
        _setNotify.Checked = _settings.NotifyEvents;
        _setNotify.CheckedChanged += (_, _) => { _settings.NotifyEvents = _setNotify.Checked; _settings.Save(); };
        Row(_setNotify);
        Row(Lbl(L.T("Connecté à", "Connected to")), new Label { Text = _api.BaseUrl, AutoSize = true, Anchor = AnchorStyles.Left });

        Row(Head(L.T("Mot de passe de l'outil", "Tool password")));
        var current = new TextBox { UseSystemPasswordChar = true, Width = 220 };
        var next = new TextBox { UseSystemPasswordChar = true, Width = 220 };
        Row(Lbl(L.T("Mot de passe actuel", "Current password")), current);
        Row(Lbl(L.T("Nouveau mot de passe", "New password")), next);
        var pwResult = new Label { AutoSize = true, ForeColor = Ui.Muted, MaximumSize = new Size(520, 0) };
        Row(Ui.Button(L.T("Changer le mot de passe", "Change password"), async (_, _) =>
        {
            try
            {
                var r = await _api.Post<TokenDto>("auth/password", new ChangePasswordDto(current.Text, next.Text));
                _api.Token = r.Token;
                _settings.Token = r.Token;
                _settings.Save();
                current.Clear();
                next.Clear();
                pwResult.ForeColor = Ui.Ok;
                pwResult.Text = L.T("Mot de passe changé. Les autres sessions sont déconnectées.", "Password changed. Other sessions have been signed out.");
            }
            catch (Exception ex) { pwResult.ForeColor = Ui.Err; pwResult.Text = ex.Message; }
        }), pwResult);
        Row(Ui.Button(L.T("Se déconnecter", "Sign out"), async (_, _) =>
        {
            try { await _api.Post<object>("auth/logout"); } catch { }
            _settings.Token = null;
            _settings.Save();
            await ReconnectAsync(L.T("Déconnecté.", "Signed out."));
        }));

        page.Controls.Add(grid);
        return page;
    }

    private async Task LoadToolSettingsAsync()
    {
        try
        {
            var s = await _api.Get<ManagerSettingsDto>("settings");
            _setAuto.Checked = s.AutoUpdate;
            _setInterval.Value = Math.Clamp(s.CheckIntervalMinutes, 1, 1440);
            _setRestart.Checked = s.AutoRestart;
            _setKeep.Value = Math.Clamp(s.BackupsToKeep, 3, 500);
            _setLanguage.SelectedIndex = s.Language == "en" ? 1 : 0;
        }
        catch (Exception ex) { _actionResult.Text = ex.Message; }
    }

    private async Task RefreshInstancesAsync()
    {
        _instances = await _api.Get<InstanceDto[]>("instances");
        if (!_instances.Any(i => i.Id == _api.Instance)) _api.Instance = "main";
        _instance.Visible = _instances.Length > 1;
        var names = _instances.Select(i => i.Name).ToArray();
        if (!_instance.Items.Cast<string>().SequenceEqual(names))
        {
            _fillingInstances = true;
            _instance.Items.Clear();
            _instance.Items.AddRange(names);
            _fillingInstances = false;
        }
        var index = Array.FindIndex(_instances, i => i.Id == _api.Instance);
        if (_instance.SelectedIndex != index) { _fillingInstances = true; _instance.SelectedIndex = index; _fillingInstances = false; }
    }

    /// <summary>Every tab follows the instance picked in the header.</summary>
    private async Task SelectInstanceAsync()
    {
        if (_fillingInstances || _instance.SelectedIndex < 0) return;
        var id = _instances[_instance.SelectedIndex].Id;
        if (id == _api.Instance) return;
        if (_config.Dirty && !Ui.Confirm(this, L.T("Modifications non enregistrées : les abandonner ?", "Unsaved changes: discard them?")))
        {
            _fillingInstances = true;
            _instance.SelectedIndex = Array.FindIndex(_instances, i => i.Id == _api.Instance);
            _fillingInstances = false;
            return;
        }
        _api.Instance = id;
        _settings.Instance = id;
        _settings.Save();
        try { await _config.LoadAsync(); } catch (Exception ex) { _actionResult.Text = ex.Message; }
        if (_tabs.SelectedTab?.Text == L.T("Sauvegardes", "Backups")) await LoadBackupsAsync();
        await PollAsync();
        if (_tabs.SelectedTab == _depotPage) await _depot.LoadAsync();
    }

    private void ShowDepotTab(bool show)
    {
        var shown = _tabs.TabPages.Contains(_depotPage);
        if (show && !shown) _tabs.TabPages.Insert(2, _depotPage);
        if (!show && shown) _tabs.TabPages.Remove(_depotPage);
    }

    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            await RefreshInstancesAsync();
            _status = await _api.Get<StatusDto>("status");
            ShowStatus(_status);
            ShowDepotTab(_status.IsDepot);
            _depot.SetBusy(_status.Busy != null);
            if (_status.LastLogId != _lastLogId) await LoadLogsAsync();
            await _config.OnRemoteVersionAsync(_status.ConfigVersion);
        }
        catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            await ReconnectAsync(ex.Message);
        }
        catch (Exception ex)
        {
            _state.Text = L.T("Service injoignable", "Service unreachable");
            _state.ForeColor = Ui.Err;
            _busy.Text = ex.Message;
        }
        finally { _polling = false; }
    }

    private void ShowStatus(StatusDto s)
    {
        _name.Text = s.ServerName;
        Text = $"{s.ServerName} — Enshrouded Manager";
        var tip = $"{s.ServerName} : {s.StateLabel}";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip; // NotifyIcon limit
        _state.Text = s.StateLabel;
        _state.ForeColor = Ui.StateColor(s.State);
        _busy.Text = s.Busy is null ? "" : "⏳ " + s.Busy;
        _progress.Visible = s.Progress is not null;
        if (s.Progress is { } pct) _progress.Value = (int)Math.Clamp(pct, 0, 100);
        foreach (var b in _actionButtons) b.Enabled = s.Busy is null;

        _sState.Text = s.StateLabel;
        _sState.ForeColor = Ui.StateColor(s.State);
        _sUptime.Text = s.StartedAt is { } st ? L.T($"Depuis {Ui.FormatDuration(DateTimeOffset.Now - st)}", $"For {Ui.FormatDuration(DateTimeOffset.Now - st)}") + (s.Pid is { } pid ? $" · PID {pid}" : "") : "";
        _sPlayers.Text = s.Players is { } p ? $"{p.Current} / {p.Max}" : "—";
        _sMem.Text = s.MemoryMb is { } mb ? L.T($"Mémoire : {mb / 1024.0:0.0} Go", $"Memory: {mb / 1024.0:0.0} GB") : "";
        _sLocal.Text = s.LocalBuild ?? "?";
        _sLocal.ForeColor = s.UpdateAvailable ? Ui.Warn : SystemColors.ControlText;
        _sRemote.Text = s.RemoteBuild == null ? L.T("Version Steam pas encore vérifiée", "Steam version not checked yet")
            : s.UpdateAvailable ? L.T($"Nouvelle version disponible : {s.RemoteBuild}", $"New version available: {s.RemoteBuild}") : L.T("À jour avec Steam", "Up to date with Steam");
        _sAuto.Text = s.AutoUpdate ? L.T("Automatiques", "Automatic") : L.T("Manuelles", "Manual");
        _sCheck.Text = L.T("Vérifié : ", "Checked: ") + Ui.FormatDate(s.LastCheck) + L.T("\nProchaine : ", "\nNext: ") + (s.NextCheck?.ToLocalTime().ToString("HH:mm") ?? "—")
                       + (s.LastUpdate is { } lu ? L.T($"\nDernière MAJ : {Ui.FormatDate(lu)}", $"\nLast update: {Ui.FormatDate(lu)}") : "");
    }

    private async Task LoadLogsAsync()
    {
        var first = _lastLogId < 0;
        var entries = await _api.Get<LogEntryDto[]>("logs?after=" + Math.Max(0, _lastLogId));
        if (entries.Length == 0) { _lastLogId = Math.Max(0, _lastLogId); return; }
        _lastLogId = entries[^1].Id;
        foreach (var e in entries)
        {
            Append(_fullLog, e);
            Append(_recentLog, e);
            // Notify only for what happens while the app is open, not for the history.
            if (!first && _settings.NotifyEvents && e.Level is "event" or "error")
                _tray.ShowBalloonTip(5000, e.Level == "error" ? L.T("Problème sur le serveur", "Server problem") : "Enshrouded", e.Message,
                    e.Level == "error" ? ToolTipIcon.Error : ToolTipIcon.Info);
        }
        Trim(_fullLog, 3000);
        Trim(_recentLog, 60);
    }

    private static void Append(RichTextBox box, LogEntryDto e)
    {
        box.SelectionStart = box.TextLength;
        box.SelectionColor = Ui.Muted;
        box.AppendText(e.Time.ToLocalTime().ToString("dd/MM HH:mm:ss") + "  ");
        box.SelectionColor = Ui.LevelColor(e.Level);
        box.AppendText(e.Message + Environment.NewLine);
        box.ScrollToCaret();
    }

    private static void Trim(RichTextBox box, int maxLines)
    {
        if (box.Lines.Length <= maxLines) return;
        box.ReadOnly = false;
        box.Select(0, box.GetFirstCharIndexFromLine(box.Lines.Length - maxLines));
        box.SelectedText = "";
        box.ReadOnly = true;
        box.SelectionStart = box.TextLength;
        box.ScrollToCaret();
    }

    private async Task RunActionAsync(string path, string? confirm = null)
    {
        if (confirm != null && !Ui.Confirm(this, confirm)) return;
        _actionResult.ForeColor = Ui.Muted;
        _actionResult.Text = L.T("En cours…", "Working…");
        foreach (var b in _actionButtons) b.Enabled = false;
        _ = Task.Delay(300).ContinueWith(_ => BeginInvoke(() => _ = PollAsync()));
        try
        {
            var r = await _api.Post<MessageDto>(path);
            _actionResult.ForeColor = Ui.Ok;
            _actionResult.Text = r.Message;
        }
        catch (Exception ex)
        {
            _actionResult.ForeColor = Ui.Err;
            _actionResult.Text = ex.Message;
        }
        await PollAsync();
    }

    private async Task LoadBackupsAsync()
    {
        try
        {
            var list = await _api.Get<BackupDto[]>("backups");
            var reasons = new Dictionary<string, string>
            {
                ["avant-maj"] = L.T("Avant mise à jour", "Before update"), ["avant-config"] = L.T("Avant changement de paramètres", "Before settings change"),
                ["avant-restauration"] = L.T("Avant restauration", "Before restore"), ["manuelle"] = L.T("Manuelle", "Manual"),
            };
            _backups.BeginUpdate();
            _backups.Items.Clear();
            foreach (var b in list)
                _backups.Items.Add(new ListViewItem([
                    Ui.FormatDate(b.Time), reasons.GetValueOrDefault(b.Reason, b.Reason), L.T($"{b.SizeBytes / 1048576.0:0.0} Mo", $"{b.SizeBytes / 1048576.0:0.0} MB"), b.Name,
                ]) { Tag = b });
            _backups.EndUpdate();
        }
        catch (Exception ex) { Ui.Error(this, ex.Message); }
    }

    private BackupDto? SelectedBackup()
    {
        if (_backups.SelectedItems.Count == 0 || _backups.SelectedItems[0].Tag is not BackupDto b)
        {
            MessageBox.Show(this, L.T("Sélectionne une sauvegarde dans la liste.", "Select a backup in the list."), L.T("Sauvegardes", "Backups"));
            return null;
        }
        return b;
    }

    private async Task RestoreAsync()
    {
        if (SelectedBackup() is not { } b) return;
        var answer = MessageBox.Show(this,
            L.T($"Restaurer la sauvegarde du {Ui.FormatDate(b.Time)} ?\n\nLe monde actuel sera remplacé (il est sauvegardé juste avant, au cas où).\n\n", $"Restore the backup from {Ui.FormatDate(b.Time)}?\n\nThe current world will be replaced (it is backed up first, just in case).\n\n") +
            L.T("Restaurer aussi les paramètres (enshrouded_server.json) ?\nOui = monde + paramètres · Non = monde uniquement", "Also restore the settings (enshrouded_server.json)?\nYes = world + settings · No = world only"),
            L.T("Restaurer", "Restore"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (answer == DialogResult.Cancel) return;
        try
        {
            var r = await _api.Post<MessageDto>($"backups/{Uri.EscapeDataString(b.Name)}/restore?config={(answer == DialogResult.Yes).ToString().ToLowerInvariant()}");
            MessageBox.Show(this, r.Message, L.T("Restauration", "Restore"));
        }
        catch (Exception ex) { Ui.Error(this, ex.Message); }
        await LoadBackupsAsync();
    }

    private async Task DownloadAsync()
    {
        if (SelectedBackup() is not { } b) return;
        using var dlg = new SaveFileDialog { FileName = b.Name, Filter = L.T("Archive zip|*.zip", "Zip archive|*.zip") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { await File.WriteAllBytesAsync(dlg.FileName, await _api.GetBytes("backups/" + Uri.EscapeDataString(b.Name))); }
        catch (Exception ex) { Ui.Error(this, ex.Message); }
    }

    private async Task ReconnectAsync(string message)
    {
        _timer.Stop();
        ShowWindow();
        using var login = new LoginForm(_settings.ServerUrl, message);
        if (login.ShowDialog(this) != DialogResult.OK || login.Client == null)
        {
            _exiting = true;
            Close();
            return;
        }
        _api = login.Client;
        _settings.ServerUrl = _api.BaseUrl;
        _settings.Token = _api.Token;
        _settings.Save();
        _api.Instance = _settings.Instance;
        _config.Attach(_api);
        _depot.Attach(_api);
        await _config.LoadAsync();
        _timer.Start();
    }
}
