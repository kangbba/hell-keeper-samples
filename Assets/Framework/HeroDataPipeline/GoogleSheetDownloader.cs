using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;
using System.IO;

/// <summary>
/// Downloads a published Google Sheet as TSV. Transport only: it knows no domain type
/// and does not parse what it fetches.
///
/// TSV rather than CSV because designer-authored cells routinely contain commas.
/// </summary>
public static class GoogleSheetDownloader
{
    /// <summary>Returns the sheet as TSV text, or null if the request failed.</summary>
    public static async UniTask<string> DownloadTsvAsync(string url, CancellationToken token = default)
    {
        // Sheets share links default to the CSV export endpoint.
        string tsvUrl = url.Replace("export?format=csv", "export?format=tsv");

        Log.d($"[GoogleSheetDownloader] Downloading {tsvUrl}");

        using var req = UnityWebRequest.Get(tsvUrl);
        await req.SendWebRequest().WithCancellation(token);

        if (req.result != UnityWebRequest.Result.Success)
        {
            Log.e($"[GoogleSheetDownloader] Download failed\nURL: {tsvUrl}\nError: {req.error}");
            return null;
        }

        Log.d($"[GoogleSheetDownloader] Downloaded {req.downloadHandler.text.Length} bytes");
        return req.downloadHandler.text;
    }

    public static void SaveTsvToFile(string tsvContent, string fullPath)
    {
        string directory = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, tsvContent);
        Log.d($"[GoogleSheetDownloader] Wrote {tsvContent.Length} bytes to {fullPath}");
    }
}
