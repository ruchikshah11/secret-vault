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

    private const int RowHeight = 34;
    private const int RowGap = 8;
    private const int SingleLineHeight = RowHeight * 2 + RowGap;
    private const int MultiLineHeight = RowHeight + RowGap + 72;
    private const int Indent = 28;

    private readonly ComboBox _typeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly IconButton _dragHandle = new(IconKind.DragHandle, 24) { Circular = false, Cursor = Cursors.SizeAll };
    private readonly TextBox _labelBox = new() { PlaceholderText = "Field label" };
    private readonly RoundedField _labelField;
    private readonly IconButton _removeButton = new(IconKind.Trash, 26) { Circular = false, IconColor = AppTheme.Danger };
    private readonly Panel _valueHost = new();

    private TextBox? _textValueBox;
    private RoundedField? _textValueField;
    private DateTimePicker? _dateValueBox;

    // Grip used by the parent form to drag-reorder rows.
    public Control DragHandle => _dragHandle;

    public event EventHandler? RemoveRequested;
    public event EventHandler? SizeAdjusted;

    public CustomFieldRow(string label, string value, CustomFieldType type)
    {
        Height = SingleLineHeight;
        Margin = new Padding(0, 0, 0, 14);
        _labelField = new RoundedField(_labelBox, RowHeight);

        _typeCombo.Items.AddRange(TypeOptions.Select(t => t.Label).Cast<object>().ToArray());
        _typeCombo.SelectedIndexChanged += (_, _) => RebuildValueControl();
        _removeButton.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);

        _labelBox.Text = label;
        _valueHost.Location = new Point(Indent, RowHeight + RowGap);
        _dragHandle.Location = new Point(0, (RowHeight - _dragHandle.Height) / 2);

        Controls.Add(_dragHandle);
        Controls.Add(_labelField);
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
        _removeButton.Location = new Point(w - _removeButton.Width, (RowHeight - _removeButton.Height) / 2);
        _typeCombo.Width = 140;
        _typeCombo.Location = new Point(_removeButton.Left - 6 - _typeCombo.Width, (RowHeight - _typeCombo.Height) / 2);
        _labelField.Location = new Point(Indent, 0);
        _labelField.Width = _typeCombo.Left - 8 - Indent;
        _valueHost.Width = w - Indent - _removeButton.Width - 6;
        _valueHost.Height = Height - RowHeight - RowGap;

        if (_textValueField is not null) _textValueField.Size = _valueHost.Size;
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
            _textValueField = null;
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
                PlaceholderText = "Value"
            };
            _textValueField = new RoundedField(_textValueBox, _valueHost.Height) { Width = _valueHost.Width };
            _valueHost.Controls.Add(_textValueField);
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
