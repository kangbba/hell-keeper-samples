using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Turns the balance sheet's TSV into hero data objects.
///
/// Sheet layout: a `[HeroType]` marker in column A opens a block, the rest of that
/// row names the columns, and each row below is one star level until column A stops
/// being a number.
///
///   [Rock]      DefaultAttackPower  DefaultAttackInterval  ...
///   1           50                  1.2
///   2           105                 1.2
///   [Ice]       DefaultAttackPower  ...
///
/// Columns bind to constructor parameters by name, so adding a stat means editing the
/// data class and the sheet, never this parser. The cost is that a renamed parameter
/// falls back to a default, which is why every miss is logged and every result is
/// Validate()d.
/// </summary>
public static class HeroDataParser
{
    public static void ParseUniqueCsv(string tsv, Dictionary<HeroType, Dictionary<int, HeroUniqueData>> uniqueDataOut)
    {
        ParseBlocks(
            tsv,
            uniqueDataOut,
            // Fire -> FireUniqueData. A hero type with no matching class is skipped,
            // so the sheet can carry a type the code has not caught up to yet.
            (type, cells, colMap) => (HeroUniqueData)Construct(Type.GetType($"{type}UniqueData"), cells, colMap, type),
            data => data.Validate());
    }

    public static void ParseBaseCsv(string tsv, Dictionary<HeroType, Dictionary<int, HeroBaseData>> baseDataOut)
    {
        ParseBlocks(
            tsv,
            baseDataOut,
            (type, cells, colMap) => (HeroBaseData)Construct(typeof(HeroBaseData), cells, colMap, type),
            data => data.Validate());
    }

    /// <summary>
    /// Walks the block structure once, delegating the per-row object construction.
    /// Both sheets share this layout, so the scan lives here rather than being
    /// copied per data type.
    /// </summary>
    private static void ParseBlocks<T>(
        string tsv,
        Dictionary<HeroType, Dictionary<int, T>> output,
        Func<HeroType, string[], Dictionary<string, int>, T> factory,
        Func<T, bool> isValid) where T : class
    {
        output.Clear();
        var lines = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            Log.e($"[HeroDataParser] {typeof(T).Name}: sheet has no data rows.");
            return;
        }

        for (int row = 0; row < lines.Length; row++)
        {
            var headerCells = lines[row].Trim().Split('\t');
            if (headerCells.Length < 2) continue;

            string firstCell = headerCells[0].Trim();
            if (!firstCell.StartsWith("[") || !firstCell.EndsWith("]")) continue;

            string heroTypeName = firstCell.Substring(1, firstCell.Length - 2);
            if (!Enum.TryParse(heroTypeName, out HeroType type))
            {
                Log.w($"[HeroDataParser] Unknown HeroType: {heroTypeName}");
                continue;
            }

            // Column A holds the block marker and star levels, so names start at B.
            var colMap = new Dictionary<string, int>();
            for (int col = 1; col < headerCells.Length; col++)
            {
                string key = headerCells[col].Trim();
                if (!string.IsNullOrEmpty(key))
                    colMap[key] = col;
            }

            if (!output.ContainsKey(type))
                output[type] = new Dictionary<int, T>();

            for (int dataRow = row + 1; dataRow < lines.Length; dataRow++)
            {
                var dataCells = lines[dataRow].Trim().Split('\t');
                if (dataCells.Length < 2) break;

                // The block ends at the first row whose column A is not a star level.
                if (!int.TryParse(dataCells[0].Trim(), out int starLevel))
                    break;

                T data = factory(type, dataCells, colMap);
                if (data == null)
                    continue;

                if (isValid(data))
                {
                    output[type][starLevel] = data;
                }
                else
                {
                    // Dropped rather than stored: a row that fails its own invariants
                    // would ship as silently wrong balance.
                    Log.w($"[HeroDataParser] {type} star {starLevel} failed Validate(); row dropped.");
                }
            }
        }

        int totalCount = 0;
        foreach (var pair in output)
            totalCount += pair.Value.Count;

        Log.d($"[HeroDataParser] {typeof(T).Name}: {output.Count} hero types, {totalCount} rows.");
    }

    /// <summary>
    /// Builds an instance by matching each constructor parameter to a sheet column
    /// of the same name, PascalCased. Returns null if the type or its constructor
    /// is missing, so the caller can skip the row.
    /// </summary>
    private static object Construct(Type targetType, string[] cells, Dictionary<string, int> colMap, HeroType type)
    {
        if (targetType == null)
        {
            Log.w($"[HeroDataParser] No data class found for {type}.");
            return null;
        }

        var constructors = targetType.GetConstructors();
        if (constructors.Length == 0)
        {
            Log.e($"[HeroDataParser] {targetType.Name} has no public constructor.");
            return null;
        }

        var parameters = constructors[0].GetParameters();
        var args = new object[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            args[i] = ReadArgument(parameters[i], cells, colMap, type, targetType.Name);
        }

        try
        {
            return constructors[0].Invoke(args);
        }
        catch (Exception e)
        {
            Log.e($"[HeroDataParser] Could not construct {targetType.Name}: {e.Message}");
            return null;
        }
    }

    private static object ReadArgument(
        ParameterInfo param,
        string[] cells,
        Dictionary<string, int> colMap,
        HeroType type,
        string typeName)
    {
        // Sheet columns are PascalCase, constructor parameters are camelCase.
        string columnName = char.ToUpper(param.Name[0]) + param.Name.Substring(1);

        if (param.ParameterType == typeof(int))
            return Mathf.RoundToInt(GetValue(cells, colMap, columnName, type));

        if (param.ParameterType == typeof(float))
            return GetValue(cells, colMap, columnName, type);

        if (param.ParameterType == typeof(string))
            return GetString(cells, colMap, columnName, type);

        if (param.ParameterType.IsEnum)
        {
            string enumStr = GetString(cells, colMap, columnName, type);
            if (Enum.TryParse(param.ParameterType, enumStr, true, out object enumValue))
                return enumValue;

            Log.w($"[HeroDataParser] {columnName} = '{enumStr}' is not a {param.ParameterType.Name}; using the first member.");
            return Enum.GetValues(param.ParameterType).GetValue(0);
        }

        Log.w($"[HeroDataParser] {typeName}.{param.Name} is a {param.ParameterType}, which the sheet cannot express.");
        return param.DefaultValue != DBNull.Value ? param.DefaultValue : null;
    }

    private static float GetValue(string[] cells, Dictionary<string, int> colMap, string key, HeroType type)
    {
        if (!colMap.TryGetValue(key, out int colIndex) || colIndex >= cells.Length)
        {
            Log.w($"[HeroDataParser] {type}: column '{key}' not found, defaulting to 0.");
            return 0f;
        }

        string cell = cells[colIndex].Trim();

        // Invariant culture: the sheet writes 1.5, which parses as 15 or fails outright
        // on a device whose locale uses a comma.
        if (!float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            Log.w($"[HeroDataParser] {type}: column '{key}' holds '{cell}', which is not a number. Defaulting to 0.");
            return 0f;
        }

        return value;
    }

    private static string GetString(string[] cells, Dictionary<string, int> colMap, string key, HeroType type)
    {
        if (colMap.TryGetValue(key, out int colIndex) && colIndex < cells.Length)
            return cells[colIndex].Trim();

        Log.w($"[HeroDataParser] {type}: column '{key}' not found, defaulting to empty.");
        return string.Empty;
    }
}
