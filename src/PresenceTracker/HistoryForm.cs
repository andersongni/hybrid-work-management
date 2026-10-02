using PresenceTracker.Application;
using PresenceTracker.Domain;

namespace PresenceTracker;

internal sealed class HistoryForm : Form
{
    public HistoryForm(TrackerService tracker, ThemeMode theme = ThemeMode.System)
    {
        Text = "Histórico mensal";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 540);
        MinimumSize = new Size(540, 420);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "Meses encerrados", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 16, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        list.Columns.Add("Mês", 185);
        list.Columns.Add("Presenças", 105);
        list.Columns.Add("Percentual", 110);
        list.Columns.Add("Meta", 110);
        root.Controls.Add(list, 0, 1);
        var footerPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        var footer = new Label { Dock = DockStyle.Fill, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        var older = new Button { Text = "Meses anteriores…", Dock = DockStyle.Fill };
        footerPanel.Controls.Add(footer, 0, 0);
        footerPanel.Controls.Add(older, 1, 0);
        root.Controls.Add(footerPanel, 0, 2);
        var offset = 1;
        var percentages = new List<decimal>();
        async Task LoadOlderAsync()
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var current = new DateOnly(today.Year, today.Month, 1);
            var end = offset + 11;
            for (var i = offset; i <= end; i++)
            {
                var date = current.AddMonths(-i);
                var data = await tracker.GetMonthAsync(date.Year, date.Month, today);
                if (data.Data.AttendanceEvents.Count == 0 && data.Data.Classifications.Count == 0 && data.Data.Plans.Count == 0)
                    continue;
                var metrics = data.Metrics;
                var monthTitle = date.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", new System.Globalization.CultureInfo("pt-BR"));
                var row = new ListViewItem(new System.Globalization.CultureInfo("pt-BR").TextInfo.ToTitleCase(monthTitle));
                row.SubItems.Add($"{metrics.RealizedDays} / {metrics.EligibleWorkingDays}");
                row.SubItems.Add($"{metrics.MonthlyPercent:0.#}%");
                row.SubItems.Add($"{metrics.TargetPercent:0.#}%");
                list.Items.Add(row);
                percentages.Add(metrics.MonthlyPercent);
            }
            offset = end + 1;
            footer.Text = list.Items.Count == 0 ? "Ainda não há meses encerrados com dados."
                : $"Média simples dos meses exibidos: {PresenceCalculator.SimpleMonthlyAverage(percentages):0.#}%";
        }
        older.Click += async (_, _) => await LoadOlderAsync();
        Load += async (_, _) => await LoadOlderAsync();
        UiTheme.Apply(this, theme);
    }
}





