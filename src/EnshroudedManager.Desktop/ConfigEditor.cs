using System.Globalization;
using System.Text.Json.Nodes;
using EnshroudedManager.Shared;

namespace EnshroudedManager.Desktop;

/// <summary>Settings editor generated from the schema served by the service (same source as the web page).</summary>
public class ConfigEditor : UserControl
{
    private readonly TabControl _sections = new() { Dock = DockStyle.Fill };
    private readonly Panel _bar = new() { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8) };
    private readonly Label _barLabel = new() { AutoSize = true, Text = L.T("Aucune modification", "No changes"), ForeColor = Ui.Muted, Anchor = AnchorStyles.Left };
    private readonly Button _apply = new() { Text = L.T("Appliquer et redémarrer", "Apply and restart"), AutoSize = true, Enabled = false, Padding = new Padding(8, 3, 8, 3) };
    private readonly Button _cancel = new() { Text = L.T("Annuler", "Cancel"), AutoSize = true, Enabled = false, Padding = new Padding(8, 3, 8, 3) };
    private readonly Label _banner = new()
    {
        Dock = DockStyle.Top, Height = 30, Visible = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0),
        BackColor = Color.FromArgb(0xE3, 0xEE, 0xFF), Cursor = Cursors.Hand,
        Text = L.T("Les paramètres ont été modifiés ailleurs (interface web). Cliquer ici pour recharger.", "Settings were changed elsewhere (web interface). Click here to reload."),
    };
    private readonly ToolTip _tips = new() { AutoPopDelay = 15000 };

    private ApiClient? _api;
    private SchemaDto? _schema;
    private JsonObject _original = new();
    private JsonObject _current = new();
    private bool _building;

    public long Version { get; private set; } = -1;
    public bool Dirty { get; private set; }
    public event Action<string>? Applied;

    public ConfigEditor()
    {
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false };
        right.Controls.Add(_apply);
        right.Controls.Add(_cancel);
        _bar.Controls.Add(right);
        _bar.Controls.Add(_barLabel);
        _barLabel.Location = new Point(8, 14);
        Controls.Add(_sections);
        Controls.Add(_banner);
        Controls.Add(_bar);

        _apply.Click += async (_, _) => await ApplyAsync();
        _cancel.Click += (_, _) => { _current = (JsonObject)_original.DeepClone(); Build(); SetDirty(); };
        _banner.Click += async (_, _) => await LoadAsync();
    }

    public void Attach(ApiClient api) => _api = api;

    public async Task LoadAsync()
    {
        if (_api == null) return;
        _schema = await _api.Get<SchemaDto>("config/schema");
        var cfg = await _api.Get<ConfigDto>("config");
        _original = cfg.Values;
        _current = (JsonObject)_original.DeepClone();
        Version = cfg.Version;
        _banner.Visible = false;
        Build();
        SetDirty();
    }

    /// <summary>Called by the status poll: reloads silently, or warns when there are local edits.</summary>
    public async Task OnRemoteVersionAsync(long version)
    {
        if (version == Version || version == 0) return;
        if (Version < 0) { await LoadAsync(); return; } // the file appeared (first install finished)
        if (Dirty) _banner.Visible = true;
        else await LoadAsync();
    }

    private void SetDirty()
    {
        Dirty = _current.ToJsonString() != _original.ToJsonString();
        _apply.Enabled = _cancel.Enabled = Dirty;
        _barLabel.Text = Dirty ? L.T("Modifications non enregistrées", "Unsaved changes") : L.T("Aucune modification", "No changes");
        _barLabel.ForeColor = Dirty ? Ui.Accent : Ui.Muted;
    }

    private void Changed()
    {
        if (!_building) SetDirty();
    }

    private void Build()
    {
        if (_schema == null) return;
        _building = true;
        var selected = _sections.SelectedIndex;
        _sections.SuspendLayout();
        _sections.TabPages.Clear();

        foreach (var section in _schema.Fields.Select(f => f.Section).Distinct())
        {
            var grid = NewGrid();
            foreach (var f in _schema.Fields.Where(f => f.Section == section))
                AddRow(grid, f, () => _current[f.Key], v => _current[f.Key] = v);
            _sections.TabPages.Add(Page(section, grid));
        }
        _sections.TabPages.Add(BuildGroupsPage());

        if (selected >= 0 && selected < _sections.TabCount) _sections.SelectedIndex = selected;
        _sections.ResumeLayout();
        _building = false;
    }

    private static TableLayoutPanel NewGrid()
    {
        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { AutoScroll = true, BackColor = SystemColors.Window };
        page.Controls.Add(content);
        return page;
    }

    private TabPage BuildGroupsPage()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10), WrapContents = true };
        if (_current["userGroups"] is not JsonArray groups) _current["userGroups"] = groups = new JsonArray();

        foreach (var node in groups.ToArray())
        {
            if (node is not JsonObject g) continue;
            var box = new GroupBox { Text = g["name"]?.ToString() ?? L.T("Nouveau groupe", "New group"), AutoSize = true, Margin = new Padding(0, 0, 12, 12), Padding = new Padding(8) };
            var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (var f in _schema!.GroupFields)
                AddRow(grid, f, () => g[f.Key], v =>
                {
                    g[f.Key] = v;
                    if (f.Key == "name") box.Text = v?.ToString() is { Length: > 0 } n ? n : L.T("Nouveau groupe", "New group");
                }, showHint: false);
            var remove = Ui.Button(L.T("Supprimer ce groupe", "Delete this group"), (_, _) =>
            {
                groups.Remove(g);
                Rebuild();
            });
            grid.Controls.Add(remove);
            grid.SetColumnSpan(remove, 3);
            box.Controls.Add(grid);
            flow.Controls.Add(box);
        }

        var add = Ui.Button(L.T("Ajouter un groupe", "Add a group"), (_, _) =>
        {
            groups.Add(new JsonObject
            {
                ["name"] = "", ["password"] = "", ["canKickBan"] = false, ["canAccessInventories"] = false,
                ["canEditWorld"] = true, ["canEditBase"] = false, ["canExtendBase"] = false, ["reservedSlots"] = 0,
            });
            Rebuild();
        });
        flow.Controls.Add(add);
        flow.SetFlowBreak(add, true);

        var info = new Label
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 10, 10, 0), ForeColor = Ui.Muted, MaximumSize = new Size(800, 0),
            Text = L.T("Chaque groupe a son mot de passe : le joueur obtient les droits du groupe dont il saisit le mot de passe en rejoignant le serveur.", "Each group has its own password: players get the rights of the group whose password they enter when joining the server."),
        };
        var page = new TabPage(L.T("Groupes de joueurs", "Player groups")) { AutoScroll = true, BackColor = SystemColors.Window };
        page.Controls.Add(flow);
        page.Controls.Add(info);
        return page;
    }

    private void Rebuild()
    {
        Build();
        _sections.SelectedIndex = _sections.TabCount - 1;
        SetDirty();
    }

    private void AddRow(TableLayoutPanel grid, FieldDto f, Func<JsonNode?> get, Action<JsonNode?> set, bool showHint = true)
    {
        var label = new Label { Text = f.Label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 8) };
        var hint = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Ui.Muted, MaximumSize = new Size(520, 0) };
        if (f.Help != null) _tips.SetToolTip(label, f.Help);

        void UpdateHint(double? value)
        {
            if (!showHint) return;
            var text = f.Help ?? "";
            var outOfRange = false;
            if (f.Min is { } min && f.Max is { } max && f.Key != "queryPort")
            {
                text = (text == "" ? "" : text + " · ") + L.T($"Plage officielle : {min.ToString(CultureInfo.CurrentCulture)} – {max.ToString(CultureInfo.CurrentCulture)}", $"Official range: {min.ToString(CultureInfo.CurrentCulture)} – {max.ToString(CultureInfo.CurrentCulture)}");
                outOfRange = value is { } v && (v < min || v > max);
                if (outOfRange) text += L.T(" — hors plage (accepté par le jeu, mais non garanti)", " — out of range (accepted by the game, but not guaranteed)");
            }
            hint.Text = text;
            hint.ForeColor = outOfRange ? Ui.Warn : Ui.Muted;
        }

        Control control;
        var value = get();
        switch (f.Type)
        {
            case "bool":
            {
                var cb = new CheckBox { Checked = value?.GetValue<bool>() ?? false, AutoSize = true, Anchor = AnchorStyles.Left };
                cb.CheckedChanged += (_, _) => { set(cb.Checked); Changed(); };
                control = cb;
                break;
            }
            case "enum":
            {
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Anchor = AnchorStyles.Left };
                combo.Items.AddRange(f.Options!.Cast<object>().ToArray());
                combo.SelectedItem = value?.ToString();
                combo.SelectedIndexChanged += (_, _) => { set(combo.SelectedItem?.ToString()); Changed(); };
                control = combo;
                break;
            }
            case "tags":
            {
                var list = new CheckedListBox { CheckOnClick = true, MultiColumn = true, ColumnWidth = 140, Width = 580, Height = 110, Anchor = AnchorStyles.Left };
                var selectedTags = (value as JsonArray)?.Select(t => t?.ToString()).ToHashSet() ?? new HashSet<string?>();
                foreach (var o in f.Options!) list.Items.Add(o, selectedTags.Contains(o));
                list.ItemCheck += (_, e) => BeginInvoke(() =>
                {
                    set(new JsonArray(list.CheckedItems.Cast<string>().Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()));
                    Changed();
                });
                control = list;
                break;
            }
            case "int":
            case "float":
            case "duration":
            {
                var decimals = f.Type == "int" ? 0 : f.Type == "duration" ? 1 : 2;
                var current = value == null ? 0 : value.GetValue<double>();
                // Keep 6 decimals when the file holds a finer value, so loading never rounds it.
                if (Math.Round(current, decimals) != current) decimals = 6;
                var num = new NumericUpDown
                {
                    DecimalPlaces = decimals, Minimum = 0, Maximum = f.Key == "queryPort" ? 65535 : 1_000_000,
                    Increment = (decimal)(f.Step ?? 1), Width = 110, Anchor = AnchorStyles.Left,
                    Value = (decimal)current,
                };
                num.ValueChanged += (_, _) =>
                {
                    var v = (double)num.Value;
                    set(f.Type == "int" ? JsonValue.Create((long)v) : JsonValue.Create(v));
                    UpdateHint(v);
                    Changed();
                };
                UpdateHint(current);
                if (f.Unit != null)
                {
                    var wrap = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
                    wrap.Controls.Add(num);
                    wrap.Controls.Add(new Label { Text = f.Unit, AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(4, 6, 0, 0) });
                    control = wrap;
                }
                else control = num;
                break;
            }
            case "password":
            {
                var wrap = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
                var tb = new TextBox { Text = value?.ToString() ?? "", UseSystemPasswordChar = true, Width = 150 };
                var show = new CheckBox { Text = L.T("Voir", "Show"), AutoSize = true, Margin = new Padding(6, 5, 0, 0) };
                tb.TextChanged += (_, _) => { set(tb.Text); Changed(); };
                show.CheckedChanged += (_, _) => tb.UseSystemPasswordChar = !show.Checked;
                wrap.Controls.Add(tb);
                wrap.Controls.Add(show);
                control = wrap;
                break;
            }
            default:
            {
                var tb = new TextBox { Text = value?.ToString() ?? "", Width = f.Section == "Groupe" ? 150 : 300, Anchor = AnchorStyles.Left };
                tb.TextChanged += (_, _) => { set(tb.Text); Changed(); };
                control = tb;
                break;
            }
        }
        if (f.Type is not ("int" or "float" or "duration")) UpdateHint(null);

        var row = grid.RowCount++;
        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(control, 1, row);
        if (showHint) grid.Controls.Add(hint, 2, row);
    }

    private async Task ApplyAsync()
    {
        if (_api == null) return;
        try
        {
            var check = await _api.Post<ApplyResultDto>("config/validate", _current);
            if (!check.Ok)
            {
                Ui.Error(FindForm(), L.T("Paramètres invalides :\n\n• ", "Invalid settings:\n\n• ") + string.Join("\n• ", check.Errors));
                return;
            }
            var msg = L.T("Le serveur va être arrêté, le monde et l'ancienne configuration sauvegardés, puis le serveur relancé avec les nouveaux paramètres.\nLes joueurs connectés seront déconnectés.", "The server will be stopped, the world and the previous settings backed up, then the server restarted with the new settings.\nConnected players will be disconnected.");
            if (check.Warnings.Length > 0) msg += L.T("\n\nAvertissements :\n• ", "\n\nWarnings:\n• ") + string.Join("\n• ", check.Warnings);
            if (!Ui.Confirm(FindForm(), msg, L.T("Appliquer les paramètres ?", "Apply the settings?"))) return;

            _apply.Enabled = false;
            _barLabel.Text = L.T("Application en cours (arrêt, sauvegarde, redémarrage)…", "Applying (stop, backup, restart)…");
            var r = await _api.Put<ApplyResultDto>("config", _current);
            if (!r.Ok)
            {
                Ui.Error(FindForm(), L.T("Échec :\n\n• ", "Failed:\n\n• ") + string.Join("\n• ", r.Errors));
                SetDirty();
                return;
            }
            await LoadAsync();
            Applied?.Invoke(L.T("Paramètres appliqués, serveur redémarré.", "Settings applied, server restarted."));
        }
        catch (Exception ex)
        {
            Ui.Error(FindForm(), ex.Message);
            SetDirty();
        }
    }
}
