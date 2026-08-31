using System.Threading;
using UnityEditor;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;

/// <summary>
/// Editor window that turns the balance spreadsheet into compiled C#, so a stat a
/// designer removes is a compile error at the call site rather than a zero at runtime.
///
/// Columns every hero shares go on HeroBaseData, columns only some heroes have go on
/// that hero's own XxxUniqueData subclass. That is why the generator diffs the per-hero
/// column sets before writing anything.
/// </summary>
public class HeroDataDownloader : EditorWindow
{
    // ---- Configuration ----
    private const string PrefsKeyUrl = "HeroDataDownloader.GoogleSheetURL";
    private const string DefaultUrl = "https://docs.google.com/spreadsheets/d/YOUR_SHEET_ID/export?format=tsv&gid=0";

    // A pasted share link points at the editor UI, not the export endpoint.
    private string ConvertToTsvExportUrl(string url)
    {
        if (url.Contains("/edit"))
        {
            return url.Replace("/edit?gid=", "/export?format=tsv&gid=")
                      .Replace("/edit#gid=", "/export?format=tsv&gid=");
        }
        return url;
    }

    private string _googleSheetUrl = "";

    private const string OutputHerodata = "Assets/HeroUniqueData/Generated/HeroRawData.cs";
    private const string OutputHeroBaseData = "Assets/HeroUniqueData/Base/HeroBaseData.cs";
    private const string OutputUniquedataBase = "Assets/HeroUniqueData/Base/HeroUniqueData.cs";
    private const string OutputUniquedataDir = "Assets/HeroUniqueData/Generated/";

    // ---- State ----
    private enum State { Idle, Downloading, Parsing, Generating, Done, Error }
    private State _state = State.Idle;
    private string _message = "Ready";
    private float _progress = 0f;
    private string _error = "";

    // ---- Parsed sheet ----
    private Dictionary<HeroType, Dictionary<int, Dictionary<string, object>>> _rawData = new();
    private HashSet<string> _allColumns = new();
    private Dictionary<HeroType, HashSet<string>> _heroColumns = new();

    [MenuItem("Tools/Hero Data/Download")]
    public static void ShowWindow()
    {
        var window = GetWindow<HeroDataDownloader>("Hero Data Downloader");
        window.minSize = new Vector2(500, 350);
        window.Show();
    }

    private CancellationTokenSource _cts;

    private void OnEnable()
    {
        _googleSheetUrl = EditorPrefs.GetString(PrefsKeyUrl, DefaultUrl);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Hero Data Downloader", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Google Sheets TSV URL:", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        _googleSheetUrl = EditorGUILayout.TextField(_googleSheetUrl);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetString(PrefsKeyUrl, _googleSheetUrl);
        }

        if (GUILayout.Button("Reset", GUILayout.Width(60)))
        {
            _googleSheetUrl = DefaultUrl;
            EditorPrefs.SetString(PrefsKeyUrl, _googleSheetUrl);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        EditorGUILayout.LabelField($"Status: {_message}");

        if (_state != State.Idle && _state != State.Done && _state != State.Error)
        {
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), _progress, $"{(_progress * 100):F0}%");
        }

        EditorGUILayout.Space();

        GUI.enabled = _state == State.Idle || _state == State.Done || _state == State.Error;
        if (GUILayout.Button("Download from Google Sheets", GUILayout.Height(40)))
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            DownloadAsync(_cts.Token).Forget();
        }
        GUI.enabled = true;

        if (_state == State.Error)
        {
            EditorGUILayout.HelpBox(_error, MessageType.Error);
        }

        if (_state == State.Done)
        {
            EditorGUILayout.HelpBox($"Done.\nGenerated: {OutputHerodata}\n{_rawData.Count} hero types.", MessageType.Info);
        }
    }

    private void OnDisable()
    {
        // Closing the window cancels the run, so nothing resumes into a dead
        // EditorWindow and calls Repaint on it.
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async UniTaskVoid DownloadAsync(CancellationToken token)
    {
        try
        {
            _state = State.Downloading;
            _message = "Downloading...";
            _progress = 0.2f;
            Repaint();

            string tsvUrl = ConvertToTsvExportUrl(_googleSheetUrl);
            Log.d($"[HeroDataDownloader] TSV URL: {tsvUrl}");

            string tsv = await GoogleSheetDownloader.DownloadTsvAsync(tsvUrl, token);
            if (string.IsNullOrEmpty(tsv))
                throw new Exception("Download failed.");

            _state = State.Parsing;
            _message = "Parsing...";
            _progress = 0.5f;
            Repaint();

            await UniTask.Yield(token);

            Log.d($"[HeroDataDownloader] Downloaded {tsv.Length} characters.");
            Log.d($"[HeroDataDownloader] First 500 characters:\n{tsv.Substring(0, Mathf.Min(500, tsv.Length))}");

            TsvRawParser.ParseToRawData(tsv, out _rawData);

            if (_rawData.Count == 0)
                throw new Exception("Parsing failed.");

            AnalyzeColumns();

            _state = State.Generating;
            _message = "Generating code...";
            _progress = 0.8f;
            Repaint();

            await UniTask.Yield(token);
            GenerateClassDefinitions();
            GenerateHeroData();

            _state = State.Done;
            _message = "Done.";
            _progress = 1f;
            Repaint();

            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            _state = State.Error;
            _message = "Error";
            _error = e.Message;
            _progress = 0f;
            Repaint();
            Log.e($"[HeroDataDownloader] {e.Message}\n{e.StackTrace}");
        }
    }

    private void AnalyzeColumns()
    {
        _allColumns.Clear();
        _heroColumns.Clear();

        foreach (var heroType in _rawData.Keys)
        {
            _heroColumns[heroType] = new HashSet<string>();

            foreach (var starLevel in _rawData[heroType].Keys)
            {
                foreach (var columnName in _rawData[heroType][starLevel].Keys)
                {
                    _allColumns.Add(columnName);
                    _heroColumns[heroType].Add(columnName);
                }
            }
        }

        Log.d($"[HeroDataDownloader] Found {_allColumns.Count} distinct columns.");
    }

    // Columns present for every hero become shared base fields; the rest are per-hero.
    private HashSet<string> ComputeCommonColumns()
    {
        var commonColumns = new HashSet<string>(_allColumns);
        foreach (var heroType in _heroColumns.Keys)
        {
            commonColumns.IntersectWith(_heroColumns[heroType]);
        }
        return commonColumns;
    }

    private void GenerateClassDefinitions()
    {
        var commonColumns = ComputeCommonColumns();

        Log.d($"[HeroDataDownloader] Shared columns: {string.Join(", ", commonColumns)}");

        GenerateHeroBaseData(commonColumns);

        GenerateUniqueData(commonColumns);
    }

    private void GenerateHeroBaseData(HashSet<string> commonColumns)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// Generated from the balance spreadsheet. Do not edit by hand.");
        sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("// Re-run Tools > Hero Data > Download after adding or removing a column.");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine();
        sb.AppendLine("[Serializable]");
        sb.AppendLine("public class HeroBaseData");
        sb.AppendLine("{");

        foreach (var col in commonColumns.OrderBy(x => x))
        {
            string fieldType = InferType(col);
            sb.AppendLine($"    public {fieldType} {col};");
        }

        sb.AppendLine();

        sb.Append("    public HeroBaseData(");
        var paramList = commonColumns.OrderBy(x => x).Select(col => $"{InferType(col)} {ToCamelCase(col)}").ToList();
        sb.Append(string.Join(", ", paramList));
        sb.AppendLine(")");
        sb.AppendLine("    {");
        foreach (var col in commonColumns.OrderBy(x => x))
        {
            sb.AppendLine($"        {col} = {ToCamelCase(col)};");
        }
        sb.AppendLine("    }");

        sb.AppendLine();
        sb.AppendLine("    public bool Validate() => true;");
        sb.AppendLine();
        sb.AppendLine("    public HeroBaseData Clone() => new HeroBaseData(");
        sb.Append("        ");
        sb.Append(string.Join(", ", commonColumns.OrderBy(x => x).Select(col => col)));
        sb.AppendLine(");");

        sb.AppendLine("}");

        Directory.CreateDirectory(Path.GetDirectoryName(OutputHeroBaseData));
        File.WriteAllText(OutputHeroBaseData, sb.ToString());
        Log.d($"[HeroDataDownloader] Wrote {OutputHeroBaseData}");
    }

    private void GenerateUniqueData(HashSet<string> commonColumns)
    {
        Directory.CreateDirectory(OutputUniquedataDir);

        var baseSb = new StringBuilder();
        baseSb.AppendLine("// Generated from the balance spreadsheet. Do not edit by hand.");
        baseSb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        baseSb.AppendLine("// Re-run Tools > Hero Data > Download after adding or removing a column.");
        baseSb.AppendLine();
        baseSb.AppendLine("using System;");
        baseSb.AppendLine();
        baseSb.AppendLine("[Serializable]");
        baseSb.AppendLine("public abstract class HeroUniqueData");
        baseSb.AppendLine("{");
        baseSb.AppendLine("    public abstract bool Validate();");
        baseSb.AppendLine("    public abstract HeroUniqueData Clone();");
        baseSb.AppendLine("}");

        File.WriteAllText(OutputUniquedataBase, baseSb.ToString());
        Log.d($"[HeroDataDownloader] Wrote {OutputUniquedataBase}");

        foreach (var heroType in _heroColumns.Keys.OrderBy(x => x.ToString()))
        {
            var uniqueColumns = _heroColumns[heroType].Except(commonColumns).OrderBy(x => x).ToList();
            var sb = new StringBuilder();

            sb.AppendLine("// Generated from the balance spreadsheet. Do not edit by hand.");
            sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("// Re-run Tools > Hero Data > Download after adding or removing a column.");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine($"[Serializable]");
            sb.AppendLine($"public class {heroType}UniqueData : HeroUniqueData");
            sb.AppendLine("{");

            foreach (var col in uniqueColumns)
            {
                string fieldType = InferType(col);
                sb.AppendLine($"    public {fieldType} {col};");
            }

            if (uniqueColumns.Count > 0)
            {
                sb.AppendLine();
                sb.Append($"    public {heroType}UniqueData(");
                var paramList = uniqueColumns.Select(col => $"{InferType(col)} {ToCamelCase(col)}").ToList();
                sb.Append(string.Join(", ", paramList));
                sb.AppendLine(")");
                sb.AppendLine("    {");
                foreach (var col in uniqueColumns)
                {
                    sb.AppendLine($"        {col} = {ToCamelCase(col)};");
                }
                sb.AppendLine("    }");
            }
            else
            {
                sb.AppendLine($"    public {heroType}UniqueData() {{ }}");
            }

            sb.AppendLine();
            sb.AppendLine("    public override bool Validate() => true;");
            sb.AppendLine();
            if (uniqueColumns.Count > 0)
            {
                sb.Append($"    public override HeroUniqueData Clone() => new {heroType}UniqueData(");
                sb.Append(string.Join(", ", uniqueColumns.Select(col => col)));
                sb.AppendLine(");");
            }
            else
            {
                sb.AppendLine($"    public override HeroUniqueData Clone() => new {heroType}UniqueData();");
            }

            sb.AppendLine("}");

            string outputPath = Path.Combine(OutputUniquedataDir, $"{heroType}UniqueData.cs");
            File.WriteAllText(outputPath, sb.ToString());
            Log.d($"[HeroDataDownloader] Wrote {outputPath}");
        }
    }

    private string InferType(string columnName)
    {
        bool hasFloat = false;
        bool hasInt = false;
        bool hasString = false;

        // Widen to the type that holds every observed value in the column.
        foreach (var heroData in _rawData.Values)
        {
            foreach (var starData in heroData.Values)
            {
                if (starData.TryGetValue(columnName, out var value))
                {
                    if (value is float)
                        hasFloat = true;
                    else if (value is int)
                        hasInt = true;
                    else if (value is string)
                        hasString = true;
                }
            }
        }

        // One fractional value makes the whole column float.
        if (hasFloat) return "float";

        if (hasInt) return "int";

        if (hasString) return "string";

        return "float";
    }

    private string ToCamelCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase)) return pascalCase;
        return char.ToLower(pascalCase[0]) + pascalCase.Substring(1);
    }

    private void GenerateHeroData()
    {
        var sb = new StringBuilder();

        sb.AppendLine("// AUTO-GENERATED FILE - DO NOT EDIT MANUALLY");
        sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("// Source: Google Sheets, via Tools > Hero Data > Download");
        sb.AppendLine();
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();

        sb.AppendLine("public static class HeroRawData");
        sb.AppendLine("{");

        GenerateHeroBaseDatas(sb);
        sb.AppendLine();

        GenerateUniqueData(sb);

        sb.AppendLine("}");

        Directory.CreateDirectory(Path.GetDirectoryName(OutputHerodata));
        File.WriteAllText(OutputHerodata, sb.ToString());
        Log.d($"[HeroDataDownloader] Wrote {OutputHerodata}");
    }

    private void GenerateHeroBaseDatas(StringBuilder sb)
    {
        var commonColumns = ComputeCommonColumns();

        sb.AppendLine("    public static readonly Dictionary<HeroType, Dictionary<int, HeroBaseData>> HeroBaseDatas = new()");
        sb.AppendLine("    {");

        foreach (var heroType in _rawData.Keys)
        {
            sb.AppendLine($"        [HeroType.{heroType}] = new()");
            sb.AppendLine("        {");

            foreach (var starLevel in _rawData[heroType].Keys)
            {
                var raw = _rawData[heroType][starLevel];

                // Argument order must match the generated constructor's parameter order.
                var parameters = new List<string>();
                foreach (var col in commonColumns.OrderBy(x => x))
                {
                    string value = FormatValueForCode(raw, col);
                    parameters.Add($"{ToCamelCase(col)}: {value}");
                }

                sb.AppendLine($"            [{starLevel}] = new HeroBaseData({string.Join(", ", parameters)}),");
            }

            sb.AppendLine("        },");
        }

        sb.AppendLine("    };");
    }

    private void GenerateUniqueData(StringBuilder sb)
    {
        var commonColumns = ComputeCommonColumns();

        sb.AppendLine("    public static readonly Dictionary<HeroType, Dictionary<int, HeroUniqueData>> UniqueData = new()");
        sb.AppendLine("    {");

        foreach (var heroType in _rawData.Keys)
        {
            sb.AppendLine($"        [HeroType.{heroType}] = new()");
            sb.AppendLine("        {");

            // Columns this hero has that the shared base class does not cover.
            var uniqueColumns = _heroColumns[heroType].Except(commonColumns).OrderBy(x => x).ToList();

            foreach (var starLevel in _rawData[heroType].Keys)
            {
                var raw = _rawData[heroType][starLevel];

                if (uniqueColumns.Count > 0)
                {
                    var parameters = new List<string>();
                    foreach (var col in uniqueColumns)
                    {
                        string value = FormatValueForCode(raw, col);
                        parameters.Add($"{ToCamelCase(col)}: {value}");
                    }

                    sb.AppendLine($"            [{starLevel}] = new {heroType}UniqueData({string.Join(", ", parameters)}),");
                }
                else
                {
                    // A hero with no unique columns still needs an instance to exist.
                    sb.AppendLine($"            [{starLevel}] = new {heroType}UniqueData(),");
                }
            }

            sb.AppendLine("        },");
        }

        sb.AppendLine("    };");
    }

    // Formats a parsed cell as a C# literal.
    private string FormatValueForCode(Dictionary<string, object> raw, string columnName)
    {
        if (!raw.TryGetValue(columnName, out var value))
            return "0";

        if (value is int i)
            return i.ToString();

        if (value is float f)
            return $"{f}f";

        if (value is string s)
        {
            if (string.IsNullOrEmpty(s))
                return "\"\"";

            return $"\"{s}\"";
        }

        return value.ToString();
    }

}
