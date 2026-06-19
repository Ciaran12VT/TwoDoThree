using System.Text;
using System.Text.Json;

namespace TwoDoThree.Models;

public sealed class SheetResourceData
{
    public List<string> Columns { get; set; } = new();

    public List<List<string>> Rows { get; set; } = new();

    public Dictionary<string, SheetCellFormat> CellFormats { get; set; } = new();
}

public sealed class SheetCellFormat
{
    public string Background { get; set; } = string.Empty;

    public bool Wrap { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }
}

public static class SheetResourceSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string CreateDefaultContent()
    {
        return Serialize(CreateDefaultData());
    }

    public static SheetResourceData Deserialize(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return CreateDefaultData();
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<SheetResourceData>(content, JsonOptions));
        }
        catch (JsonException)
        {
            return CreateDefaultData();
        }
    }

    public static string Serialize(SheetResourceData data)
    {
        return JsonSerializer.Serialize(Normalize(data), JsonOptions);
    }

    public static string ToCsv(string? content)
    {
        var data = Deserialize(content);
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", data.Columns.Select(EscapeCsvValue)));

        foreach (var row in data.Rows)
        {
            builder.AppendLine(string.Join(",", row.Select(EscapeCsvValue)));
        }

        return builder.ToString();
    }

    public static string GetColumnName(int zeroBasedIndex)
    {
        var index = Math.Max(0, zeroBasedIndex);
        var name = string.Empty;

        do
        {
            name = (char)('A' + index % 26) + name;
            index = index / 26 - 1;
        }
        while (index >= 0);

        return name;
    }

    private static SheetResourceData CreateDefaultData()
    {
        return new SheetResourceData
        {
            Columns = Enumerable.Range(0, 5).Select(GetColumnName).ToList(),
            Rows = Enumerable.Range(0, 12)
                .Select(_ => Enumerable.Repeat(string.Empty, 5).ToList())
                .ToList()
        };
    }

    private static SheetResourceData Normalize(SheetResourceData? data)
    {
        data ??= CreateDefaultData();

        if (data.Columns.Count == 0)
        {
            data.Columns.Add(GetColumnName(0));
        }

        var usedColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var columnIndex = 0; columnIndex < data.Columns.Count; columnIndex++)
        {
            var columnName = string.IsNullOrWhiteSpace(data.Columns[columnIndex])
                ? GetColumnName(columnIndex)
                : data.Columns[columnIndex].Trim();

            var baseName = columnName;
            var suffix = 2;
            while (!usedColumnNames.Add(columnName))
            {
                columnName = $"{baseName} {suffix++}";
            }

            data.Columns[columnIndex] = columnName;
        }

        foreach (var row in data.Rows)
        {
            while (row.Count < data.Columns.Count)
            {
                row.Add(string.Empty);
            }

            if (row.Count > data.Columns.Count)
            {
                row.RemoveRange(data.Columns.Count, row.Count - data.Columns.Count);
            }
        }

        data.CellFormats ??= new Dictionary<string, SheetCellFormat>();
        foreach (var (key, format) in data.CellFormats.ToList())
        {
            if (format is null
                || !TryParseCellFormatKey(key, out var rowIndex, out var columnIndex)
                || rowIndex < 0
                || rowIndex >= data.Rows.Count
                || columnIndex < 0
                || columnIndex >= data.Columns.Count
                || IsEmpty(format))
            {
                data.CellFormats.Remove(key);
                continue;
            }

            format.Background = format.Background?.Trim() ?? string.Empty;
        }

        return data;
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

    private static bool IsEmpty(SheetCellFormat format)
    {
        return string.IsNullOrWhiteSpace(format.Background)
               && !format.Wrap
               && !format.Bold
               && !format.Italic;
    }

    private static string EscapeCsvValue(string? value)
    {
        value ??= string.Empty;
        return value.Contains('"', StringComparison.Ordinal)
               || value.Contains(',', StringComparison.Ordinal)
               || value.Contains('\n', StringComparison.Ordinal)
               || value.Contains('\r', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }
}
