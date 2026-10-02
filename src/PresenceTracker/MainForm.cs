using System.Globalization;
using Microsoft.Extensions.Logging;
using PresenceTracker.Application;
using PresenceTracker.Domain;
using PresenceTracker.NetworkMonitoring;
using PresenceTracker.Persistence;

namespace PresenceTracker;

public sealed class MainForm : Form
{
    private readonly TrackerService tracker;
    private readonly WlanMonitor wlanMonitor;
    private readonly IDatabaseBackupService backup;
    private readonly ILogger<MainForm> logger;
    private readonly System.Drawing.Icon appIcon;
    private readonly TableLayoutPanel calendar = new();
    private readonly Label monthLabel = new();
    private readonly FlowLayoutPanel metricCards = new();
    private readonly FlowLayoutPanel actions = new();
    private readonly StatusStrip status = new();
    private readonly ToolStripStatusLabel statusText = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly HashSet<DateOnly> selectedDates = [];
    private MonthViewData? viewData;
    private DateOnly month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly lastObservedToday = DateOnly.FromDateTime(DateTime.Today);
    private bool followsCurrentMonth = true;
    private DateOnly? selectionAnchor;
    private bool exiting;
    private bool minimizeToTray = true;

    public MainForm(TrackerService tracker, WlanMonitor wlanMonitor,
        IDatabaseBackupService backup, ILogger<MainForm> logger)
    {
        this.tracker = tracker;
        this.wlanMonitor = wlanMonitor;
        this.backup = backup;
        this.logger = logger;
        appIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? System.Drawing.SystemIcons.Application;
        Text = "Presence Tracker";
        Icon = appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(900, 650);
        Size = new Size(1120, 790);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Segoe UI", 9F);
        BuildLayout();
        ConfigureTray();
        refreshTimer.Interval = 60_000;
        refreshTimer.Tick += async (_, _) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (today.Year != lastObservedToday.Year || today.Month != lastObservedToday.Month)
            {
                if (followsCurrentMonth)
                    month = new DateOnly(today.Year, today.Month, 1);
            }
            lastObservedToday = today;
            if (viewData is not null)
            {
                var configuredNetworks = viewData.Data.Networks
                    .Where(network => network.IsActive && network.CountsAsPresence)
                    .Select(network => network.Ssid)
                    .ToArray();
                try
                {
                    wlanMonitor.RecordCurrentConnectionsForNetworks(configuredNetworks);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Não foi possível reconciliar as redes conectadas.");
                }
            }
            await RefreshMonthAsync();
        };
        refreshTimer.Start();
        Load += async (_, _) => await RefreshMonthAsync();
        FormClosing += OnFormClosing;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        monthLabel.Font = new Font(Font.FontFamily, 16, FontStyle.Bold);
        monthLabel.Dock = DockStyle.Fill;
        monthLabel.AutoSize = false;
        monthLabel.AutoEllipsis = true;
        monthLabel.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(monthLabel, 0, 0);
        var previousMonth = CreateButton("‹", (_, _) => ShiftMonth(-1), 44);
        previousMonth.AccessibleName = "Mês anterior";
        var nextMonth = CreateButton("›", (_, _) => ShiftMonth(1), 44);
        nextMonth.AccessibleName = "Próximo mês";
        header.Controls.Add(previousMonth, 1, 0);
        header.Controls.Add(nextMonth, 2, 0);
        header.Controls.Add(CreateButton("Hoje", (_, _) => ShowCurrentMonth(), 66), 3, 0);
        var menu = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
        menu.Controls.Add(CreateButton("Configurações", (_, _) => OpenSettings(), 124));
        menu.Controls.Add(CreateButton("Histórico", (_, _) => OpenHistory(), 84));
        header.Controls.Add(menu, 4, 0);
        root.Controls.Add(header, 0, 0);

        metricCards.Dock = DockStyle.Fill;
        metricCards.WrapContents = false;
        metricCards.Padding = new Padding(0, 8, 0, 8);
        root.Controls.Add(metricCards, 0, 1);

        actions.Dock = DockStyle.Fill;
        actions.WrapContents = true;
        actions.AutoScroll = false;
        actions.AutoSize = true;
        actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        actions.Controls.Add(ActionButton("Presencial manual", BatchAction.ManualPresence));
        actions.Controls.Add(ActionButton("Planejar", BatchAction.PlanPresence));
        actions.Controls.Add(ActionButton("Férias", BatchAction.Vacation));
        actions.Controls.Add(ActionButton("Day off", BatchAction.DayOff));
        actions.Controls.Add(ActionButton("Feriado", BatchAction.Holiday));
        actions.Controls.Add(ActionButton("Não trabalhado", BatchAction.NonWorkingDay));
        actions.Controls.Add(ActionButton("Remover planejamento", BatchAction.RemovePlan));
        actions.Controls.Add(ActionButton("Remover classificação", BatchAction.RemoveClassification));
        actions.Controls.Add(ActionButton("Remover presença manual", BatchAction.RemoveManualPresence));
        root.Controls.Add(actions, 0, 2);

        calendar.Dock = DockStyle.Fill;
        calendar.ColumnCount = 7;
        calendar.RowCount = 7;
        calendar.Margin = new Padding(0, 5, 0, 5);
        for (var column = 0; column < 7; column++)
            calendar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7));
        calendar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        for (var row = 1; row < 7; row++)
            calendar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 6));
        root.Controls.Add(calendar, 0, 3);

        statusText.Text = "Monitoramento Wi-Fi ativo";
        status.Items.Add(statusText);
        status.Dock = DockStyle.Fill;
        root.Controls.Add(status, 0, 4);
    }

    private void ConfigureTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowWindow());
        menu.Items.Add("Status de hoje", null, (_, _) => ShowTodayDetails());
        menu.Items.Add("Configurações", null, (_, _) => OpenSettings());
        menu.Items.Add("Ver logs", null, (_, _) => OpenFolder(AppDataPaths.Logs));
        menu.Items.Add("Sobre", null, (_, _) => MessageBox.Show(this, "Presence Tracker\\nMonitoramento local de presença em redes configuradas.", "Sobre", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => { exiting = true; Close(); });
        trayIcon.Icon = appIcon;
        trayIcon.Text = "Presence Tracker";
        trayIcon.ContextMenuStrip = menu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (_, _) => ShowWindow();
    }

    public void RefreshFromNetworkChange()
    {
        if (!IsDisposed && IsHandleCreated)
            BeginInvoke((Action)(async () => await RefreshMonthAsync()));
    }

    private async Task RefreshMonthAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            monthLabel.Text = month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", new CultureInfo("pt-BR"));
            viewData = await tracker.GetMonthAsync(month.Year, month.Month, today);
            minimizeToTray = viewData.Data.Settings.MinimizeToTray;
            RenderMetrics(viewData.Metrics);
            RenderCalendar();
            UiTheme.Apply(this, viewData.Data.Settings.Theme);
            statusText.Text = $"Monitoramento Wi-Fi ativo · {viewData.Data.Networks.Count(n => n.IsActive && n.CountsAsPresence)} rede(s) presencial(is)";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao atualizar o calendário de {Year}-{Month}.", month.Year, month.Month);
            statusText.Text = "Erro ao carregar os dados locais.";
        }
    }

    private void RenderMetrics(MonthMetrics metrics)
    {
        metricCards.Controls.Clear();
        metricCards.Controls.Add(MetricCard("Ritmo até hoje", $"{metrics.RealizedThroughToday} / {metrics.ElapsedWorkingDays}", $"{metrics.CurrentPacePercent:0.#}% dos dias úteis decorridos"));
        metricCards.Controls.Add(MetricCard("Percentual do mês", $"{metrics.MonthlyPercent:0.#}%", $"Meta: {metrics.TargetPercent:0.#}% ({metrics.TargetDays} dias)"));
        metricCards.Controls.Add(MetricCard("Faltam", metrics.DaysNeeded.ToString(), $"de {metrics.DaysRemaining} dia(s) útil(eis) restantes"));
        var statusLine = metrics.TargetReachableByPlan ? "Meta atingível pelo planejamento" : $"Faltam {Math.Max(0, metrics.DaysNeeded - metrics.PlannedDays)} dia(s) planejado(s)";
        metricCards.Controls.Add(MetricCard("Projeção", $"{metrics.ProjectedPercent:0.#}%", statusLine));
    }

    private Control MetricCard(string title, string value, string subtitle)
    {
        var panel = new Panel { Width = 245, Height = 96, Margin = new Padding(0, 0, 12, 0), BackColor = Color.White, Padding = new Padding(12) };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.FromArgb(82, 95, 111), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        content.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, AutoSize = false, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), ForeColor = Color.FromArgb(32, 54, 79), TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        content.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.FromArgb(92, 104, 120), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        panel.Controls.Add(content);
        return panel;
    }

    private void RenderCalendar()
    {
        if (viewData is null) return;
        calendar.SuspendLayout();
        calendar.Controls.Clear();
        var names = new[] { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" };
        for (var i = 0; i < names.Length; i++)
            calendar.Controls.Add(new Label { Text = names[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(82, 95, 111), Font = new Font(Font.FontFamily, 9, FontStyle.Bold) }, i, 0);

        var first = new DateOnly(month.Year, month.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7;
        for (var index = 0; index < 42; index++)
        {
            var dayNumber = index - offset + 1;
            if (dayNumber < 1 || dayNumber > DateTime.DaysInMonth(month.Year, month.Month))
            {
                calendar.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = new Padding(2), BackColor = Color.FromArgb(240, 242, 245) }, index % 7, index / 7 + 1);
                continue;
            }
            var date = new DateOnly(month.Year, month.Month, dayNumber);
            var day = viewData.Days[dayNumber - 1];
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(7),
                Font = new Font(Font.FontFamily, 9),
                Text = $"{dayNumber}{Environment.NewLine}{DayTag(day.Outcome)}",
                BackColor = DayColor(day.Outcome),
                ForeColor = Color.FromArgb(38, 51, 67),
                Tag = date
            };
            button.FlatAppearance.BorderSize = selectedDates.Contains(date) ? 2 : 1;
            button.FlatAppearance.BorderColor = selectedDates.Contains(date) ? Color.FromArgb(36, 111, 209) : Color.FromArgb(222, 227, 233);
            var tip = new ToolTip();
            tip.SetToolTip(button, day.Outcome.Reason + (day.Outcome.HolidayName is null ? "" : $": {day.Outcome.HolidayName}"));
            button.Click += (_, e) => SelectDate(date, (ModifierKeys & Keys.Control) == Keys.Control, (ModifierKeys & Keys.Shift) == Keys.Shift);
            button.MouseDoubleClick += (_, _) => ShowDayDetails(date);
            calendar.Controls.Add(button, index % 7, index / 7 + 1);
        }
        calendar.ResumeLayout();
    }

    private static string DayTag(DayOutcome outcome)
    {
        if (outcome.IsHoliday) return "Feriado";
        if (outcome.Absence is not null) return PresenceCalculator.AbsenceLabel(outcome.Absence.Value);
        if (outcome.HasManualAttendance) return "Presencial manual";
        if (outcome.HasExcludedAutomaticAttendance) return "Automático excluído";
        if (outcome.CountsAsPresence) return "Presencial automático";
        if (outcome.IsPlanned) return "Planejado";
        return outcome.IsWorkingDay ? "Dia útil" : "Não útil";
    }

    private static Color DayColor(DayOutcome outcome)
    {
        if (outcome.IsHoliday || outcome.Absence == AbsenceType.Holiday) return Color.FromArgb(235, 238, 242);
        if (outcome.Absence == AbsenceType.Vacation) return Color.FromArgb(255, 232, 236);
        if (outcome.Absence == AbsenceType.DayOff) return Color.FromArgb(255, 242, 218);
        if (outcome.Absence is not null) return Color.FromArgb(238, 232, 250);
        if (outcome.HasManualAttendance) return Color.FromArgb(214, 239, 255);
        if (outcome.CountsAsPresence) return Color.FromArgb(218, 245, 225);
        if (outcome.IsPlanned) return Color.FromArgb(255, 248, 208);
        return outcome.IsWorkingDay ? Color.White : Color.FromArgb(245, 246, 248);
    }

    private void SelectDate(DateOnly date, bool control, bool shift)
    {
        if (shift && selectionAnchor is { } anchor)
        {
            selectedDates.Clear();
            var first = anchor < date ? anchor : date;
            var last = anchor > date ? anchor : date;
            for (var current = first; current <= last; current = current.AddDays(1))
                selectedDates.Add(current);
        }
        else if (control)
        {
            if (!selectedDates.Add(date)) selectedDates.Remove(date);
            selectionAnchor = date;
        }
        else
        {
            selectedDates.Clear();
            selectedDates.Add(date);
            selectionAnchor = date;
        }
        RenderCalendar();
    }

    private Button ActionButton(string title, BatchAction action)
    {
        var button = CreateButton(title, async (_, _) => await ApplyActionAsync(action), 100);
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(96, 40);
        button.Padding = new Padding(8, 0, 8, 0);
        button.Height = 40;
        button.Margin = new Padding(0, 4, 6, 4);
        return button;
    }

    private async Task ApplyActionAsync(BatchAction action)
    {
        if (selectedDates.Count == 0)
        {
            statusText.Text = "Selecione um ou mais dias no calendário.";
            return;
        }
        try
        {
            if (selectedDates.Count >= 5 || action == BatchAction.RemoveClassification)
                await backup.CreateBackupAsync();
            await tracker.ApplyBatchAsync(selectedDates, action);
            selectedDates.Clear();
            selectionAnchor = null;
            await RefreshMonthAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao aplicar ação {Action} aos dias selecionados.", action);
            MessageBox.Show(this, "Não foi possível aplicar a alteração. Os dados anteriores foram mantidos.", "Presence Tracker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowDayDetails(DateOnly date)
    {
        var day = viewData?.Days.FirstOrDefault(x => x.Outcome.Date == date);
        if (day is null) return;
        using var dialog = new DayDetailsForm(day, async (id, status) =>
        {
            await tracker.SetAttendanceStatusAsync(id, status);
            await RefreshMonthAsync();
        });
        dialog.ShowDialog(this);
    }

    private async void ShowTodayDetails()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        month = new DateOnly(today.Year, today.Month, 1);
        followsCurrentMonth = true;
        await RefreshMonthAsync();
        ShowWindow();
        ShowDayDetails(today);
    }

    private void ShowCurrentMonth()
    {
        month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        followsCurrentMonth = true;
        selectedDates.Clear();
        selectionAnchor = null;
        _ = RefreshMonthAsync();
    }

    private Task<int> RecordNewlyConfiguredNetworksAsync(IReadOnlyCollection<string> ssids) =>
        Task.FromResult(wlanMonitor.RecordCurrentConnectionsForNetworks(ssids, force: true));
    private void ShiftMonth(int amount)
    {
        month = month.AddMonths(amount);
        followsCurrentMonth = false;
        selectedDates.Clear();
        selectionAnchor = null;
        _ = RefreshMonthAsync();
    }

    private void OpenSettings()
    {
        if (viewData is null) return;
        using var dialog = new SettingsForm(tracker, viewData.Data.Settings,
            viewData.Data.Networks, backup, logger, RecordNewlyConfiguredNetworksAsync);
        dialog.ShowDialog(this);
        _ = RefreshMonthAsync();
    }

    private void OpenHistory()
    {
        using var dialog = new HistoryForm(tracker, viewData?.Data.Settings.Theme ?? ThemeMode.System);
        dialog.ShowDialog(this);
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private void ShowWindow()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!exiting && minimizeToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            trayIcon.ShowBalloonTip(1200, "Presence Tracker", "O monitoramento continua na bandeja.", ToolTipIcon.Info);
        }
        else
            refreshTimer.Stop();
    }

    private static Button CreateButton(string text, EventHandler handler, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(44, 64, 87), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = Color.FromArgb(215, 221, 229);
        button.Click += handler;
        return button;
    }

    protected override void WndProc(ref Message message)
    {
        const int wmPowerBroadcast = 0x0218;
        const int pbtApmSuspend = 0x0004;
        if (message.Msg == wmPowerBroadcast && message.WParam.ToInt32() == pbtApmSuspend)
            wlanMonitor.PrepareForSuspend();
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refreshTimer.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}













