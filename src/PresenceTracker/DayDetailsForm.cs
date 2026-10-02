using System.Globalization;
using PresenceTracker.Application;
using PresenceTracker.Domain;

namespace PresenceTracker;

internal sealed class DayDetailsForm : Form
{
    public DayDetailsForm(DayViewData day, Func<long, AttendanceStatus, Task> updateStatus, ThemeMode theme = ThemeMode.System)
    {
        var outcome = day.Outcome;
        Text = outcome.Date.ToDateTime(TimeOnly.MinValue).ToString("dddd, dd 'de' MMMM 'de' yyyy", new CultureInfo("pt-BR"));
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 520);
        MinimumSize = new Size(560, 420);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(root);
        var result = outcome.CountsAsPresence ? "Contabilizado" : $"Não contabilizado: {outcome.Reason}";
        root.Controls.Add(new Label { Text = $"{outcome.Reason}\\n{result}", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);

        var attendanceRows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        attendanceRows.SizeChanged += (_, _) => ResizeAttendanceRows(attendanceRows);
        foreach (var item in day.AttendanceEvents.OrderBy(e => e.OccurredAt))
        {
            var row = new Panel { Width = 560, Height = 42 };
            var label = new Label
            {
                Text = $"{(item.Source == AttendanceSource.Automatic ? "Presencial automático" : "Presencial manual")} · {item.OccurredAt.ToLocalTime():HH:mm:ss} · {(item.Status == AttendanceStatus.Active ? "Ativo" : "Excluído manualmente")}",
                Location = new Point(4, 8), Width = 415, Height = 26, TextAlign = ContentAlignment.MiddleLeft
            };
            row.Controls.Add(label);
            if (item.Source == AttendanceSource.Automatic)
            {
                var action = new Button { Text = item.Status == AttendanceStatus.Active ? "Excluir" : "Restaurar", Location = new Point(430, 5), Width = 110, Height = 30 };
                action.Click += async (_, _) =>
                {
                    await updateStatus(item.Id, item.Status == AttendanceStatus.Active ? AttendanceStatus.Excluded : AttendanceStatus.Active);
                    Close();
                };
                row.Controls.Add(action);
            }
            attendanceRows.Controls.Add(row);
        }
        if (day.AttendanceEvents.Count == 0)
            attendanceRows.Controls.Add(new Label { Text = "Nenhuma presença registrada.", AutoSize = true });
        var attendanceBox = new GroupBox { Text = "Presenças · manual/automática", Dock = DockStyle.Fill, Padding = new Padding(8) };
        attendanceBox.Controls.Add(attendanceRows);
        root.Controls.Add(attendanceBox, 0, 1);

        var networkList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        networkList.Columns.Add("Horário", 100);
        networkList.Columns.Add("SSID", 140);
        networkList.Columns.Add("Evento", 110);
        networkList.Columns.Add("Interface", 180);
        networkList.SizeChanged += (_, _) => ResizeNetworkColumns(networkList);
        foreach (var item in day.NetworkEvents)
        {
            var row = new ListViewItem(item.OccurredAt.ToLocalTime().ToString("HH:mm:ss"));
            row.SubItems.Add(item.Ssid ?? "—");
            row.SubItems.Add(item.Type == NetworkEventType.Connected ? "Conectado" : "Desconectado");
            row.SubItems.Add(item.InterfaceName);
            networkList.Items.Add(row);
        }
        var networkBox = new GroupBox { Text = "Eventos de rede", Dock = DockStyle.Fill, Padding = new Padding(8) };
        networkBox.Controls.Add(networkList);
        root.Controls.Add(networkBox, 0, 2);

        var footer = new Label
        {
            Text = $"Planejamento: {(outcome.IsPlanned ? "sim" : "não")}    ·    Classificação: {outcome.Absence?.ToString() ?? "nenhuma"}",
            Dock = DockStyle.Fill, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(80, 92, 107)
        };
        root.Controls.Add(footer, 0, 3);
        ResizeAttendanceRows(attendanceRows);
        ResizeNetworkColumns(networkList);
        UiTheme.Apply(this, theme);
    }

    private static void ResizeAttendanceRows(FlowLayoutPanel rows)
    {
        var rowWidth = Math.Max(300, rows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (var row in rows.Controls.OfType<Panel>())
        {
            row.Width = rowWidth;
            var action = row.Controls.OfType<Button>().FirstOrDefault();
            var label = row.Controls.OfType<Label>().FirstOrDefault();
            if (action is null)
            {
                if (label is not null) label.Width = Math.Max(100, rowWidth - 8);
                continue;
            }

            action.Location = new Point(rowWidth - action.Width - 4, 5);
            if (label is not null)
                label.Width = Math.Max(100, action.Left - 12);
        }
    }

    private static void ResizeNetworkColumns(ListView list)
    {
        if (list.Columns.Count != 4) return;
        var timeWidth = 90;
        var eventWidth = 100;
        var available = Math.Max(170, list.ClientSize.Width - timeWidth - eventWidth - SystemInformation.VerticalScrollBarWidth - 8);
        var ssidWidth = Math.Min(150, available / 2);
        list.Columns[0].Width = timeWidth;
        list.Columns[1].Width = ssidWidth;
        list.Columns[2].Width = eventWidth;
        list.Columns[3].Width = Math.Max(100, available - ssidWidth);
    }
}







