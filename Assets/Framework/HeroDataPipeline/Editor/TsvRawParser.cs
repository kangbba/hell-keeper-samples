using System;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Parses the hero balance sheet into untyped rows, keyed by hero type and star level.
///
/// Every column is kept as authored, so a designer can add one without touching
/// this parser; naming it in the generated data class is what makes it typed.
/// Cell types are inferred per value (int, then float, then string).
/// </summary>
public static class TsvRawParser
{
    /// <summary>
    /// Sheet layout: a `[HeroType]` marker in column A opens a block, the rest of
    /// that row is the column header, and the rows below it are star levels until
    /// column A stops being a number.
    /// </summary>
    public static void ParseToRawData(
        string tsv,
        out Dictionary<HeroType, Dictionary<int, Dictionary<string, object>>> output)
    {
        output = new Dictionary<HeroType, Dictionary<int, Dictionary<string, object>>>();

        var lines = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            Log.e("[TsvRawParser] Sheet has no data rows.");
            return;
        }

        for (int row = 0; row < lines.Length; row++)
        {
            var cells = lines[row].Trim().Split('\t');
            if (cells.Length < 2) continue;

            // A block opens with [HeroType] in column A.
            string firstCell = cells[0].Trim();
            if (!firstCell.StartsWith("[") || !firstCell.EndsWith("]")) continue;

            string heroTypeName = firstCell.Substring(1, firstCell.Length - 2);
            if (!Enum.TryParse(heroTypeName, out HeroType type))
            {
                Log.w($"[TsvRawParser] Unknown HeroType: {heroTypeName}");
                continue;
            }

            var headerCells = cells;
            var columnNames = new List<string>();

            // Column A holds the block marker and star levels, so headers start at B.
            for (int col = 1; col < headerCells.Length; col++)
            {
                string colName = headerCells[col].Trim();
                if (!string.IsNullOrEmpty(colName))
                    columnNames.Add(colName);
            }

            if (!output.ContainsKey(type))
                output[type] = new Dictionary<int, Dictionary<string, object>>();

            int dataRow = row + 1;
            while (dataRow < lines.Length)
            {
                var dataCells = lines[dataRow].Trim().Split('\t');
                if (dataCells.Length < 2) break;

                // The block ends at the first row whose column A is not a star level.
                string starLevelStr = dataCells[0].Trim();
                if (!int.TryParse(starLevelStr, out int starLevel))
                    break;

                var rowData = new Dictionary<string, object>();

                for (int col = 0; col < columnNames.Count && col + 1 < dataCells.Length; col++)
                {
                    string columnName = columnNames[col];
                    string value = dataCells[col + 1].Trim();

                    object parsedValue = ParseValue(value);
                    rowData[columnName] = parsedValue;
                }

                output[type][starLevel] = rowData;
                dataRow++;
            }
        }

        Log.d($"[TsvRawParser] Parsed {output.Count} hero types.");
    }

    /// <summary>
    /// Narrowest type that round-trips the cell: int, then float, then string.
    /// </summary>
    private static object ParseValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
            return intVal;

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatVal))
            return floatVal;

        return value;
    }
}
