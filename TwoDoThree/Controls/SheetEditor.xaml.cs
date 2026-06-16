using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TwoDoThree.Models;

namespace TwoDoThree.Controls;

public partial class SheetEditor : UserControl
{
    public static readonly DependencyProperty ResourceProperty =
        DependencyProperty.Register(
            nameof(Resource),
            typeof(ResourceItem),
            typeof(SheetEditor),
            new PropertyMetadata(null, OnResourceChanged));

    private DataTable table = new();
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
            table = CreateTable(Resource?.Content);
            AttachTableEvents();
            SheetGrid.ItemsSource = table.DefaultView;
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
        var rows = SheetGrid.SelectedItems
            .OfType<DataRowView>()
            .Select(rowView => rowView.Row)
            .Where(row => row.RowState != DataRowState.Detached && row.RowState != DataRowState.Deleted)
            .Distinct()
            .ToList();

        foreach (var row in rows)
        {
            table.Rows.Remove(row);
        }

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

        table.Columns.Remove(columnName);
        RefreshGridColumns();
        SyncResourceFromTable();
    }

    private void SheetGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.MinWidth = 90;
        e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
    }

    private void SheetGrid_CurrentCellChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(SyncResourceFromTable, DispatcherPriority.Background);
    }

    private void SheetGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(SyncResourceFromTable, DispatcherPriority.Background);
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
                .ToList()
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

    private static DataTable CreateTable(string? content)
    {
        var data = SheetResourceSerializer.Deserialize(content);
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
