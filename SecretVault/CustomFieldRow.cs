using SecretVault.Models;

namespace SecretVault.Gui;

public class CustomFieldRow : Panel
{
    private static readonly (CustomFieldType Type, string Label)[] TypeOptions =
    {
        (CustomFieldType.Text, "Text"),
        (CustomFieldType.Password, "Hidden Field"),
        (CustomFieldType.Date, "Date"),
        (CustomFieldType.Url, "Website Address"),
        (CustomFieldType.Email, "Email"),
        (CustomFieldType.Phone, "Phone Number"),
        (CustomFieldType.PinCode, "Pin Code"),
        (CustomFieldType.Name, "Name"),
        (CustomFieldType.Address, "Address"),
        (CustomFieldType.MultiLineText, "Multi-line Text"),
    };

    private const int SingleLineHeight = 64;
    private const int MultiLineHeight = 88;

    private readonly ComboBox _typeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly IconButton _dragHandle = new(IconKind.DragHandle, 24) { Circular = false, Enabled = false };
    private readonly TextBox _labelBox = new() { PlaceholderText = "Field label" };
    private readonly IconButton _removeButton = new(IconKind.Trash, 26) { Circular = false, IconColor = AppTheme.Danger };
    private readonly Panel _valueHost = new();

    private TextBox? _textValueBox;
    private DateTimePicker? _dateValueBox;

    public event EventHandler? RemoveRequested;
    public event EventHandler? SizeAdjusted;

    public CustomFieldRow(string label, string value, CustomFieldType type)
    {
        Height = SingleLineHeight;
        Margin = new Padding(0, 0, 0, 8);

        _typeCombo.Items.AddRange(TypeOptions.Select(t => t.Label).Cast<object>().ToArray());
        _typeCombo.SelectedIndexChanged += (_, _) => RebuildValueControl();
        _removeButton.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);

        _labelBox.Text = label;
        _valueHost.Location = new Point(28, 30);
        _dragHandle.Location = new Point(0, 34);

        Controls.Add(_dragHandle);
        Controls.Add(_labelBox);
        Controls.Add(_typeCombo);
        Controls.Add(_removeButton);
        Controls.Add(_valueHost);

        _typeCombo.SelectedIndex = Math.Max(0, Array.FindIndex(TypeOptions, t => t.Type == type));
        SetValueText(value);

        Resize += (_, _) => LayoutRow();
        LayoutRow();
    }

    private void LayoutRow()
    {
        var w = Width;
        _removeButton.Location = new Point(w - _removeButton.Width, 0);
        _typeCombo.Size = new Size(120, 26);
        _typeCombo.Location = new Point(w - _removeButton.Width - 8 - _typeCombo.Width, 0);
        _labelBox.Location = new Point(28, 2);
        _labelBox.Size = new Size(_typeCombo.Left - 8 - 28, 24);
        _valueHost.Width = w - 28;
        _valueHost.Height = Height - 34;

        if (_textValueBox is not null) _textValueBox.Size = _valueHost.Size;
        if (_dateValueBox is not null) _dateValueBox.Width = _valueHost.Width;
    }

    private void RebuildValueControl()
    {
        var current = GetValueText();
        _valueHost.Controls.Clear();

        var isMultiLine = Type is CustomFieldType.Address or CustomFieldType.MultiLineText;
        Height = isMultiLine ? MultiLineHeight : SingleLineHeight;
        LayoutRow();

        if (Type == CustomFieldType.Date)
        {
            _textValueBox = null;
            _dateValueBox = new DateTimePicker { Width = _valueHost.Width, Format = DateTimePickerFormat.Short };
            if (DateTime.TryParse(current, out var parsed))
                _dateValueBox.Value = parsed;
            _valueHost.Controls.Add(_dateValueBox);
        }
        else
        {
            _dateValueBox = null;
            _textValueBox = new TextBox
            {
                Size = _valueHost.Size,
                Text = current,
                Multiline = isMultiLine,
                UseSystemPasswordChar = Type is CustomFieldType.Password or CustomFieldType.PinCode,
                BorderStyle = BorderStyle.FixedSingle
            };
            _valueHost.Controls.Add(_textValueBox);
        }

        AppTheme.Apply(_valueHost);
        SizeAdjusted?.Invoke(this, EventArgs.Empty);
    }

    private void SetValueText(string value)
    {
        RebuildValueControl();
        if (_textValueBox is not null) _textValueBox.Text = value;
        else if (_dateValueBox is not null && DateTime.TryParse(value, out var parsed)) _dateValueBox.Value = parsed;
    }

    public CustomFieldType Type => TypeOptions[_typeCombo.SelectedIndex].Type;

    public string Label => _labelBox.Text;

    public string GetValueText() =>
        _dateValueBox is not null ? _dateValueBox.Value.ToString("MM/dd/yyyy") : _textValueBox?.Text ?? string.Empty;

    public CustomField ToCustomField() => new()
    {
        Label = Label,
        Value = GetValueText(),
        Type = Type
    };
}
