using System.Globalization;
using Microsoft.Extensions.Logging;
using PresenceTracker.Application;
using PresenceTracker.Domain;
using PresenceTracker.Persistence;
using PresenceTracker.Infrastructure;

namespace PresenceTracker;

internal sealed class SettingsForm : Form
{
    private readonly TrackerService tracker;
    private readonly ILogger logger;
    private readonly Func<IReadOnlyCollection<string>, Task<int>> recordNewlyConfiguredNetworks;
    private readonly List<PresenceNetwork> networks;
    private readonly TrackerSettings settings;
    private readonly HashSet<string> savedQualifyingNetworks;
    private readonly FlowLayoutPanel networkRows = new();
    private readonly System.Windows.Forms.Timer saveTimer = new();
    private readonly Label saveStatus = new();
    private readonly NumericUpDown target = new();
    private readonly NumericUpDown retention = new();
    private readonly CheckBox startup = new();
    private readonly CheckBox minimize = new();
    private readonly ComboBox theme = new();
    private readonly ComboBox logLevel = new();
    private readonly Dictionary<DayOfWeek, CheckBox> weekdays = [];
    private bool initializing = true;
    private bool saveInProgress;
    private bool savePending;

    public SettingsForm(TrackerService tracker, TrackerSettings settings,
        IReadOnlyList<PresenceNetwork> networks, IDatabaseBackupService backup, ILogger logger,
        Func<IReadOnlyCollection<string>, Task<int>> recordNewlyConfiguredNetworks)
    {
        this.tracker = tracker;
        this.recordNewlyConfiguredNetworks = recordNewlyConfiguredNetworks;
        this.settings = new TrackerSettings
        {
            Id = settings.Id, TargetPercent = settings.TargetPercent, WorkingDays = settings.WorkingDays,
            StartWithWindows = settings.StartWithWindows, MinimizeToTray = settings.MinimizeToTray,
            Theme = settings.Theme, BackupRetentionCount = settings.BackupRetentionCount,
            MinimumLogLevel = settings.MinimumLogLevel
        };
        this.networks = networks.Select(CloneNetwork).ToList();
        savedQualifyingNetworks = GetQualifyingNetworkNames(this.networks);
        this.logger = logger;
        Text = "Configurações";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(700, 620);
        MinimumSize = new Size(610, 500);
        BuildLayout(backup);
        saveTimer.Interval = 400;
        saveTimer.Tick += async (_, _) =>
        {
            saveTimer.Stop();
            await SaveAsync();
        };
        initializing = false;
        UiTheme.Apply(this, settings.Theme);
    }

    private void BuildLayout(IDatabaseBackupService backup)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(root);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        root.Controls.Add(tabs, 0, 0);

        var general = new TabPage("Geral");
        var generalGrid = SettingsGrid();
        general.Controls.Add(generalGrid);
        target.DecimalPlaces = 0; target.Minimum = 1m; target.Maximum = 100m; target.Increment = 1m;
        target.Value = Math.Clamp(decimal.Round(settings.TargetPercent, 0, MidpointRounding.AwayFromZero), 1m, 100m);
        AddSettingRow(generalGrid, 0, "Meta mensal (%)", target);
        startup.Text = "Iniciar automaticamente com o Windows"; startup.Checked = settings.StartWithWindows;
        AddSettingRow(generalGrid, 1, "Inicialização", startup);
        minimize.Text = "Ao fechar, manter o aplicativo na bandeja"; minimize.Checked = settings.MinimizeToTray;
        AddSettingRow(generalGrid, 2, "Fechamento", minimize);
        theme.DropDownStyle = ComboBoxStyle.DropDownList;
        theme.Items.AddRange(Enum.GetValues<ThemeMode>().Cast<object>().ToArray());
        theme.SelectedItem = settings.Theme;
        AddSettingRow(generalGrid, 3, "Tema", theme);
        retention.Minimum = 1; retention.Maximum = 365; retention.Value = Math.Clamp(settings.BackupRetentionCount, 1, 365);
        AddSettingRow(generalGrid, 4, "Quantidade de backups", retention);
        logLevel.DropDownStyle = ComboBoxStyle.DropDownList;
        logLevel.Items.AddRange(new object[] { "Debug", "Information", "Warning", "Error" });
        logLevel.SelectedItem = settings.MinimumLogLevel;
        AddSettingRow(generalGrid, 5, "Nível de log", logLevel);
        var folders = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        folders.Controls.Add(Button("Pasta de backups", (_, _) => OpenFolder(AppDataPaths.Backups)));
        folders.Controls.Add(Button("Pasta de logs", (_, _) => OpenFolder(AppDataPaths.Logs)));
        AddSettingRow(generalGrid, 6, "Pastas locais", folders);
        folders.Controls.Add(Button("Criar backup agora", async (_, _) =>
        {
            try { await backup.CreateBackupAsync(); saveStatus.Text = "Backup criado."; }
            catch (Exception ex) { logger.LogError(ex, "Backup manual falhou."); MessageBox.Show(this, "Não foi possível criar o backup.", "Backup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }));
        tabs.TabPages.Add(general);

        var networkPage = new TabPage("Redes");
        var networkRoot = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 2 };
        networkRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        networkRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        networkPage.Controls.Add(networkRoot);
        var addNetwork = Button("Adicionar rede presencial", (_, _) =>
        {
            networks.Add(new PresenceNetwork { Ssid = "", IsActive = true, CountsAsPresence = true });
            RenderNetworks();
            QueueSave();
        });
        networkRoot.Controls.Add(addNetwork, 0, 0);
        networkRows.Dock = DockStyle.Fill;
        networkRows.FlowDirection = FlowDirection.TopDown;
        networkRows.WrapContents = false;
        networkRows.AutoScroll = true;
        networkRows.SizeChanged += (_, _) => ResizeNetworkRows();
        networkRoot.Controls.Add(networkRows, 0, 1);
        RenderNetworks();
        tabs.TabPages.Add(networkPage);

        var workdaysPage = new TabPage("Dias úteis");
        var daysGrid = new FlowLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(14), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        var weekdayItems = new[]
        {
            (DayOfWeek.Monday, "Segunda-feira"), (DayOfWeek.Tuesday, "Terça-feira"),
            (DayOfWeek.Wednesday, "Quarta-feira"), (DayOfWeek.Thursday, "Quinta-feira"),
            (DayOfWeek.Friday, "Sexta-feira"), (DayOfWeek.Saturday, "Sábado"), (DayOfWeek.Sunday, "Domingo")
        };
        foreach (var (day, name) in weekdayItems)
        {
            var check = new CheckBox { Text = name, Width = 250, Height = 32, Checked = IsEnabled(settings.WorkingDays, day) };
            check.CheckedChanged += (_, _) => QueueSave();
            weekdays[day] = check;
            daysGrid.Controls.Add(check);
        }
        workdaysPage.Controls.Add(daysGrid);
        tabs.TabPages.Add(workdaysPage);

        var holidayPage = new TabPage("Feriados");
        var holidayPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(14), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        holidayPanel.Controls.Add(new Label { Text = "Consulte e corrija feriados nacionais, estaduais, municipais e personalizados.", AutoSize = true, Padding = new Padding(0, 4, 0, 16) });
        holidayPanel.Controls.Add(Button("Gerenciar feriados e sincronizar", (_, _) =>
        {
            using var form = new HolidayManagementForm(tracker, logger);
            form.ShowDialog(this);
        }));
        holidayPage.Controls.Add(holidayPanel);
        tabs.TabPages.Add(holidayPage);

        saveStatus.Text = "Todas as alterações são salvas automaticamente.";
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        saveStatus.Dock = DockStyle.Fill;
        saveStatus.AutoSize = false;
        saveStatus.AutoEllipsis = true;
        saveStatus.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(saveStatus, 0, 0);
        var closeButton = Button("Fechar", (_, _) => Close());
        closeButton.Dock = DockStyle.Fill;
        footer.Controls.Add(closeButton, 1, 0);
        root.Controls.Add(footer, 0, 1);

        target.ValueChanged += (_, _) => QueueSave();
        startup.CheckedChanged += (_, _) => QueueSave();
        minimize.CheckedChanged += (_, _) => QueueSave();
        theme.SelectedValueChanged += (_, _) => { QueueSave(); if (theme.SelectedItem is ThemeMode selected) UiTheme.Apply(this, selected); };
        retention.ValueChanged += (_, _) => QueueSave();
        logLevel.SelectedValueChanged += (_, _) => QueueSave();
    }

    private void RenderNetworks()
    {
        networkRows.Controls.Clear();
        foreach (var network in networks)
        {
            var row = new FlowLayoutPanel { Height = 42, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 2, 0, 3) };
            var active = new CheckBox { Text = "Ativa", Checked = network.IsActive, Width = 72, Height = 28, Padding = new Padding(0, 5, 0, 0) };
            active.CheckedChanged += (_, _) => { network.IsActive = active.Checked; QueueSave(); };
            var ssid = new TextBox { Text = network.Ssid, Width = 390, Height = 28, PlaceholderText = "SSID (ex.: CORP)" };
            ssid.TextChanged += (_, _) => { if (!string.IsNullOrWhiteSpace(ssid.Text)) { network.Ssid = ssid.Text.Trim(); QueueSave(); } };
            var remove = Button("Remover", (_, _) => { networks.Remove(network); RenderNetworks(); QueueSave(); });
            remove.Width = 90;
            row.Controls.Add(active); row.Controls.Add(ssid); row.Controls.Add(remove);
            networkRows.Controls.Add(row);
        }
        ResizeNetworkRows();
    }

    private void ResizeNetworkRows()
    {
        var rowWidth = Math.Max(330, networkRows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (FlowLayoutPanel row in networkRows.Controls)
        {
            row.Width = rowWidth;
            var ssid = row.Controls.OfType<TextBox>().FirstOrDefault();
            if (ssid is not null)
                ssid.Width = Math.Max(120, rowWidth - 72 - 90 - 22);
        }
    }

    private async Task SaveAsync()
    {
        if (initializing) return;
        if (saveInProgress)
        {
            savePending = true;
            return;
        }

        saveInProgress = true;
        try
        {
            settings.TargetPercent = target.Value;
            settings.StartWithWindows = startup.Checked;
            settings.MinimizeToTray = minimize.Checked;
            settings.Theme = theme.SelectedItem is ThemeMode selectedTheme ? selectedTheme : ThemeMode.System;
            settings.BackupRetentionCount = (int)retention.Value;
            settings.MinimumLogLevel = logLevel.SelectedItem?.ToString() ?? "Information";
            settings.WorkingDays = WorkingDays.None;
            foreach (var pair in weekdays)
                if (pair.Value.Checked)
                    settings.WorkingDays |= FlagFor(pair.Key);
            var cleanNetworks = networks.Where(n => !string.IsNullOrWhiteSpace(n.Ssid)).ToArray();
            var qualifyingNetworks = GetQualifyingNetworkNames(cleanNetworks);
            var newlyConfiguredNetworks = qualifyingNetworks.Except(savedQualifyingNetworks, StringComparer.OrdinalIgnoreCase).ToArray();
            await tracker.UpdateSettingsAsync(settings, cleanNetworks);
            WindowsStartup.SetEnabled(settings.StartWithWindows);
            AppLogging.SetMinimumLevel(settings.MinimumLogLevel);
            savedQualifyingNetworks.Clear();
            savedQualifyingNetworks.UnionWith(qualifyingNetworks);

            var connectedMatches = 0;
            if (newlyConfiguredNetworks.Length > 0)
            {
                try
                {
                    connectedMatches = await recordNewlyConfiguredNetworks(newlyConfiguredNetworks);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Não foi possível consultar conexões WLAN ao cadastrar uma rede presencial.");
                }
            }
            saveStatus.Text = connectedMatches > 0
                ? $"Salvo às {DateTime.Now:HH:mm:ss}. Presença da rede conectada registrada."
                : $"Salvo às {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao persistir configurações.");
            saveStatus.Text = "Não foi possível salvar. Verifique se os SSIDs não estão duplicados.";
        }
        finally
        {
            saveInProgress = false;
            if (savePending && !IsDisposed)
            {
                savePending = false;
                QueueSave();
            }
        }
    }

    private static HashSet<string> GetQualifyingNetworkNames(IEnumerable<PresenceNetwork> source) => source
        .Where(network => network.IsActive && network.CountsAsPresence && !string.IsNullOrWhiteSpace(network.Ssid))
        .Select(network => network.Ssid.Trim())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void QueueSave()
    {
        if (!initializing)
        {
            saveStatus.Text = "Salvando…";
            saveTimer.Stop();
            saveTimer.Start();
        }
    }

    private static TableLayoutPanel SettingsGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(14), ColumnCount = 2, RowCount = 8, AutoSize = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 8; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        return grid;
    }

    private static void AddSettingRow(TableLayoutPanel grid, int row, string label, Control control)
    {
        grid.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Left;
        grid.Controls.Add(control, 1, row);
    }

    private static bool IsEnabled(WorkingDays days, DayOfWeek day) => (days & FlagFor(day)) != 0;
    private static WorkingDays FlagFor(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WorkingDays.Monday, DayOfWeek.Tuesday => WorkingDays.Tuesday,
        DayOfWeek.Wednesday => WorkingDays.Wednesday, DayOfWeek.Thursday => WorkingDays.Thursday,
        DayOfWeek.Friday => WorkingDays.Friday, DayOfWeek.Saturday => WorkingDays.Saturday,
        _ => WorkingDays.Sunday
    };

    private static PresenceNetwork CloneNetwork(PresenceNetwork n) => new() { Id = n.Id, Ssid = n.Ssid, IsActive = n.IsActive, CountsAsPresence = n.CountsAsPresence };
    private static Button Button(string label, EventHandler onClick) { var b = new Button { Text = label, AutoSize = true, Height = 34 }; b.Click += onClick; return b; }
    private static void OpenFolder(string path) { Directory.CreateDirectory(path); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) saveTimer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class HolidayManagementForm : Form
{
    private readonly TrackerService tracker;
    private readonly ILogger logger;
    private readonly ListView list = new();
    private readonly NumericUpDown yearFilter = new() { Minimum = 1900, Maximum = 2199, Value = DateTime.Today.Year, Width = 90 };
    private IReadOnlyList<Holiday> holidays = [];
    private bool resizingColumns;

    public HolidayManagementForm(TrackerService tracker, ILogger logger)
    {
        this.tracker = tracker;
        this.logger = logger;
        Text = "Feriados";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 580);
        MinimumSize = new Size(640, 460);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(root);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        bar.Controls.Add(Button("Adicionar", async (_, _) => await AddHolidayAsync()));
        bar.Controls.Add(Button("Editar", (_, _) => EditSelected()));
        bar.Controls.Add(Button("Remover", async (_, _) => await RemoveSelectedAsync()));
        bar.Controls.Add(new Label { Text = "Ano:", AutoSize = true, Padding = new Padding(0, 9, 0, 0) });
        bar.Controls.Add(yearFilter);
        yearFilter.ValueChanged += async (_, _) => await RefreshAsync();
        bar.Controls.Add(Button("Sincronizar ano…", async (_, _) => await SyncAsync()));
        root.Controls.Add(bar, 0, 0);
        list.Dock = DockStyle.Fill;
        list.View = View.Details; list.FullRowSelect = true; list.GridLines = true;
        list.Columns.Add("Data", 110); list.Columns.Add("Nome", 320); list.Columns.Add("Escopo", 120); list.Columns.Add("Fonte", 120); list.Columns.Add("Ativo", 60);
        list.SizeChanged += (_, _) => ResizeColumns();
        list.DoubleClick += (_, _) => EditSelected();
        root.Controls.Add(list, 0, 1);
        root.Controls.Add(new Label { Text = "A edição de feriados é salva automaticamente.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        Load += async (_, _) => await RefreshAsync();
    }

    private void ResizeColumns()
    {
        if (resizingColumns || list.Columns.Count != 5) return;
        resizingColumns = true;
        try
        {
            var dateWidth = 100;
            var scopeWidth = 100;
            var sourceWidth = 100;
            var activeWidth = 55;
            var nameWidth = Math.Max(150, list.ClientSize.Width - dateWidth - scopeWidth - sourceWidth - activeWidth - SystemInformation.VerticalScrollBarWidth - 8);
            list.Columns[0].Width = dateWidth;
            list.Columns[1].Width = nameWidth;
            list.Columns[2].Width = scopeWidth;
            list.Columns[3].Width = sourceWidth;
            list.Columns[4].Width = activeWidth;
        }
        finally
        {
            resizingColumns = false;
        }
    }

    private async Task RefreshAsync()
    {
        holidays = await tracker.GetHolidaysAsync((int)yearFilter.Value);
        list.Items.Clear();
        foreach (var item in holidays)
        {
            var row = new ListViewItem(item.Date.ToString("dd/MM/yyyy"));
            row.SubItems.Add(item.Name); row.SubItems.Add(item.Scope.ToString()); row.SubItems.Add(item.Source.ToString()); row.SubItems.Add(item.IsActive ? "Sim" : "Não");
            row.Tag = item;
            list.Items.Add(row);
        }
    }

    private async Task AddHolidayAsync()
    {
        var holiday = new Holiday { Date = new DateOnly((int)yearFilter.Value, 1, 1), Name = "Novo feriado", Scope = HolidayScope.Custom, Source = HolidaySource.Manual, IsActive = true };
        await tracker.SaveHolidayAsync(holiday);
        using var editor = new HolidayEditorForm(holiday, tracker, logger);
        editor.ShowDialog(this);
        await RefreshAsync();
    }

    private void EditSelected()
    {
        if (list.SelectedItems.Count == 0) return;
        var holiday = (Holiday)list.SelectedItems[0].Tag!;
        using var editor = new HolidayEditorForm(holiday, tracker, logger);
        editor.ShowDialog(this);
        _ = RefreshAsync();
    }

    private async Task RemoveSelectedAsync()
    {
        if (list.SelectedItems.Count == 0) return;
        var holiday = (Holiday)list.SelectedItems[0].Tag!;
        if (MessageBox.Show(this, $"Remover o feriado {holiday.Name} de {holiday.Date:dd/MM/yyyy}?", "Confirmar remoção", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        await tracker.DeleteHolidayAsync(holiday.Id);
        await RefreshAsync();
    }

    private async Task SyncAsync()
    {
        using var prompt = new YearPickerForm();
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var provider = new PresenceTracker.Infrastructure.BrasilApiHolidayProvider(client);
            await tracker.SynchronizeHolidaysAsync(prompt.Year, new PresenceTracker.Infrastructure.CombinedHolidayProvider(new PresenceTracker.Infrastructure.LocalHolidayProvider(), provider));
            await RefreshAsync();
            MessageBox.Show(this, "Feriados sincronizados. Os feriados manuais foram preservados.", "Feriados", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Sincronização de feriados falhou para {Year}.", prompt.Year);
            MessageBox.Show(this, "Não foi possível sincronizar os feriados agora. A base local existente foi mantida.", "Feriados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Button Button(string text, EventHandler click) { var button = new Button { Text = text, AutoSize = true, Height = 34 }; button.Click += click; return button; }
}

internal sealed class HolidayEditorForm : Form
{
    private readonly Holiday holiday;
    private readonly TrackerService tracker;
    private readonly ILogger logger;
    private readonly System.Windows.Forms.Timer debounce = new();
    private readonly TextBox name = new();
    private readonly DateTimePicker date = new();
    private readonly ComboBox scope = new();
    private readonly CheckBox active = new();
    private bool initializing = true;

    public HolidayEditorForm(Holiday holiday, TrackerService tracker, ILogger logger)
    {
        this.holiday = holiday; this.tracker = tracker; this.logger = logger;
        Text = "Editar feriado";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(460, 280);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 5 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(grid);
        name.Text = holiday.Name;
        date.Value = holiday.Date.ToDateTime(TimeOnly.MinValue);
        scope.DropDownStyle = ComboBoxStyle.DropDownList;
        scope.Items.AddRange(Enum.GetValues<HolidayScope>().Cast<object>().ToArray());
        scope.SelectedItem = holiday.Scope;
        active.Text = "Ativo"; active.Checked = holiday.IsActive;
        AddField(grid, 0, "Nome", name); AddField(grid, 1, "Data", date); AddField(grid, 2, "Escopo", scope); AddField(grid, 3, "Status", active);
        var note = new Label { Text = "As alterações são gravadas automaticamente.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        grid.Controls.Add(note, 0, 4); grid.SetColumnSpan(note, 2);
        debounce.Interval = 300;
        debounce.Tick += async (_, _) => { debounce.Stop(); await PersistAsync(); };
        name.TextChanged += (_, _) => Queue();
        date.ValueChanged += (_, _) => Queue();
        scope.SelectedValueChanged += (_, _) => Queue();
        active.CheckedChanged += (_, _) => Queue();
        initializing = false;
    }

    private void Queue()
    {
        if (initializing) return;
        debounce.Stop(); debounce.Start();
    }

    private async Task PersistAsync()
    {
        holiday.Name = name.Text.Trim();
        holiday.Date = DateOnly.FromDateTime(date.Value);
        holiday.Scope = scope.SelectedItem is HolidayScope selected ? selected : HolidayScope.Custom;
        holiday.IsActive = active.Checked;
        if (string.IsNullOrWhiteSpace(holiday.Name)) return;
        try { await tracker.SaveHolidayAsync(holiday); }
        catch (Exception exception) { logger.LogError(exception, "Falha ao salvar feriado {HolidayId}.", holiday.Id); }
    }

    private static void AddField(TableLayoutPanel grid, int row, string label, Control control)
    {
        grid.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Fill; grid.Controls.Add(control, 1, row);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        debounce.Stop();
        if (!initializing) _ = PersistAsync();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing) { if (disposing) debounce.Dispose(); base.Dispose(disposing); }
}

internal sealed class YearPickerForm : Form
{
    private readonly NumericUpDown year = new() { Minimum = 1900, Maximum = 2199, Value = DateTime.Today.Year, Width = 120 };
    public int Year => (int)year.Value;
    public YearPickerForm()
    {
        Text = "Sincronizar feriados"; StartPosition = FormStartPosition.CenterParent; Size = new Size(320, 150); FormBorderStyle = FormBorderStyle.FixedDialog;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(new Label { Text = "Ano dos feriados:", AutoSize = true });
        panel.Controls.Add(year);
        var button = new Button { Text = "Sincronizar", DialogResult = DialogResult.OK, Width = 110 };
        panel.Controls.Add(button); AcceptButton = button; Controls.Add(panel);
    }
}


















