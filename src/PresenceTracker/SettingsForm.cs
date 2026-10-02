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
    private readonly HashSet<int> removedNetworkIds = [];
    private readonly FlowLayoutPanel networkRows = new();
    private readonly System.Windows.Forms.Timer saveTimer = new();
    private readonly Label saveStatus = new();
    private readonly NumericUpDown target = new();
    private readonly NumericUpDown retention = new();
    private readonly NumericUpDown backupInterval = new();
    private readonly TextBox backupFolder = new() { ReadOnly = true };
    private readonly CheckBox startup = new();
    private readonly CheckBox minimize = new();
    private readonly ComboBox theme = new();
    private readonly ComboBox logLevel = new();
    private readonly ComboBox weekStartsOn = new();
    private readonly Dictionary<DayOfWeek, CheckBox> weekdays = [];
    private bool initializing = true;
    private bool saveInProgress;
    private bool savePending;
    private bool selectingBackupFolder;

    public SettingsForm(TrackerService tracker, TrackerSettings settings,
        IReadOnlyList<PresenceNetwork> networks, IDatabaseBackupService backup, ILogger logger,
        Func<IReadOnlyCollection<string>, Task<int>> recordNewlyConfiguredNetworks,
        IReadOnlyCollection<string>? currentlyConnectedSsids = null)
    {
        this.tracker = tracker;
        this.recordNewlyConfiguredNetworks = recordNewlyConfiguredNetworks;
        this.settings = new TrackerSettings
        {
            Id = settings.Id, TargetPercent = settings.TargetPercent, WorkingDays = settings.WorkingDays,
            CalendarWeekStartsOn = settings.CalendarWeekStartsOn,
            StartWithWindows = settings.StartWithWindows, MinimizeToTray = settings.MinimizeToTray,
            Theme = settings.Theme, BackupRetentionCount = settings.BackupRetentionCount,
            BackupIntervalHours = settings.BackupIntervalHours,
            BackupFolder = settings.BackupFolder,
            MinimumLogLevel = settings.MinimumLogLevel
        };
        this.networks = networks.Select(CloneNetwork).ToList();
        foreach (var ssid in currentlyConnectedSsids ?? [])
            if (!string.IsNullOrWhiteSpace(ssid) && !this.networks.Any(n => string.Equals(n.Ssid, ssid.Trim(), StringComparison.OrdinalIgnoreCase)))
                this.networks.Add(new PresenceNetwork { Ssid = ssid.Trim(), IsActive = true, CountsAsPresence = false });
        savedQualifyingNetworks = GetQualifyingNetworkNames(this.networks);
        this.logger = logger;
        Text = "Configurações";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(780, 680);
        MinimumSize = new Size(620, 500);
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
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        Controls.Add(root);
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label { Text = "Configurações", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        heading.Controls.Add(new Label { Text = "Personalize metas, redes e comportamento do Presence Tracker.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        root.Controls.Add(heading, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(112, 32) };
        root.Controls.Add(tabs, 0, 1);

        var general = new TabPage("Geral");
        var generalGrid = SettingsGrid(5);
        var generalScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        general.Controls.Add(generalScroll);
        generalScroll.Controls.Add(generalGrid);
        target.DecimalPlaces = 0; target.Minimum = 1m; target.Maximum = 100m; target.Increment = 1m;
        target.Value = Math.Clamp(decimal.Round(settings.TargetPercent, 0, MidpointRounding.AwayFromZero), 1m, 100m);
        AddSettingRow(generalGrid, 0, "Meta mensal (%)", target);
        startup.Text = "Iniciar"; startup.Checked = settings.StartWithWindows;
        startup.AutoSize = false; startup.Dock = DockStyle.Fill; startup.TextAlign = ContentAlignment.MiddleLeft;
        AddSettingRow(generalGrid, 1, "Inicialização", startup);
        minimize.Text = "Ao fechar"; minimize.Checked = settings.MinimizeToTray;
        minimize.AutoSize = false; minimize.Dock = DockStyle.Fill; minimize.TextAlign = ContentAlignment.MiddleLeft;
        AddSettingRow(generalGrid, 2, "Fechamento", minimize);
        theme.DropDownStyle = ComboBoxStyle.DropDownList;
        theme.Items.AddRange(Enum.GetValues<ThemeMode>().Cast<object>().ToArray());
        theme.SelectedItem = settings.Theme;
        AddSettingRow(generalGrid, 3, "Tema", theme);
        tabs.TabPages.Add(general);

        var networkPage = new TabPage("Redes");
        var networkRoot = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 3 };
        networkRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        networkRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        networkRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        networkPage.Controls.Add(networkRoot);
        var addNetwork = Button("Adicionar rede Wi-Fi", (_, _) =>
        {
            networks.Add(new PresenceNetwork { Ssid = "", IsActive = true, CountsAsPresence = false });
            RenderNetworks();
            QueueSave();
        });
        networkRoot.Controls.Add(addNetwork, 0, 0);
        networkRoot.Controls.Add(NetworkHeader(), 0, 1);
        networkRows.Dock = DockStyle.Fill;
        networkRows.FlowDirection = FlowDirection.TopDown;
        networkRows.WrapContents = false;
        networkRows.AutoScroll = true;
        networkRows.SizeChanged += (_, _) => ResizeNetworkRows();
        networkRoot.Controls.Add(networkRows, 0, 2);
        RenderNetworks();
        tabs.TabPages.Add(networkPage);

        var dateConfigPage = new TabPage("Datas");
        var dateConfigScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        dateConfigPage.Controls.Add(dateConfigScroll);
        var dateConfigLayout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, AutoSize = true, Padding = Padding.Empty };
        dateConfigLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dateConfigLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dateConfigScroll.Controls.Add(dateConfigLayout);
        
        var dateGrid = SettingsGrid(1);
        weekStartsOn.DropDownStyle = ComboBoxStyle.DropDownList;
        weekStartsOn.Items.AddRange(["Segunda-feira", "Domingo"]);
        weekStartsOn.SelectedIndex = settings.CalendarWeekStartsOn == DayOfWeek.Sunday ? 1 : 0;
        AddSettingRow(dateGrid, 0, "Semana começa em", weekStartsOn);
        dateConfigLayout.Controls.Add(dateGrid, 0, 0);
        
        var dateActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(14, 6, 14, 12) };
        
        var weekdaysLabel = new Label { Text = "Dias úteis", Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        dateActions.Controls.Add(weekdaysLabel);
        var daysGrid = new FlowLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 16), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
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
        dateActions.Controls.Add(daysGrid);
        
        var holidaysLabel = new Label { Text = "Feriados", Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        dateActions.Controls.Add(holidaysLabel);
        dateActions.Controls.Add(new Label { Text = "Consulte e corrija feriados nacionais, estaduais, municipais e personalizados.", AutoSize = true, Padding = new Padding(0, 0, 0, 12) });
        dateActions.Controls.Add(Button("Gerenciar feriados e sincronizar", (_, _) =>
        {
            using var form = new HolidayManagementForm(tracker, logger, settings.Theme);
            form.ShowDialog(this);
        }));
        
        dateConfigLayout.Controls.Add(dateActions, 0, 1);
        tabs.TabPages.Add(dateConfigPage);

        var backupPage = new TabPage("Backup");
        var backupContent = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        backupPage.Controls.Add(backupContent);
        var backupLayout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, AutoSize = true, Padding = Padding.Empty };
        backupLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        backupLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        backupContent.Controls.Add(backupLayout);
        var backupGrid = SettingsGrid(3);
        retention.Minimum = 1; retention.Maximum = 365; retention.Value = Math.Clamp(settings.BackupRetentionCount, 1, 365);
        AddSettingRow(backupGrid, 0, "Quantidade de backups", retention);
        backupInterval.Minimum = 1; backupInterval.Maximum = 720; backupInterval.Value = Math.Clamp(settings.BackupIntervalHours, 1, 720);
        AddSettingRow(backupGrid, 1, "Intervalo automático (horas)", backupInterval);
        backupFolder.Text = string.IsNullOrWhiteSpace(settings.BackupFolder) ? AppDataPaths.Backups : settings.BackupFolder;
        var backupFolderEditor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Height = 38, Margin = Padding.Empty };
        backupFolderEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        backupFolderEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        backupFolder.Dock = DockStyle.Fill;
        backupFolderEditor.Controls.Add(backupFolder, 0, 0);
        backupFolderEditor.Controls.Add(Button("Escolher…", (_, _) => ChooseBackupFolder()), 1, 0);
        AddSettingRow(backupGrid, 2, "Pasta de backups", backupFolderEditor);
        backupLayout.Controls.Add(backupGrid, 0, 0);
        var backupActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(14, 2, 14, 12) };
        backupActions.Controls.Add(Button("Abrir backups", (_, _) => OpenFolder(GetBackupDirectory())));
        backupActions.Controls.Add(Button("Criar backup", async (_, _) =>
        {
            try
            {
                saveTimer.Stop();
                await SaveAsync();
                var path = await backup.CreateBackupAsync();
                saveStatus.Text = path is null ? "Banco de dados ainda não está disponível." : $"Backup criado: {Path.GetFileName(path)}";
            }
            catch (Exception ex) { logger.LogError(ex, "Backup manual falhou."); MessageBox.Show(this, "Não foi possível criar o backup.", "Backup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }));
        backupActions.Controls.Add(Button("Restaurar backup", async (_, _) => await RestoreBackupAsync(backup)));
        backupLayout.Controls.Add(backupActions, 0, 1);
        tabs.TabPages.Add(backupPage);

        var developerPage = new TabPage("Desenvolvedor");
        var developerGrid = SettingsGrid(2);
        var developerScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        developerPage.Controls.Add(developerScroll);
        developerScroll.Controls.Add(developerGrid);
        logLevel.DropDownStyle = ComboBoxStyle.DropDownList;
        logLevel.Items.AddRange(new object[] { "Debug", "Information", "Warning", "Error" });
        logLevel.SelectedItem = settings.MinimumLogLevel;
        AddSettingRow(developerGrid, 0, "Nível de log", logLevel);
        var factoryRestoreButton = Button("Restaurar configurações de fábrica", async (_, _) => await RestoreFactoryDefaultsAsync(backup));
        factoryRestoreButton.Dock = DockStyle.Fill;
        factoryRestoreButton.AutoSize = false;
        developerGrid.Controls.Add(factoryRestoreButton, 0, 1);
        developerGrid.SetColumnSpan(factoryRestoreButton, 2);
        var developerActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = true,
            Padding = new Padding(14, 6, 14, 6), Margin = Padding.Empty
        };
        developerActions.Controls.Add(Button("Abrir logs", (_, _) => OpenFolder(AppDataPaths.Logs)));
        developerPage.Controls.Add(developerActions);
        tabs.TabPages.Add(developerPage);

        saveStatus.Text = "Todas as alterações são salvas automaticamente.";
        saveStatus.Dock = DockStyle.Fill;
        saveStatus.AutoSize = false;
        saveStatus.AutoEllipsis = true;
        saveStatus.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(saveStatus, 0, 2);

        target.ValueChanged += (_, _) => QueueSave();
        startup.CheckedChanged += (_, _) => QueueSave();
        minimize.CheckedChanged += (_, _) => QueueSave();
        theme.SelectedValueChanged += (_, _) => { QueueSave(); if (theme.SelectedItem is ThemeMode selected) UiTheme.Apply(this, selected); };
        retention.ValueChanged += (_, _) => QueueSave();
        backupInterval.ValueChanged += (_, _) => QueueSave();
        logLevel.SelectedValueChanged += (_, _) => QueueSave();
        weekStartsOn.SelectedValueChanged += (_, _) => QueueSave();
    }

    private void RenderNetworks()
    {
        networkRows.Controls.Clear();
        foreach (var network in networks)
        {
            var row = new TableLayoutPanel { Height = 42, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 2, 0, 3), Padding = new Padding(4, 0, 4, 0), BackColor = UiTheme.Surface };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
            var ssid = new TextBox { Text = network.Ssid, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(2, 5, 8, 5), MaxLength = 32, PlaceholderText = "Nome da rede Wi-Fi" };
            ssid.TextChanged += (_, _) =>
            {
                network.Ssid = ssid.Text.Trim();
                QueueSave();
            };
            var qualifies = new CheckBox
            {
                Appearance = Appearance.Button, Checked = network.CountsAsPresence, Width = 54, Height = 28,
                TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.None,
                AccessibleName = $"Considerar {network.Ssid} como presencial"
            };
            qualifies.FlatAppearance.BorderSize = 0;
            void StylePresenceToggle()
            {
                qualifies.Text = qualifies.Checked ? "●" : "○";
                qualifies.BackColor = qualifies.Checked ? UiTheme.Blue : Color.FromArgb(213, 220, 228);
                qualifies.ForeColor = qualifies.Checked ? Color.White : Color.FromArgb(104, 117, 130);
            }
            StylePresenceToggle();
            qualifies.CheckedChanged += (_, _) => { network.CountsAsPresence = qualifies.Checked; StylePresenceToggle(); QueueSave(); };
            var remove = Button("Remover", (_, _) =>
            {
                if (network.Id != 0) removedNetworkIds.Add(network.Id);
                networks.Remove(network); RenderNetworks(); QueueSave();
            });
            remove.Dock = DockStyle.Fill;
            row.Controls.Add(ssid, 0, 0); row.Controls.Add(qualifies, 1, 0); row.Controls.Add(remove, 2, 0);
            networkRows.Controls.Add(row);
        }
        ResizeNetworkRows();
    }

    private static Control NetworkHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(4, 0, 4, 0), Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
        header.Controls.Add(new Label { Text = "SSID", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9, FontStyle.Bold) }, 0, 0);
        header.Controls.Add(new Label { Text = "Considerar presencial", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), AutoEllipsis = true }, 1, 0);
        header.Controls.Add(new Label { Text = "Ação", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9, FontStyle.Bold) }, 2, 0);
        return header;
    }

    private void ResizeNetworkRows()
    {
        var rowWidth = Math.Max(330, networkRows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (Control row in networkRows.Controls) row.Width = rowWidth;
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
            settings.CalendarWeekStartsOn = weekStartsOn.SelectedIndex == 1 ? DayOfWeek.Sunday : DayOfWeek.Monday;
            settings.StartWithWindows = startup.Checked;
            settings.MinimizeToTray = minimize.Checked;
            settings.Theme = theme.SelectedItem is ThemeMode selectedTheme ? selectedTheme : ThemeMode.System;
            settings.BackupRetentionCount = (int)retention.Value;
            settings.BackupIntervalHours = (int)backupInterval.Value;
            settings.BackupFolder = GetBackupDirectory();
            settings.MinimumLogLevel = logLevel.SelectedItem?.ToString() ?? "Information";
            settings.WorkingDays = WorkingDays.None;
            foreach (var pair in weekdays)
                if (pair.Value.Checked)
                    settings.WorkingDays |= FlagFor(pair.Key);
            var cleanNetworks = networks.Where(n => !string.IsNullOrWhiteSpace(n.Ssid)).ToArray();
            var qualifyingNetworks = GetQualifyingNetworkNames(cleanNetworks);
            var newlyConfiguredNetworks = qualifyingNetworks.Except(savedQualifyingNetworks, StringComparer.OrdinalIgnoreCase).ToArray();
            await tracker.UpdateSettingsAsync(settings, cleanNetworks, removedNetworkIds: removedNetworkIds);
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

    private string GetBackupDirectory() => Path.GetFullPath(string.IsNullOrWhiteSpace(backupFolder.Text)
        ? AppDataPaths.Backups : backupFolder.Text.Trim());

    private async void ChooseBackupFolder()
    {
        if (selectingBackupFolder) return;
        selectingBackupFolder = true;
        saveStatus.Text = "Abrindo seletor de pasta…";
        try
        {
            var selectedPath = await ShowFolderDialogOnStaThreadAsync(
                Directory.Exists(backupFolder.Text) ? backupFolder.Text : AppDataPaths.Backups);
            if (selectedPath is not null && !IsDisposed)
            {
                backupFolder.Text = selectedPath;
                QueueSave();
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "O seletor do Windows não conseguiu abrir a pasta de backup.");
            if (!IsDisposed)
                MessageBox.Show(this, "Não foi possível abrir o seletor de pastas. Tente novamente ou reinicie o Presence Tracker.",
                    "Pasta de backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            selectingBackupFolder = false;
            if (!IsDisposed && saveStatus.Text == "Abrindo seletor de pasta…")
                saveStatus.Text = "Todas as alterações são salvas automaticamente.";
        }
    }

    private static Task<string?> ShowFolderDialogOnStaThreadAsync(string initialPath)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Escolha onde o Presence Tracker salvará os arquivos de backup.",
                    UseDescriptionForTitle = true,
                    RootFolder = Environment.SpecialFolder.MyComputer,
                    SelectedPath = initialPath,
                    ShowNewFolderButton = true,
                    AutoUpgradeEnabled = true
                };
                completion.TrySetResult(dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Presence Tracker folder picker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private async Task RestoreBackupAsync(IDatabaseBackupService backup)
    {
        var backupPath = await ShowBackupFileDialogOnStaThreadAsync();
        if (backupPath is null || IsDisposed) return;
        var confirmation = MessageBox.Show(this,
            "A restauração substituirá as configurações e o histórico atuais pelos dados do arquivo selecionado. Um backup de segurança do estado atual será criado antes da importação. Continuar?",
            "Confirmar restauração", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirmation != DialogResult.Yes) return;

        if (saveInProgress) return;
        saveTimer.Stop();
        await SaveAsync();
        try
        {
            saveStatus.Text = "Validando e restaurando backup…";
            var safetyBackup = await backup.RestoreBackupAsync(backupPath);
            await tracker.RefreshAfterRestoreAsync();
            var restored = await tracker.GetMonthAsync(DateTime.Today.Year, DateTime.Today.Month, DateOnly.FromDateTime(DateTime.Today));
            WindowsStartup.SetEnabled(restored.Data.Settings.StartWithWindows);
            AppLogging.SetMinimumLevel(restored.Data.Settings.MinimumLogLevel);
            saveStatus.Text = safetyBackup is null
                ? "Backup restaurado."
                : $"Backup restaurado. Cópia de segurança: {Path.GetFileName(safetyBackup)}";
            MessageBox.Show(this, "Configurações e histórico de presença restaurados.", "Restauração concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao restaurar o arquivo {BackupPath}.", backupPath);
            MessageBox.Show(this, $"Não foi possível restaurar o backup. O banco atual foi preservado se a validação falhou.\n\n{exception.Message}",
                "Falha na restauração", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RestoreFactoryDefaultsAsync(IDatabaseBackupService backup)
    {
        var confirmation = MessageBox.Show(this,
            "Esta ação apagará as configurações atuais, redes, feriados personalizados, planejamentos e histórico de presença. Um backup de segurança será criado antes. Deseja continuar?",
            "Restaurar configurações de fábrica", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirmation != DialogResult.Yes || saveInProgress) return;

        saveTimer.Stop();
        await SaveAsync();
        try
        {
            saveStatus.Text = "Criando backup de segurança…";
            var safetyBackup = await backup.CreateBackupAsync()
                ?? throw new InvalidOperationException("O backup de segurança não foi criado.");
            saveStatus.Text = "Restaurando configurações de fábrica…";
            await tracker.ResetToFactoryDefaultsAsync();
            var restored = await tracker.GetMonthAsync(DateTime.Today.Year, DateTime.Today.Month, DateOnly.FromDateTime(DateTime.Today));
            WindowsStartup.SetEnabled(restored.Data.Settings.StartWithWindows);
            AppLogging.SetMinimumLevel(restored.Data.Settings.MinimumLogLevel);
            MessageBox.Show(this,
                $"As configurações de fábrica foram restauradas. O histórico foi limpo. Backup de segurança: {Path.GetFileName(safetyBackup)}",
                "Restauração concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao restaurar as configurações de fábrica.");
            MessageBox.Show(this, $"Não foi possível restaurar as configurações de fábrica.\n\n{exception.Message}",
                "Falha na restauração", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Task<string?> ShowBackupFileDialogOnStaThreadAsync()
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Restaurar backup do Presence Tracker",
                    Filter = "Backups Presence Tracker (*.ptbackup;*.db)|*.ptbackup;*.db|Todos os arquivos (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };
                completion.TrySetResult(dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Presence Tracker backup picker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void QueueSave()
    {
        if (!initializing)
        {
            saveStatus.Text = "Salvando…";
            saveTimer.Stop();
            saveTimer.Start();
        }
    }

    private static TableLayoutPanel SettingsGrid(int rowCount = 8)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(14), ColumnCount = 2, RowCount = rowCount, AutoSize = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rowCount; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        return grid;
    }

    private static void AddSettingRow(TableLayoutPanel grid, int row, string label, Control control)
    {
        grid.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = control is CheckBox or TableLayoutPanel ? DockStyle.Fill : DockStyle.Left;
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
    private static Button Button(string label, EventHandler onClick)
    {
        var b = new Button { Text = label, AutoSize = true, Height = 36, Padding = new Padding(10, 0, 10, 0) };
        if (label.StartsWith("Adicionar", StringComparison.Ordinal) || label.StartsWith("Sincronizar", StringComparison.Ordinal))
            b.Tag = "primary";
        b.Click += onClick;
        return b;
    }
    private static Button FooterButton(string label, EventHandler onClick)
    {
        var button = Button(label, onClick);
        button.Dock = DockStyle.Fill;
        button.AutoSize = false;
        button.MinimumSize = new Size(0, 38);
        button.Margin = new Padding(3, 4, 3, 4);
        return button;
    }
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
    private readonly ThemeMode theme;
    private readonly ListView list = new();
    private readonly NumericUpDown yearFilter = new() { Minimum = 1900, Maximum = 2199, Value = DateTime.Today.Year, Width = 90 };
    private IReadOnlyList<Holiday> holidays = [];
    private bool resizingColumns;

    public HolidayManagementForm(TrackerService tracker, ILogger logger, ThemeMode theme = ThemeMode.System)
    {
        this.tracker = tracker;
        this.logger = logger;
        this.theme = theme;
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
        UiTheme.Apply(this, theme);
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
        using var editor = new HolidayEditorForm(holiday, tracker, logger, theme);
        editor.ShowDialog(this);
        await RefreshAsync();
    }

    private void EditSelected()
    {
        if (list.SelectedItems.Count == 0) return;
        var holiday = (Holiday)list.SelectedItems[0].Tag!;
        using var editor = new HolidayEditorForm(holiday, tracker, logger, theme);
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
        using var prompt = new YearPickerForm(theme);
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

    private static Button Button(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 36, Padding = new Padding(10, 0, 10, 0) };
        if (text.StartsWith("Adicionar", StringComparison.Ordinal) || text.StartsWith("Sincronizar", StringComparison.Ordinal))
            button.Tag = "primary";
        button.Click += click;
        return button;
    }
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

    public HolidayEditorForm(Holiday holiday, TrackerService tracker, ILogger logger, ThemeMode theme = ThemeMode.System)
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
        UiTheme.Apply(this, theme);
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
    public YearPickerForm(ThemeMode theme = ThemeMode.System)
    {
        Text = "Sincronizar feriados"; StartPosition = FormStartPosition.CenterParent; Size = new Size(320, 150); FormBorderStyle = FormBorderStyle.FixedDialog;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(new Label { Text = "Ano dos feriados:", AutoSize = true });
        panel.Controls.Add(year);
        var button = new Button { Text = "Sincronizar", DialogResult = DialogResult.OK, Width = 110, Tag = "primary" };
        panel.Controls.Add(button); AcceptButton = button; Controls.Add(panel);
        UiTheme.Apply(this, theme);
    }
}


















