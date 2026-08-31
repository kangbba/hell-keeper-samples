using System.Threading;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

public static class HeroAssetManager
{
    private const string HERO_PREFAB_PATH = "HeroPrefabs/HeroPrefab_{0}";
    private const string HERO_DATA_PATH = "HeroDatas/HeroData_{0}";
    private const string BUFF_DATA_PATH = "HeroBuffDatas/HeroBuffData_{0}";

    private static readonly Dictionary<HeroType, Hero> _heroPrefabs = new();
    private static readonly Dictionary<HeroType, HeroGraphic> _heroGraphics = new();
    private static readonly Dictionary<HeroType, HeroData> _heroDatas = new();
    private static readonly Dictionary<BuffType, HeroBuffData> _buffDatas = new();

    private static bool _initialized = false;
    private static bool _isInitializing = false;

    public static readonly HeroType[] AllHeroTypes;
    public static readonly BuffType[] AllBuffTypes;

    static HeroAssetManager()
    {
        AllHeroTypes = (HeroType[])Enum.GetValues(typeof(HeroType));
        AllBuffTypes = (BuffType[])Enum.GetValues(typeof(BuffType));
    }

    // ============================================================
    // ---- Properties ----
    // ============================================================

    // Built once at the end of initialization: the icon set does not change afterwards,
    // and the UI reads these while scrolling.
    private static readonly Dictionary<HeroType, Sprite> _heroIcons = new();
    private static readonly Dictionary<BuffType, Sprite> _buffIcons = new();

    /// <summary>Hero icons keyed by type.</summary>
    public static IReadOnlyDictionary<HeroType, Sprite> HeroIcons => _heroIcons;

    /// <summary>Buff icons keyed by type.</summary>
    public static IReadOnlyDictionary<BuffType, Sprite> BuffIcons => _buffIcons;

    public static bool IsInitialized => _initialized;
    public static bool IsInitializing => _isInitializing;

    // ============================================================
    // ---- Initialization ----
    // ============================================================

    public static async UniTask<bool> InitializeAsync(CancellationToken token = default)
    {
        if (_initialized)
        {
            return true;
        }

        // A second caller awaits the in-flight load rather than starting its own.
        if (_isInitializing)
        {
            await UniTask.WaitUntil(() => !_isInitializing, cancellationToken: token);
            return _initialized;
        }

        _isInitializing = true;

        try
        {
            // Resource loading must run on the main thread.
            await UniTask.SwitchToMainThread(token);

            bool success = true;

            if (!await PreloadHeroPrefabsAsync(token))
            {
                Log.e("[HeroAssetManager] HeroPrefabs load failed!");
                success = false;
            }

            if (!await PreloadHeroDatasAsync(token))
            {
                Log.e("[HeroAssetManager] HeroDatas load failed!");
                success = false;
            }

            if (!await PreloadBuffDatasAsync(token))
            {
                Log.e("[HeroAssetManager] BuffDatas load failed!");
                success = false;
            }

            if (success)
            {
                BuildIconCaches();
                _initialized = true;
            }
            else
            {
                Log.e("[HeroAssetManager] Initialization failed - check resource paths!");
            }

            return success;
        }
        finally
        {
            // Cleared even when a load is cancelled or throws. Otherwise the flag stays
            // set and every later caller waits on an initialization that already ended.
            _isInitializing = false;
        }
    }

    private static void BuildIconCaches()
    {
        _heroIcons.Clear();
        foreach (var kvp in _heroDatas)
        {
            if (kvp.Value?.heroIcon != null)
                _heroIcons[kvp.Key] = kvp.Value.heroIcon;
        }

        _buffIcons.Clear();
        foreach (var kvp in _buffDatas)
        {
            if (kvp.Value?.buffIcon != null)
                _buffIcons[kvp.Key] = kvp.Value.buffIcon;
        }
    }
    // ============================================================
    // ---- Prefab loading ----
    // ============================================================

    private static async UniTask<bool> PreloadHeroPrefabsAsync(CancellationToken token)
    {
        int loadedCount = 0;
        int totalCount = AllHeroTypes.Length;

        foreach (var type in AllHeroTypes)
        {
            string path = string.Format(HERO_PREFAB_PATH, type);
            var request = Resources.LoadAsync<Hero>(path);

            await request.WithCancellation(token);

            var prefab = request.asset as Hero;
            if (prefab == null)
            {
                Log.e($"[HeroAssetManager] Hero prefab not found at Resources/{path}");
                continue;
            }
            _heroPrefabs[type] = prefab;

            // Cached from inside the hero prefab, so callers do not re-walk it per spawn.
            var heroGraphic = prefab.GetComponentInChildren<HeroGraphic>();
            if (heroGraphic != null)
            {
                _heroGraphics[type] = heroGraphic;
            }
            else
            {
                Log.w($"[HeroAssetManager] HeroGraphic not found in {type} prefab");
            }

            loadedCount++;
        }

        return loadedCount == totalCount;
    }

    // ---- ScriptableObject loading ----
    private static async UniTask<bool> PreloadHeroDatasAsync(CancellationToken token)
    {
        int loadedCount = 0;
        int totalCount = AllHeroTypes.Length;

        foreach (var type in AllHeroTypes)
        {
            string path = string.Format(HERO_DATA_PATH, type);
            var request = Resources.LoadAsync<HeroData>(path);

            await request.WithCancellation(token);

            var heroData = request.asset as HeroData;
            if (heroData != null)
            {
                _heroDatas[type] = heroData;
                loadedCount++;
            }
            else
            {
                Log.e($"[HeroAssetManager] HeroData not found at Resources/{path}");
            }
        }

        return loadedCount == totalCount;
    }

    private static async UniTask<bool> PreloadBuffDatasAsync(CancellationToken token)
    {
        int loadedCount = 0;
        int totalCount = AllBuffTypes.Length;

        foreach (var type in AllBuffTypes)
        {
            string path = string.Format(BUFF_DATA_PATH, type);
            var request = Resources.LoadAsync<HeroBuffData>(path);

            await request.WithCancellation(token);

            var buffData = request.asset as HeroBuffData;
            if (buffData != null)
            {
                _buffDatas[type] = buffData;
                loadedCount++;
            }
            else
            {
                Log.e($"[HeroAssetManager] BuffData not found at Resources/{path}");
            }
        }

        return loadedCount == totalCount;
    }

    public static HeroType GetRandomHeroType(HeroType? exclude = null)
    {
        if (AllHeroTypes == null || AllHeroTypes.Length == 0)
        {
            Log.e("[HeroAssetManager] AllHeroTypes is empty; falling back to Fire.");
            return HeroType.Fire;
        }

        List<HeroType> candidates = new();

        foreach (var type in AllHeroTypes)
        {
            if (exclude.HasValue && exclude.Value == type)
            {
                continue;
            }

            candidates.Add(type);
        }

        if (candidates.Count == 0)
        {
            Log.w("[HeroAssetManager] Every type was excluded; falling back to Fire.");
            return HeroType.Fire;
        }

        int index = UnityEngine.Random.Range(0, candidates.Count);
        HeroType selected = candidates[index];

        return selected;
    }

    public static Hero GetHeroPrefab(HeroType type)
    {
        if (!_heroPrefabs.TryGetValue(type, out var prefab))
        {
            Log.e($"[HeroAssetManager] No prefab cached for {type}");
            return null;
        }

        return prefab;
    }

    /// <summary>
    /// The HeroGraphic extracted from the hero prefab.
    /// </summary>
    public static HeroGraphic GetHeroGraphicPrefab(HeroType type)
    {
        if (!_heroGraphics.TryGetValue(type, out var graphic))
        {
            Log.e($"[HeroAssetManager] No HeroGraphic cached for {type}");
            return null;
        }

        return graphic;
    }

    // ---- HeroData accessors ----
    public static HeroData GetHeroData(HeroType type)
    {
        if (_heroDatas.TryGetValue(type, out var data))
            return data;

        Log.w($"[HeroAssetManager] No HeroData found for {type}");
        return null;
    }

    public static Color GetHeroColor(HeroType type)
    {
        var data = GetHeroData(type);
        return data != null ? data.heroColor : Color.white;
    }

    public static Sprite GetHeroIcon(HeroType type)
    {
        var data = GetHeroData(type);
        return data != null ? data.heroIcon : null;
    }

    /// <summary>
    /// Grade label as shown to the player. Korean is inline here, as it is in a
    /// handful of other places in the project: the game shipped Korean-only, so a
    /// localization table was never introduced. Routing these through one is the
    /// change a second language would force.
    /// </summary>
    public static string GetHeroGradeLabel(HeroType type)
    {
        var data = GetHeroData(type);
        if (data == null)
            return string.Empty;

        return data.heroGrade == HeroGrade.Advanced ? "상급악마" : "하급악마";
    }

    // ============================================================
    // ---- BuffData accessors ----
    // ============================================================

    public static HeroBuffData GetBuffData(BuffType type)
    {
        if (_buffDatas.TryGetValue(type, out var data))
            return data;

        Log.w($"[HeroAssetManager] No BuffData found for {type}");
        return null;
    }

    public static Sprite GetBuffIcon(BuffType type)
    {
        var data = GetBuffData(type);
        return data != null ? data.buffIcon : null;
    }

    public static List<HeroBuffData> GetAllBuffDatas()
    {
        return _buffDatas.Values.ToList();
    }

    // ============================================================
    // ---- Queries ----
    // ============================================================

    public static List<HeroData> GetAllHeroDatas()
    {
        return _heroDatas.Values.ToList();
    }

    /// <summary>
    /// All heroes of a given grade.
    /// </summary>
    public static List<HeroData> GetHeroDatasByGrade(HeroGrade grade)
    {
        return _heroDatas.Values.Where(data => data.heroGrade == grade).ToList();
    }

    // ============================================================
    // ============================================================

    /// <summary>
    /// Random hero type of a grade, used by the slot draw.
    /// </summary>
    /// <param name="grade">Grade to draw from.</param>
    /// <param name="exclude">Type to exclude, if any.</param>
    public static HeroType GetRandomHeroTypeByGrade(HeroGrade grade, HeroType? exclude = null)
    {
        var candidates = _heroDatas.Values
            .Where(data => data.heroGrade == grade)
            .Select(data => data.heroType)
            .ToList();

        if (exclude.HasValue)
        {
            candidates.RemoveAll(type => type == exclude.Value);
        }

        if (candidates.Count == 0)
        {
            Log.w($"[HeroAssetManager] No heroes found for grade {grade}. Fallback to Fire.");
            return HeroType.Fire;
        }

        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }
}
