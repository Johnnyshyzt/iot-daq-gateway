using System.Globalization;
using System.Net;
using System.Text;

namespace Studio.Host.Visualization;

internal static class SpreadsheetXml
{
    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\"?>");
        builder.Append("<?mso-application progid=\"Excel.Sheet\"?>");
        builder.Append("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
        builder.Append("<Worksheet ss:Name=\"");
        builder.Append(Escape(TrimSheet(sheetName)));
        builder.Append("\"><Table>");
        builder.Append("<Row>");
        foreach (var header in headers)
        {
            Cell(builder, header, number: false);
        }

        builder.Append("</Row>");
        foreach (var row in rows)
        {
            builder.Append("<Row>");
            foreach (var cell in row)
            {
                var number = double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                Cell(builder, cell, number);
            }

            builder.Append("</Row>");
        }

        builder.Append("</Table></Worksheet></Workbook>");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void Cell(StringBuilder builder, string value, bool number)
    {
        builder.Append(number ? "<Cell><Data ss:Type=\"Number\">" : "<Cell><Data ss:Type=\"String\">");
        builder.Append(Escape(value));
        builder.Append("</Data></Cell>");
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value ?? "");

    private static string TrimSheet(string name)
    {
        var text = string.IsNullOrWhiteSpace(name) ? "Sheet1" : name.Trim();
        return text.Length <= 31 ? text : text[..31];
    }
}
