using ClosedXML.Excel;
using MooTrack.Derivation;

namespace MooTrack.Reporting;

public static class Workbook
{
    public static void Write(
        string path,
        IReadOnlyList<DayRecord> days,
        IReadOnlyList<WeekRecord> weeks,
        DerivationOptions options)
    {
        using var workbook = new XLWorkbook();

        Sheet(workbook, "Daily", CsvFormat.DailyHeader, days.Select(CsvFormat.Row));
        Sheet(workbook, "Weekly", CsvFormat.WeeklyHeader, weeks.Select(CsvFormat.Row));
        Parameters(workbook, options);

        Replace(path, workbook);
    }

    private static void Sheet(
        XLWorkbook workbook, string name, string header, IEnumerable<string> rows)
    {
        var sheet = workbook.Worksheets.Add(name);
        var columns = header.Split(',');

        for (var c = 0; c < columns.Length; c++)
            sheet.Cell(1, c + 1).Value = columns[c];

        var r = 2;
        foreach (var row in rows)
        {
            var cells = row.Split(',');
            for (var c = 0; c < cells.Length; c++)
            {
                if (Decimal.TryParse(cells[c], out var number) && cells[c].Contains('.'))
                    sheet.Cell(r, c + 1).Value = number;
                else
                    sheet.Cell(r, c + 1).Value = cells[c];
            }
            r++;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
    }

    private static void Parameters(XLWorkbook workbook, DerivationOptions options)
    {
        var sheet = workbook.Worksheets.Add("Method");
        var rows = new (string Name, string Value)[]
        {
            ("BridgeThreshold", $"{options.BridgeThreshold.TotalMinutes:0} min"),
            ("ConfirmationWindow", $"{options.ConfirmationWindow.TotalMinutes:0} min"),
            ("FringeMaxDuration", $"{options.FringeMaxDuration.TotalMinutes:0} min"),
            ("FringeGap", $"{options.FringeGap.TotalMinutes:0} min"),
            ("TickInterval", $"{options.TickInterval.TotalSeconds:0} s"),
            ("GapTolerance", $"{options.GapTolerance.TotalMinutes:0} min"),
            ("BridgeAcrossLock", options.BridgeAcrossLock.ToString()),
            ("Generated", DateTimeOffset.Now.ToString("u")),
        };

        sheet.Cell(1, 1).Value = "Parameter";
        sheet.Cell(1, 2).Value = "Value";

        for (var i = 0; i < rows.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = rows[i].Name;
            sheet.Cell(i + 2, 2).Value = rows[i].Value;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
    }

    // Excel holds an exclusive lock on an open workbook; writing in place would
    // truncate the file and lose the previous run with nothing to replace it.
    private static void Replace(string path, XLWorkbook workbook)
    {
        var temporary = Path.Combine(
            Path.GetDirectoryName(path) ?? ".",
            Path.GetFileNameWithoutExtension(path) + ".tmp.xlsx");
        workbook.SaveAs(temporary);

        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException)
        {
            File.Delete(temporary);
            throw new IOException($"{path} is locked - close it in Excel and run again");
        }
    }
}
