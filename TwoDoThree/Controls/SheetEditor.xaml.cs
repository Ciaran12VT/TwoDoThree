using System.ComponentModel;
using System.Data;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TwoDoThree.Models;

namespace TwoDoThree.Controls;

public partial class SheetEditor : UserControl
{
    private static readonly IReadOnlyList<(string Name, string Color)> BackgroundColors =
    [
        ("No fill", string.Empty),
        ("Yellow", "#FEF3C7"),
        ("Green", "#DCFCE7"),
        ("Blue", "#DBEAFE"),
        ("Rose", "#FFE4E6"),
        ("Purple", "#F3E8FF"),
        ("Gray", "#E5E7EB")
    ];

    public static readonly DependencyProperty ResourceProperty =
        DependencyProperty.Register(
            nameof(Resource),
            typeof(ResourceItem),
            typeof(SheetEditor),
            new PropertyMetadata(null, OnResourceChanged));

    private DataTable table = new();
    private Dictionary<string, SheetCellFormat> cellFormats = new(StringComparer.Ordinal);
    private bool isUpdatingResource;
    private bool isUpdatingTable;

    public SheetEditor()
    {
        InitializeComponent();
    }

    public ResourceItem? Resource
    {
        get => (ResourceItem?)GetValue(ResourceProperty);
        set => SetValue(ResourceProperty, value);
    }

    private static void OnResourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var editor = (SheetEditor)dependencyObject;

        if (e.OldValue is ResourceItem oldResource)
        {
            oldResource.PropertyChanged -= editor.Resource_PropertyChanged;
        }

        if (e.NewValue is ResourceItem newResource)
        {
            newResource.PropertyChanged += editor.Resource_PropertyChanged;
        }

        editor.RefreshTableFromResource();
    }

    private void Resource_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isUpdatingResource || isUpdatingTable || e.PropertyName != nameof(ResourceItem.Content))
        {
            return;
        }

        Dispatcher.Invoke(RefreshTableFromResource);
    }

    private void RefreshTableFromResource()
    {
        isUpdatingTable = true;
        DetachTableEvents();

        try
        {
            var data = SheetResourceSerializer.Deserialize(Resource?.Content);
            table = CreateTable(data);
            cellFormats = data.CellFormats.ToDictionary(
                pair => pair.Key,
                pair => CloneFormat(pair.Value),
                StringComparer.Ordinal);
            AttachTableEvents();
            SheetGrid.ItemsSource = table.DefaultView;
            Dispatcher.BeginInvoke(RefreshVisibleCellFormatting, DispatcherPriority.Background);
        }
        finally
        {
            isUpdatingTable = false;
        }
    }

    private void AddRowButton_Click(object sender, RoutedEventArgs e)
    {
        var row = table.NewRow();
        table.Rows.Add(row);
        SyncResourceFromTable();
    }

    private void AddColumnButton_Click(object sender, RoutedEventArgs e)
    {
        table.Columns.Add(GetNextColumnName(), typeof(string));
        RefreshGridColumns();
        SyncResourceFromTable();
    }

    private void DeleteRowButton_Click(object sender, RoutedEventArgs e)
    {
        var rowIndexes = SheetGrid.SelectedItems
            .OfType<DataRowView>()
            .Select(rowView => rowView.Row)
            .Where(row => row.RowState != DataRowState.Detached && row.RowState != DataRowState.Deleted)
            .Select(row => table.Rows.IndexOf(row))
            .Where(rowIndex => rowIndex >= 0)
            .Distinct()
            .OrderByDescending(rowIndex => rowIndex)
            .ToList();

        foreach (var rowIndex in rowIndexes)
        {
            table.Rows.RemoveAt(rowIndex);
        }

        RemoveCellFormatRows(rowIndexes);
        SyncResourceFromTable();
    }

    private void DeleteColumnButton_Click(object sender, RoutedEventArgs e)
    {
        if (table.Columns.Count <= 1)
        {
            return;
        }

        var columnName = SheetGrid.CurrentColumn?.SortMemberPath;
        if (string.IsNullOrWhiteSpace(columnName) || !table.Columns.Contains(columnName))
        {
            columnName = table.Columns[table.Columns.Count - 1].ColumnName;
        }

        var columnIndex = table.Columns.IndexOf(columnName);
        table.Columns.Remove(columnName);
        RemoveCellFormatColumn(columnIndex);
        RefreshGridColumns();
        SyncResourceFromTable();
    }

    private void SheetGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.MinWidth = 90;
        e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        e.Column.CellStyle = CreateCellStyle();

        if (e.Column is DataGridTextColumn textColumn)
        {
            textColumn.ElementStyle = CreateTextBlockStyle();
            textColumn.EditingElementStyle = CreateTextBoxStyle();
        }
    }

    private void SheetGrid_CurrentCellChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(SyncResourceFromTable, DispatcherPriority.Background);
    }

    private void SheetGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.C && TryCopySelectionToClipboard())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.V && TryPasteClipboardIntoGrid())
        {
            e.Handled = true;
        }
    }

    private void SheetGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(SyncResourceFromTable, DispatcherPriority.Background);
    }

    private void CellBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = button };
        foreach (var (name, color) in BackgroundColors)
        {
            var item = new MenuItem
            {
                Header = CreateBackgroundMenuHeader(name, color),
                Tag = color
            };
            item.Click += (_, _) => SetSelectedCellBackground(color);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void WrapCellsButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateSelectedCellFormats(format => format.Wrap = true);
    }

    private void BoldCellsButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedCells = GetSelectedGridCells();
        var shouldEnable = selectedCells.Any(cell => !GetCellFormat(cell.RowIndex, cell.ColumnIndex).Bold);
        UpdateSelectedCellFormats(format => format.Bold = shouldEnable, selectedCells);
    }

    private void ItalicCellsButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedCells = GetSelectedGridCells();
        var shouldEnable = selectedCells.Any(cell => !GetCellFormat(cell.RowIndex, cell.ColumnIndex).Italic);
        UpdateSelectedCellFormats(format => format.Italic = shouldEnable, selectedCells);
    }

    private void Table_Changed(object sender, EventArgs e)
    {
        SyncResourceFromTable();
    }

    private void SyncResourceFromTable()
    {
        if (isUpdatingTable || Resource is null)
        {
            return;
        }

        isUpdatingResource = true;

        try
        {
            Resource.Content = CreateContentFromTable();
        }
        finally
        {
            isUpdatingResource = false;
        }
    }

    private string CreateContentFromTable()
    {
        var data = new SheetResourceData
        {
            Columns = table.Columns.Cast<DataColumn>()
                .Select(column => column.ColumnName)
                .ToList(),
            CellFormats = GetValidCellFormats()
        };

        foreach (DataRow row in table.Rows)
        {
            if (row.RowState == DataRowState.Deleted)
            {
                continue;
            }

            data.Rows.Add(table.Columns.Cast<DataColumn>()
                .Select(column => row[column]?.ToString() ?? string.Empty)
                .ToList());
        }

        return SheetResourceSerializer.Serialize(data);
    }

    private static DataTable CreateTable(SheetResourceData data)
    {
        var dataTable = new DataTable();

        foreach (var column in data.Columns)
        {
            dataTable.Columns.Add(column, typeof(string));
        }

        foreach (var sourceRow in data.Rows)
        {
            var row = dataTable.NewRow();
            for (var columnIndex = 0; columnIndex < dataTable.Columns.Count; columnIndex++)
            {
                row[columnIndex] = columnIndex < sourceRow.Count ? sourceRow[columnIndex] : string.Empty;
            }

            dataTable.Rows.Add(row);
        }

        return dataTable;
    }

    private string GetNextColumnName()
    {
        var index = table.Columns.Count;
        string columnName;

        do
        {
            columnName = SheetResourceSerializer.GetColumnName(index++);
        }
        while (table.Columns.Contains(columnName));

        return columnName;
    }

    private void RefreshGridColumns()
    {
        SheetGrid.ItemsSource = null;
        SheetGrid.ItemsSource = table.DefaultView;
        Dispatcher.BeginInvoke(RefreshVisibleCellFormatting, DispatcherPriority.Background);
    }

    private bool TryPasteClipboardIntoGrid()
    {
        if (!Clipboard.ContainsText())
        {
            return false;
        }

        var clipboardText = Clipboard.GetText(TextDataFormat.UnicodeText);
        if (string.IsNullOrEmpty(clipboardText))
        {
            clipboardText = Clipboard.GetText();
        }

        var values = ParseClipboardGrid(clipboardText);
        if (values.Count == 0)
        {
            return false;
        }

        SheetGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SheetGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var startRow = GetPasteStartRowIndex();
        var startColumn = GetPasteStartColumnIndex();
        var requiredColumnCount = startColumn + values.Max(row => row.Count);
        var requiredRowCount = startRow + values.Count;

        DetachTableEvents();

        try
        {
            while (table.Columns.Count < requiredColumnCount)
            {
                table.Columns.Add(GetNextColumnName(), typeof(string));
            }

            while (table.Rows.Count < requiredRowCount)
            {
                table.Rows.Add(table.NewRow());
            }

            for (var rowIndex = 0; rowIndex < values.Count; rowIndex++)
            {
                var sourceRow = values[rowIndex];
                var targetRow = table.Rows[startRow + rowIndex];

                for (var columnIndex = 0; columnIndex < sourceRow.Count; columnIndex++)
                {
                    targetRow[startColumn + columnIndex] = sourceRow[columnIndex];
                }
            }
        }
        finally
        {
            AttachTableEvents();
        }

        RefreshGridColumns();
        SyncResourceFromTable();
        SelectPastedCell(startRow, startColumn);
        return true;
    }

    private void SetSelectedCellBackground(string color)
    {
        UpdateSelectedCellFormats(format => format.Background = color);
    }

    private void UpdateSelectedCellFormats(Action<SheetCellFormat> update)
    {
        UpdateSelectedCellFormats(update, GetSelectedGridCells());
    }

    private void UpdateSelectedCellFormats(Action<SheetCellFormat> update, IReadOnlyList<(int RowIndex, int ColumnIndex)> selectedCells)
    {
        if (selectedCells.Count == 0)
        {
            return;
        }

        SheetGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SheetGrid.CommitEdit(DataGridEditingUnit.Row, true);

        foreach (var (rowIndex, columnIndex) in selectedCells)
        {
            if (rowIndex < 0 || rowIndex >= table.Rows.Count || columnIndex < 0 || columnIndex >= table.Columns.Count)
            {
                continue;
            }

            var key = GetCellFormatKey(rowIndex, columnIndex);
            if (!cellFormats.TryGetValue(key, out var format))
            {
                format = new SheetCellFormat();
                cellFormats[key] = format;
            }

            update(format);
            if (IsEmpty(format))
            {
                cellFormats.Remove(key);
            }
        }

        SyncResourceFromTable();
        RefreshVisibleCellFormatting();
    }

    private bool TryCopySelectionToClipboard()
    {
        var selectedCells = GetSelectedGridCells();
        if (selectedCells.Count == 0)
        {
            return false;
        }

        var minRowIndex = selectedCells.Min(cell => cell.RowIndex);
        var maxRowIndex = selectedCells.Max(cell => cell.RowIndex);
        var minColumnIndex = selectedCells.Min(cell => cell.ColumnIndex);
        var maxColumnIndex = selectedCells.Max(cell => cell.ColumnIndex);

        var tabDelimitedText = CreateDelimitedSelectionText(minRowIndex, maxRowIndex, minColumnIndex, maxColumnIndex, '\t');
        var csvText = CreateDelimitedSelectionText(minRowIndex, maxRowIndex, minColumnIndex, maxColumnIndex, ',');
        var dataObject = new DataObject();
        dataObject.SetText(tabDelimitedText, TextDataFormat.UnicodeText);
        dataObject.SetText(tabDelimitedText, TextDataFormat.Text);
        dataObject.SetData(DataFormats.CommaSeparatedValue, csvText);
        Clipboard.SetDataObject(dataObject, true);
        return true;
    }

    private List<(int RowIndex, int ColumnIndex)> GetSelectedGridCells()
    {
        var selectedCells = SheetGrid.SelectedCells
            .Select(TryGetGridCellCoordinates)
            .Where(cell => cell.HasValue)
            .Select(cell => cell!.Value)
            .Distinct()
            .ToList();

        if (selectedCells.Count > 0)
        {
            return selectedCells;
        }

        var currentCell = TryGetGridCellCoordinates(SheetGrid.CurrentCell);
        return currentCell.HasValue
            ? new List<(int RowIndex, int ColumnIndex)> { currentCell.Value }
            : new List<(int RowIndex, int ColumnIndex)>();
    }

    private SheetCellFormat GetCellFormat(int rowIndex, int columnIndex)
    {
        return cellFormats.TryGetValue(GetCellFormatKey(rowIndex, columnIndex), out var format)
            ? format
            : new SheetCellFormat();
    }

    private (int RowIndex, int ColumnIndex)? TryGetGridCellCoordinates(DataGridCellInfo cell)
    {
        if (cell.Item is not DataRowView rowView)
        {
            return null;
        }

        var rowIndex = table.Rows.IndexOf(rowView.Row);
        var columnName = cell.Column?.SortMemberPath;
        if (rowIndex < 0 || string.IsNullOrWhiteSpace(columnName) || !table.Columns.Contains(columnName))
        {
            return null;
        }

        var columnIndex = table.Columns.IndexOf(columnName);
        return columnIndex >= 0
            ? (rowIndex, columnIndex)
            : null;
    }

    private string CreateDelimitedSelectionText(
        int minRowIndex,
        int maxRowIndex,
        int minColumnIndex,
        int maxColumnIndex,
        char delimiter)
    {
        var builder = new StringBuilder();

        for (var rowIndex = minRowIndex; rowIndex <= maxRowIndex; rowIndex++)
        {
            if (rowIndex > minRowIndex)
            {
                builder.AppendLine();
            }

            for (var columnIndex = minColumnIndex; columnIndex <= maxColumnIndex; columnIndex++)
            {
                if (columnIndex > minColumnIndex)
                {
                    builder.Append(delimiter);
                }

                var value = table.Rows[rowIndex][columnIndex]?.ToString() ?? string.Empty;
                builder.Append(EscapeDelimitedValue(value, delimiter));
            }
        }

        return builder.ToString();
    }

    private static string EscapeDelimitedValue(string value, char delimiter)
    {
        return value.Contains('"', StringComparison.Ordinal)
               || value.Contains(delimiter, StringComparison.Ordinal)
               || value.Contains('\n', StringComparison.Ordinal)
               || value.Contains('\r', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private int GetPasteStartRowIndex()
    {
        if (SheetGrid.CurrentItem is DataRowView currentRowView)
        {
            var currentRowIndex = table.Rows.IndexOf(currentRowView.Row);
            if (currentRowIndex >= 0)
            {
                return currentRowIndex;
            }
        }

        var selectedRowView = SheetGrid.SelectedCells
            .Select(cell => cell.Item)
            .OfType<DataRowView>()
            .FirstOrDefault();

        if (selectedRowView is not null)
        {
            var selectedRowIndex = table.Rows.IndexOf(selectedRowView.Row);
            if (selectedRowIndex >= 0)
            {
                return selectedRowIndex;
            }
        }

        return SheetGrid.SelectedIndex >= 0
            ? Math.Min(SheetGrid.SelectedIndex, table.Rows.Count)
            : 0;
    }

    private int GetPasteStartColumnIndex()
    {
        var columnName = SheetGrid.CurrentColumn?.SortMemberPath;
        if (!string.IsNullOrWhiteSpace(columnName) && table.Columns.Contains(columnName))
        {
            return table.Columns.IndexOf(columnName);
        }

        var selectedColumnName = SheetGrid.SelectedCells
            .Select(cell => cell.Column?.SortMemberPath)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) && table.Columns.Contains(name));

        return selectedColumnName is not null
            ? table.Columns.IndexOf(selectedColumnName)
            : 0;
    }

    private void SelectPastedCell(int rowIndex, int columnIndex)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (rowIndex < 0
                || rowIndex >= table.DefaultView.Count
                || columnIndex < 0
                || columnIndex >= table.Columns.Count)
            {
                return;
            }

            var rowView = table.DefaultView[rowIndex];
            var columnName = table.Columns[columnIndex].ColumnName;
            var column = SheetGrid.Columns.FirstOrDefault(gridColumn => gridColumn.SortMemberPath == columnName);
            if (column is null)
            {
                return;
            }

            SheetGrid.SelectedCells.Clear();
            SheetGrid.CurrentCell = new DataGridCellInfo(rowView, column);
            SheetGrid.SelectedCells.Add(SheetGrid.CurrentCell);
            SheetGrid.ScrollIntoView(rowView, column);
        }, DispatcherPriority.Background);
    }

    private Dictionary<string, SheetCellFormat> GetValidCellFormats()
    {
        return cellFormats
            .Where(pair => TryParseCellFormatKey(pair.Key, out var rowIndex, out var columnIndex)
                           && rowIndex >= 0
                           && rowIndex < table.Rows.Count
                           && columnIndex >= 0
                           && columnIndex < table.Columns.Count
                           && !IsEmpty(pair.Value))
            .ToDictionary(
                pair => pair.Key,
                pair => CloneFormat(pair.Value),
                StringComparer.Ordinal);
    }

    private void RemoveCellFormatRows(IReadOnlyCollection<int> removedRowIndexes)
    {
        if (removedRowIndexes.Count == 0)
        {
            return;
        }

        var removedRows = removedRowIndexes.ToHashSet();
        var sortedRows = removedRows.OrderBy(rowIndex => rowIndex).ToList();
        var shiftedFormats = new Dictionary<string, SheetCellFormat>(StringComparer.Ordinal);
        foreach (var (key, format) in cellFormats)
        {
            if (!TryParseCellFormatKey(key, out var rowIndex, out var columnIndex) || removedRows.Contains(rowIndex))
            {
                continue;
            }

            var shift = sortedRows.Count(removedRowIndex => removedRowIndex < rowIndex);
            shiftedFormats[GetCellFormatKey(rowIndex - shift, columnIndex)] = format;
        }

        cellFormats = shiftedFormats;
    }

    private void RemoveCellFormatColumn(int removedColumnIndex)
    {
        if (removedColumnIndex < 0)
        {
            return;
        }

        var shiftedFormats = new Dictionary<string, SheetCellFormat>(StringComparer.Ordinal);
        foreach (var (key, format) in cellFormats)
        {
            if (!TryParseCellFormatKey(key, out var rowIndex, out var columnIndex)
                || columnIndex == removedColumnIndex)
            {
                continue;
            }

            var shiftedColumnIndex = columnIndex > removedColumnIndex
                ? columnIndex - 1
                : columnIndex;
            shiftedFormats[GetCellFormatKey(rowIndex, shiftedColumnIndex)] = format;
        }

        cellFormats = shiftedFormats;
    }

    private void RefreshVisibleCellFormatting()
    {
        foreach (var cell in FindVisualChildren<DataGridCell>(SheetGrid))
        {
            ApplyFormatToCell(cell);
        }
    }

    private void SheetGridCell_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGridCell cell)
        {
            ApplyFormatToCell(cell);
        }
    }

    private void SheetCellTextElement_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject dependencyObject
            && FindAncestor<DataGridCell>(dependencyObject) is { } cell)
        {
            ApplyFormatToCell(cell);
        }
    }

    private void ApplyFormatToCell(DataGridCell cell)
    {
        if (TryGetGridCellCoordinates(cell) is not { } coordinates)
        {
            return;
        }

        var format = GetCellFormat(coordinates.RowIndex, coordinates.ColumnIndex);
        if (string.IsNullOrWhiteSpace(format.Background))
        {
            cell.ClearValue(Control.BackgroundProperty);
        }
        else
        {
            cell.Background = CreateBrush(format.Background);
        }

        cell.FontWeight = format.Bold ? FontWeights.Bold : FontWeights.Normal;
        cell.FontStyle = format.Italic ? FontStyles.Italic : FontStyles.Normal;
        ApplyTextWrapping(cell, format.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap);
    }

    private Style CreateCellStyle()
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new EventSetter(FrameworkElement.LoadedEvent, new RoutedEventHandler(SheetGridCell_Loaded)));
        return style;
    }

    private Style CreateTextBlockStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
        style.Setters.Add(new EventSetter(FrameworkElement.LoadedEvent, new RoutedEventHandler(SheetCellTextElement_Loaded)));
        return style;
    }

    private Style CreateTextBoxStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.NoWrap));
        style.Setters.Add(new EventSetter(FrameworkElement.LoadedEvent, new RoutedEventHandler(SheetCellTextElement_Loaded)));
        return style;
    }

    private static StackPanel CreateBackgroundMenuHeader(string name, string color)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 8, 0),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = string.IsNullOrWhiteSpace(color) ? Brushes.Transparent : CreateBrush(color)
        });
        panel.Children.Add(new TextBlock { Text = name });
        return panel;
    }

    private static void ApplyTextWrapping(DependencyObject parent, TextWrapping wrapping)
    {
        foreach (var textBlock in FindVisualChildren<TextBlock>(parent))
        {
            textBlock.TextWrapping = wrapping;
        }

        foreach (var textBox in FindVisualChildren<TextBox>(parent))
        {
            textBox.TextWrapping = wrapping;
        }
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        try
        {
            return ColorConverter.ConvertFromString(color) is Color parsedColor
                ? new SolidColorBrush(parsedColor)
                : Brushes.Transparent;
        }
        catch (FormatException)
        {
            return Brushes.Transparent;
        }
    }

    private static string GetCellFormatKey(int rowIndex, int columnIndex)
    {
        return $"{rowIndex},{columnIndex}";
    }

    private static bool TryParseCellFormatKey(string key, out int rowIndex, out int columnIndex)
    {
        rowIndex = -1;
        columnIndex = -1;
        var parts = key.Split(',');
        return parts.Length == 2
               && int.TryParse(parts[0], out rowIndex)
               && int.TryParse(parts[1], out columnIndex);
    }

    private static SheetCellFormat CloneFormat(SheetCellFormat format)
    {
        return new SheetCellFormat
        {
            Background = format.Background,
            Wrap = format.Wrap,
            Bold = format.Bold,
            Italic = format.Italic
        };
    }

    private static bool IsEmpty(SheetCellFormat format)
    {
        return string.IsNullOrWhiteSpace(format.Background)
               && !format.Wrap
               && !format.Bold
               && !format.Italic;
    }

    private (int RowIndex, int ColumnIndex)? TryGetGridCellCoordinates(DataGridCell cell)
    {
        if (cell.DataContext is not DataRowView rowView)
        {
            return null;
        }

        var rowIndex = table.Rows.IndexOf(rowView.Row);
        var columnName = cell.Column?.SortMemberPath;
        if (rowIndex < 0 || string.IsNullOrWhiteSpace(columnName) || !table.Columns.Contains(columnName))
        {
            return null;
        }

        var columnIndex = table.Columns.IndexOf(columnName);
        return columnIndex >= 0
            ? (rowIndex, columnIndex)
            : null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static List<List<string>> ParseClipboardGrid(string? clipboardText)
    {
        if (string.IsNullOrEmpty(clipboardText))
        {
            return new List<List<string>>();
        }

        var delimiter = clipboardText.Contains('\t', StringComparison.Ordinal) ? '\t' : ',';
        return ParseDelimitedText(clipboardText, delimiter);
    }

    private static List<List<string>> ParseDelimitedText(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (character == '"')
            {
                if (inQuotes && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == delimiter && !inQuotes)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if ((character == '\r' || character == '\n') && !inQuotes)
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(field.ToString());
                rows.Add(row);
                row = new List<string>();
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        if (field.Length > 0 || row.Count > 0 || text.EndsWith(delimiter))
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private void AttachTableEvents()
    {
        table.ColumnChanged += Table_Changed;
        table.RowChanged += Table_Changed;
        table.RowDeleted += Table_Changed;
    }

    private void DetachTableEvents()
    {
        table.ColumnChanged -= Table_Changed;
        table.RowChanged -= Table_Changed;
        table.RowDeleted -= Table_Changed;
    }
}
