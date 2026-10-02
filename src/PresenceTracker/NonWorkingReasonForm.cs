using PresenceTracker.Application;
using PresenceTracker.Domain;

namespace PresenceTracker;

internal sealed class NonWorkingReasonForm : Form
{
    private readonly ComboBox reason = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };

    public BatchAction? SelectedAction { get; private set; }

    public NonWorkingReasonForm(ThemeMode theme)
    {
        Text = "Classificar dia não trabalhado";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(450, 250);
        MinimumSize = new Size(420, 230);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(root);
        root.Controls.Add(new Label
        {
            Text = "Qual foi o motivo do dia não trabalhado?", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        root.Controls.Add(new Label { Text = "Classificação", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        reason.Items.AddRange(new object[] { "Férias", "Day off", "Feriado", "Não trabalhado" });
        reason.SelectedIndex = 0;
        root.Controls.Add(reason, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = Padding.Empty
        };
        var apply = new Button { Text = "Aplicar", Width = 100, Height = 36, Tag = "primary" };
        apply.Click += (_, _) =>
        {
            SelectedAction = reason.SelectedIndex switch
            {
                0 => BatchAction.Vacation,
                1 => BatchAction.DayOff,
                2 => BatchAction.Holiday,
                _ => BatchAction.NonWorkingDay
            };
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = new Button { Text = "Cancelar", Width = 100, Height = 36, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 3);
        AcceptButton = apply;
        CancelButton = cancel;
        UiTheme.Apply(this, theme);
    }
}
