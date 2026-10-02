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
    private readonly TableLayoutPanel metricCards = new();
    private readonly FlowLayoutPanel actions = new();
    private readonly TableLayoutPanel summary = new();
    private readonly Dictionary<string, Label> summaryValues = new();
    private readonly Label goalMessage = new();
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
    private ThemeMode currentTheme = ThemeMode.System;

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
        MinimumSize = new Size(1024, 700);
        Size = new Size(1440, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Background;
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
                    wlanMonitor.ObserveCurrentConnections();
                    wlanMonitor.RecordCurrentConnectionsForNetworks(configuredNetworks);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Não foi possível reconciliar as redes conectadas.");
                }
            }
            await RefreshMonthAsync();
            if (viewData is not null)
            {
                try { await backup.CreateBackupIfDueAsync(); }
                catch (Exception exception) { logger.LogWarning(exception, "Não foi possível criar o backup automático agendado."); }
            }
        };
        refreshTimer.Start();
        Load += async (_, _) => await RefreshMonthAsync();
        FormClosing += OnFormClosing;
    }

    private void BuildLayout()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.SizeChanged += (_, _) =>
        {
            shell.ColumnStyles[0].Width = Math.Clamp(shell.ClientSize.Width * 0.18f, 190f, 270f);
        };
        Controls.Add(shell);
        shell.Controls.Add(BuildSidebar(), 0, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22, 16, 22, 12), Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        shell.Controls.Add(root, 1, 0);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        var monthNavigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Left, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, Padding = Padding.Empty
        };
        var previousMonth = CreateButton("‹", (_, _) => ShiftMonth(-1), 44);
        previousMonth.AccessibleName = "Mês anterior";
        monthLabel.Font = new Font(Font.FontFamily, 16, FontStyle.Bold);
        monthLabel.Width = 260;
        monthLabel.Height = 38;
        monthLabel.Margin = new Padding(8, 0, 8, 0);
        monthLabel.AutoSize = false;
        monthLabel.AutoEllipsis = false;
        monthLabel.TextAlign = ContentAlignment.MiddleLeft;
        var nextMonth = CreateButton("›", (_, _) => ShiftMonth(1), 44);
        nextMonth.AccessibleName = "Próximo mês";
        monthNavigation.Controls.Add(previousMonth);
        monthNavigation.Controls.Add(monthLabel);
        monthNavigation.Controls.Add(nextMonth);
        header.Controls.Add(monthNavigation, 0, 0);
        header.Controls.Add(CreateButton("Hoje", (_, _) => ShowCurrentMonth(), 72), 1, 0);
        root.Controls.Add(header, 0, 0);

        metricCards.Dock = DockStyle.Fill;
        metricCards.ColumnCount = 4;
        metricCards.RowCount = 1;
        metricCards.Margin = Padding.Empty;
        metricCards.Padding = new Padding(0, 5, 0, 5);
        for (var i = 0; i < 4; i++) metricCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        metricCards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(metricCards, 0, 1);

        actions.Dock = DockStyle.Fill;
        actions.WrapContents = true;
        actions.AutoScroll = false;
        actions.AutoSize = true;
        actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        actions.Margin = new Padding(0, 0, 0, 5);
        actions.Controls.Add(ActionButton("Presencial manual", BatchAction.ManualPresence));
        actions.Controls.Add(ActionButton("Planejar", BatchAction.PlanPresence));
        var nonWorkingButton = CreateActionButton("Não trabalhado", ChooseNonWorkingReasonAsync, 130);
        SetButtonTone(nonWorkingButton, "absence");
        actions.Controls.Add(nonWorkingButton);
        actions.Controls.Add(ActionButton("Restaurar padrão", BatchAction.RestoreDefaults));
        var dayDetailsButton = CreateButton("Detalhes do dia", (_, _) => ShowSelectedDayDetails(), 136);
        dayDetailsButton.AccessibleName = "Detalhes do dia selecionado";
        actions.Controls.Add(dayDetailsButton);
        root.Controls.Add(actions, 0, 2);

        var calendarArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        calendarArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        calendarArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        calendar.Dock = DockStyle.Fill;
        calendar.ColumnCount = 7;
        calendar.RowCount = 7;
        calendar.Margin = new Padding(0, 5, 8, 5);
        calendar.BackColor = Color.Transparent;
        for (var column = 0; column < 7; column++)
            calendar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7));
        calendar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        for (var row = 1; row < 7; row++)
            calendar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 6));
        calendarArea.Controls.Add(calendar, 0, 0);
        BuildMonthSummary();
        calendarArea.Controls.Add(summary, 1, 0);
        root.Controls.Add(calendarArea, 0, 3);

        statusText.Text = "Monitoramento Wi-Fi ativo";
        status.Items.Add(statusText);
        status.Dock = DockStyle.Fill;
        root.Controls.Add(status, 0, 4);
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(15, 18, 15, 16),
            BackColor = UiTheme.Navy, ForeColor = Color.White, Tag = "brand:sidebar", Margin = Padding.Empty
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));

        var brand = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Navy, Tag = "brand:sidebar" };
        var logo = new Label
        {
            Text = "P", TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.White, BackColor = UiTheme.Blue, Location = new Point(2, 4), Size = new Size(38, 38), Tag = "brand:sidebar"
        };
        brand.Controls.Add(logo);
        var brandTitle = new Label
        {
            Text = "Presence Tracker", Location = new Point(2, 52), Size = new Size(174, 32),
            Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.White, Tag = "brand:sidebar",
            AutoSize = false, AutoEllipsis = false, TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        brand.Controls.Add(brandTitle);
        brand.SizeChanged += (_, _) => brandTitle.Width = Math.Max(0, brand.ClientSize.Width - brandTitle.Left - 2);
        sidebar.Controls.Add(brand, 0, 0);

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false,
            BackColor = UiTheme.Navy, Tag = "brand:sidebar", Margin = Padding.Empty, Padding = Padding.Empty
        };
        navigation.SizeChanged += (_, _) =>
        {
            foreach (var button in navigation.Controls.OfType<Button>())
                button.Width = Math.Max(0, navigation.ClientSize.Width);
        };
        navigation.Controls.Add(NavigationButton("▦   Calendário", "Calendário", () => { }));
        navigation.Controls.Add(NavigationButton("◷   Histórico", "Histórico", OpenHistory));
        navigation.Controls.Add(NavigationButton("⚙   Configurações", "Configurações", OpenSettings));
        sidebar.Controls.Add(navigation, 0, 1);

        var monitor = new Label
        {
            Text = "●  Monitoramento ativo\n    Wi-Fi em segundo plano", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(126, 228, 168),
            Font = new Font("Segoe UI", 8.5F), Tag = "brand:sidebar"
        };
        sidebar.Controls.Add(monitor, 0, 2);
        return sidebar;
    }

    private Button NavigationButton(string title, string section, Action action)
    {
        var button = new Button
        {
            Text = title, Width = 182, Height = 42, Margin = new Padding(0, 2, 0, 5),
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 4, 0),
            FlatStyle = FlatStyle.Flat, BackColor = section == "Calendário" ? UiTheme.Blue : UiTheme.Navy,
            ForeColor = Color.White, Font = new Font("Segoe UI", 9F), Cursor = Cursors.Hand,
            Tag = $"brand:nav:{section}"
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) =>
        {
            SetActiveNavigation(section);
            try { action(); }
            finally
            {
                if (section != "Calendário")
                    SetActiveNavigation("Calendário");
            }
        };
        return button;
    }

    private void SetActiveNavigation(string section)
    {
        if (Controls.Count == 0) return;
        var shell = Controls[0];
        var sidebar = shell.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        var navigation = sidebar?.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        if (navigation is null) return;
        foreach (var button in navigation.Controls.OfType<Button>())
        {
            var active = button.Tag is string tag && tag.EndsWith(section, StringComparison.Ordinal);
            button.BackColor = active ? UiTheme.Blue : UiTheme.Navy;
        }
    }

    private void BuildMonthSummary()
    {
        summary.Dock = DockStyle.Fill;
        summary.ColumnCount = 1;
        summary.RowCount = 9;
        summary.Padding = new Padding(18, 15, 18, 14);
        summary.Margin = new Padding(4, 5, 0, 5);
        summary.BackColor = UiTheme.Surface;
        summary.Tag = "card";
        summary.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        for (var i = 0; i < 5; i++) summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        var heading = new Label { Text = "Resumo do mês", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        summary.Controls.Add(heading, 0, 0);
        AddSummaryRow(1, "Dias úteis", "workdays");
        AddSummaryRow(2, "Presenças realizadas", "realized");
        AddSummaryRow(3, "Planejados", "planned");
        AddSummaryRow(4, "Faltam para a meta", "needed");
        AddSummaryRow(5, "Dias úteis restantes", "remaining");
        AddSummaryRow(6, "Projeção atual", "projected");
        var spacer = new Panel { Dock = DockStyle.Fill, Tag = "card" };
        summary.Controls.Add(spacer, 0, 7);
        goalMessage.Dock = DockStyle.Fill;
        goalMessage.Padding = new Padding(10, 5, 10, 5);
        goalMessage.BackColor = Color.FromArgb(232, 249, 239);
        goalMessage.ForeColor = UiTheme.Green;
        goalMessage.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        goalMessage.TextAlign = ContentAlignment.MiddleLeft;
        goalMessage.AutoEllipsis = true;
        goalMessage.Tag = "brand:goal";
        summary.Controls.Add(goalMessage, 0, 8);
    }

    private void AddSummaryRow(int row, string title, string key)
    {
        var line = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, Tag = "card" };
        line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        line.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var value = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = UiTheme.Text };
        summaryValues[key] = value;
        line.Controls.Add(value, 1, 0);
        summary.Controls.Add(line, 0, row);
    }

    private void ConfigureTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowWindow());
        menu.Items.Add("Status de hoje", null, (_, _) => ShowTodayDetails());
        menu.Items.Add("Configurações", null, (_, _) => OpenSettings());
        menu.Items.Add("Ver logs", null, (_, _) => OpenFolder(AppDataPaths.Logs));
        menu.Items.Add("Sobre", null, (_, _) => MessageBox.Show(this, "Presence Tracker\nMonitoramento local de presença em redes configuradas.", "Sobre", MessageBoxButtons.OK, MessageBoxIcon.Information));
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
            var monthTitle = month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", new CultureInfo("pt-BR"));
            monthLabel.Text = new CultureInfo("pt-BR").TextInfo.ToTitleCase(monthTitle);
            monthLabel.Width = TextRenderer.MeasureText(monthLabel.Text, monthLabel.Font).Width + 20;
            viewData = await tracker.GetMonthAsync(month.Year, month.Month, today);
            minimizeToTray = viewData.Data.Settings.MinimizeToTray;
            currentTheme = viewData.Data.Settings.Theme;
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
        metricCards.Controls.Add(MetricCard("Ritmo atual", $"{metrics.CurrentPacePercent:0.#}%", $"{metrics.RealizedThroughToday} / {metrics.ElapsedWorkingDays} dias", Color.FromArgb(27, 158, 91), metrics.CurrentPacePercent), 0, 0);
        metricCards.Controls.Add(MetricCard("Mês realizado", $"{metrics.MonthlyPercent:0.#}%", $"{metrics.RealizedDays} dia(s) presencial(is)", Color.FromArgb(126, 73, 214), metrics.MonthlyPercent), 1, 0);
        var projectedDays = Math.Min(metrics.EligibleWorkingDays, metrics.RealizedDays + metrics.PlannedDays);
        metricCards.Controls.Add(MetricCard("Planejamento", $"{metrics.ProjectedPercent:0.#}%", $"{projectedDays} dia(s) realizados/planejados", Color.FromArgb(20, 111, 242), metrics.ProjectedPercent), 2, 0);
        var statusLine = metrics.TargetReachableByPlan ? "Meta atingível pelo planejamento" : $"Faltam {Math.Max(0, metrics.DaysNeeded - metrics.PlannedDays)} dia(s) planejado(s)";
        metricCards.Controls.Add(MetricCard("Meta", $"{metrics.TargetPercent:0}%", $"Necessários: {metrics.TargetDays} dias"), 3, 0);
        summaryValues["workdays"].Text = metrics.EligibleWorkingDays.ToString();
        summaryValues["realized"].Text = $"{metrics.RealizedDays} dia(s)";
        summaryValues["planned"].Text = $"{metrics.PlannedDays} dia(s)";
        summaryValues["needed"].Text = $"{metrics.DaysNeeded} dia(s)";
        summaryValues["remaining"].Text = $"{metrics.DaysRemaining} dia(s)";
        summaryValues["projected"].Text = $"{metrics.ProjectedPercent:0.#}%";
        goalMessage.Text = metrics.TargetReachableByPlan
            ? "✓  Você está dentro da meta com o planejamento atual."
            : $"!  {statusLine}";
        goalMessage.BackColor = currentTheme == ThemeMode.Dark
            ? (metrics.TargetReachableByPlan ? Color.FromArgb(25, 63, 49) : Color.FromArgb(72, 55, 27))
            : (metrics.TargetReachableByPlan ? Color.FromArgb(232, 249, 239) : Color.FromArgb(255, 245, 224));
        goalMessage.ForeColor = metrics.TargetReachableByPlan ? Color.FromArgb(27, 158, 91) : Color.FromArgb(177, 111, 0);
    }

    private Control MetricCard(string title, string value, string subtitle, Color? accent = null, decimal? progress = null)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Height = 96, Margin = new Padding(0, 0, 10, 0), BackColor = UiTheme.Surface, Padding = new Padding(12), Tag = "card" };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty, Tag = "card" };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        content.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, AutoSize = false, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        content.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, AutoSize = false, Font = new Font(Font.FontFamily, 18, FontStyle.Bold), ForeColor = accent ?? UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft, Tag = accent is null ? null : "brand:metric-accent" }, 0, 1);
        content.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Fill, AutoSize = false, ForeColor = UiTheme.Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        if (progress is { } progressValue && accent is { } accentColor)
        {
            var track = new Panel
            {
                Dock = DockStyle.Fill, Height = 7, BackColor = currentTheme == ThemeMode.Dark ? Color.FromArgb(58, 72, 89) : Color.FromArgb(231, 237, 243),
                Tag = "brand:metric-track"
            };
            var fill = new Panel { Dock = DockStyle.Left, Width = 0, BackColor = accentColor, Tag = "brand:metric-fill" };
            track.Controls.Add(fill);
            void ResizeFill() => fill.Width = (int)Math.Round(track.ClientSize.Width * (double)Math.Clamp(progressValue, 0m, 100m) / 100d);
            track.SizeChanged += (_, _) => ResizeFill();
            ResizeFill();
            content.Controls.Add(track, 0, 3);
        }
        panel.Controls.Add(content);
        return panel;
    }

    private void RenderCalendar()
    {
        if (viewData is null) return;
        calendar.SuspendLayout();
        calendar.Controls.Clear();
        var mondayFirst = new[] { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" };
        var startsSunday = viewData.Data.Settings.CalendarWeekStartsOn == DayOfWeek.Sunday;
        var names = startsSunday ? new[] { "Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb" } : mondayFirst;
        for (var i = 0; i < names.Length; i++)
            calendar.Controls.Add(new Label { Text = names[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.Muted, Font = new Font(Font.FontFamily, 9, FontStyle.Bold) }, i, 0);

        var first = new DateOnly(month.Year, month.Month, 1);
        var weekStart = startsSunday ? DayOfWeek.Sunday : DayOfWeek.Monday;
        var offset = ((int)first.DayOfWeek - (int)weekStart + 7) % 7;
        for (var index = 0; index < 42; index++)
        {
            var dayNumber = index - offset + 1;
            if (dayNumber < 1 || dayNumber > DateTime.DaysInMonth(month.Year, month.Month))
            {
                calendar.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = new Padding(2), BackColor = currentTheme == ThemeMode.Dark ? Color.FromArgb(24, 36, 49) : Color.FromArgb(237, 242, 247) }, index % 7, index / 7 + 1);
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
                ForeColor = currentTheme == ThemeMode.Dark ? Color.FromArgb(230, 237, 245) : UiTheme.Text,
                Tag = date
            };
            button.FlatAppearance.BorderSize = selectedDates.Contains(date) ? 2 : 1;
            button.FlatAppearance.BorderColor = selectedDates.Contains(date) ? UiTheme.Blue : UiTheme.Border;
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

    private Color DayColor(DayOutcome outcome)
    {
        if (currentTheme == ThemeMode.Dark)
        {
            if (outcome.IsHoliday || outcome.Absence is not null || !outcome.IsWorkingDay) return Color.FromArgb(43, 55, 68);
            if (outcome.HasExcludedAutomaticAttendance) return Color.FromArgb(29, 43, 58);
            if (outcome.HasManualAttendance || outcome.CountsAsPresence) return Color.FromArgb(28, 66, 48);
            if (outcome.IsPlanned) return Color.FromArgb(34, 55, 88);
            return Color.FromArgb(29, 43, 58);
        }
        if (outcome.IsHoliday || outcome.Absence is not null || !outcome.IsWorkingDay) return Color.FromArgb(235, 239, 244);
        if (outcome.HasExcludedAutomaticAttendance) return UiTheme.Surface;
        if (outcome.HasManualAttendance || outcome.CountsAsPresence) return Color.FromArgb(226, 247, 234);
        if (outcome.IsPlanned) return Color.FromArgb(231, 241, 255);
        return UiTheme.Surface;
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
        UpdateCalendarSelection();
    }

    private void UpdateCalendarSelection()
    {
        foreach (var dayButton in calendar.Controls.OfType<Button>())
        {
            if (dayButton.Tag is not DateOnly date) continue;
            var selected = selectedDates.Contains(date);
            dayButton.FlatAppearance.BorderSize = selected ? 2 : 1;
            dayButton.FlatAppearance.BorderColor = selected ? UiTheme.Blue : UiTheme.Border;
        }
    }

    private Button ActionButton(string title, BatchAction action)
    {
        var button = CreateActionButton(title, () => ApplyActionAsync(action), 100);
        SetButtonTone(button, action switch
        {
            BatchAction.ManualPresence => "presence",
            BatchAction.PlanPresence => "plan",
            _ => "neutral"
        });
        return button;
    }

    private Button CreateActionButton(string title, Func<Task> action, int width)
    {
        var button = CreateButton(title, async (_, _) => await action(), width);
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(96, 40);
        button.Padding = new Padding(8, 0, 8, 0);
        button.Height = 40;
        button.Margin = new Padding(0, 4, 6, 4);
        return button;
    }

    private async Task ChooseNonWorkingReasonAsync()
    {
        if (selectedDates.Count == 0)
        {
            statusText.Text = "Selecione um ou mais dias no calendário.";
            return;
        }

        using var dialog = new NonWorkingReasonForm(currentTheme);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedAction is { } action)
            await ApplyActionAsync(action);
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
            if (selectedDates.Count >= 5 || action == BatchAction.RestoreDefaults)
                await backup.CreateBackupAsync();
            await tracker.ApplyBatchAsync(selectedDates, action);
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
        }, currentTheme);
        dialog.ShowDialog(this);
    }

    private void ShowSelectedDayDetails()
    {
        if (selectedDates.Count == 0)
        {
            ShowTodayDetails();
            return;
        }

        var date = selectionAnchor is { } anchor && selectedDates.Contains(anchor)
            ? anchor
            : selectedDates.OrderBy(value => value).Last();
        ShowDayDetails(date);
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
        IReadOnlyCollection<string> connectedSsids = [];
        try { connectedSsids = wlanMonitor.GetCurrentConnections().Select(connection => connection.Ssid).ToArray(); }
        catch (Exception exception) { logger.LogWarning(exception, "Não foi possível listar as conexões WLAN atuais."); }
        using var dialog = new SettingsForm(tracker, viewData.Data.Settings,
            viewData.Data.Networks, backup, logger, RecordNewlyConfiguredNetworksAsync, connectedSsids);
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
        var button = new RoundedButton { Text = text, Width = width, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(44, 64, 87), Cursor = Cursors.Hand, Tag = "action:neutral" };
        button.FlatAppearance.BorderColor = Color.FromArgb(215, 221, 229);
        button.Click += handler;
        return button;
    }

    private static void SetButtonTone(Button button, string tone)
    {
        button.Tag = $"action:{tone}";
        switch (tone)
        {
            case "presence": button.BackColor = Color.FromArgb(226, 247, 234); button.ForeColor = Color.FromArgb(27, 130, 76); break;
            case "plan": button.BackColor = Color.FromArgb(231, 241, 255); button.ForeColor = Color.FromArgb(20, 100, 210); break;
            case "absence": button.BackColor = Color.FromArgb(255, 242, 223); button.ForeColor = Color.FromArgb(177, 111, 0); break;
            default: button.BackColor = Color.White; button.ForeColor = Color.FromArgb(44, 64, 87); break;
        }
        button.FlatAppearance.BorderColor = Color.FromArgb(215, 221, 229);
    }

    private sealed class RoundedButton : Button
    {
        public RoundedButton() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            var radius = Math.Min(9, Math.Max(4, Height / 4));
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            using var fill = new SolidBrush(Enabled ? BackColor : SystemColors.Control);
            var borderColor = Focused ? Color.FromArgb(178, 201, 226) : Color.FromArgb(226, 231, 238);
            using var border = new Pen(borderColor, 1F);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
            var textBounds = Rectangle.Inflate(bounds, -Padding.Horizontal / 2 - 4, -Padding.Vertical / 2);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
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













