using System;
using System.Collections.Generic;

/// <summary>
/// Runtime access to the hero balance data. Reads the static tables the editor tool
/// generates; it does not download, parse or generate anything, and does not know the
/// sheet URL exists, which keeps editor-only code out of the build.
/// </summary>
public static class HeroDataManager
{
    private static bool _isInitialized;

    private static Dictionary<HeroType, Dictionary<int, HeroBaseData>> _baseCache;
    private static Dictionary<HeroType, Dictionary<int, HeroUniqueData>> _uniqueCache;

    private static HeroDataQuery _query;

    public static bool IsInitialized => _isInitialized;
    public static HeroDataQuery Query => _query;

    public static void Initialize()
    {
        if (_isInitialized)
        {
            Log.w("[HeroDataManager] Already initialized.");
            return;
        }

        _baseCache = HeroRawData.HeroBaseDatas;
        _uniqueCache = HeroRawData.UniqueData;

        if (_baseCache == null || _uniqueCache == null)
        {
            Log.e("[HeroDataManager] HeroRawData is empty. Run Tools > Hero Data > Download.");
            return;
        }

        ValidateLoadedData();

        _query = new HeroDataQuery(_uniqueCache, _baseCache);

        _isInitialized = true;
    }

    /// <summary>
    /// Fails at startup rather than at the first spawn. A hero missing from the
    /// sheet otherwise surfaces as a null deref deep in combat, far from the cause.
    /// </summary>
    private static void ValidateLoadedData()
    {
        var requiredTypes = new[]
        {
            HeroType.Fire,
            HeroType.Ice,
            HeroType.Lightning,
            HeroType.Poison,
            HeroType.Rock
        };

        var missing = new List<string>();

        foreach (var type in requiredTypes)
        {
            if (!_uniqueCache.ContainsKey(type))
                missing.Add($"{type} UniqueData");

            if (!_baseCache.ContainsKey(type))
                missing.Add($"{type} HeroBaseData");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "HeroDataManager: balance data is incomplete. Run Tools > Hero Data > Download.\n" +
                "Missing: " + string.Join(", ", missing));
        }
    }

    /// <summary>
    /// Returns a copy, so a caller that scales a stat for buffs cannot write
    /// through to the shared table and corrupt every later read.
    /// </summary>
    public static HeroUniqueData GetUnique(HeroType type, int starLevel)
    {
        RequireInitialized();

        if (_uniqueCache.TryGetValue(type, out var starData) && starData.TryGetValue(starLevel, out var data))
            return data.Clone();

        throw new KeyNotFoundException($"No UniqueData for {type} at star level {starLevel}.");
    }

    public static HeroBaseData GetBase(HeroType type, int starLevel)
    {
        RequireInitialized();

        if (_baseCache.TryGetValue(type, out var starData) && starData.TryGetValue(starLevel, out var stat))
            return stat.Clone();

        throw new KeyNotFoundException($"No HeroBaseData for {type} at star level {starLevel}.");
    }

    private static void RequireInitialized()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("HeroDataManager.Initialize() has not been called.");
    }
}
