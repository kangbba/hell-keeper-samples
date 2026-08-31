using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Fluent read API over the balance data. Hero, star level and property can be chosen
/// in either order, because designers ask both "Fire's travel time at 3 stars" and
/// "how Fire's travel time scales across stars".
///
///   float[] times = HeroDataManager.Query
///       .GetHero(HeroType.Fire)
///       .GetProperty(x => ((FireUniqueData)x).TravelTime)
///       .GetStarLevel(2, 3);
///
/// Each stage returns a new query object. A star level a hero never reaches is
/// filtered out rather than throwing.
/// </summary>
public class HeroDataQuery
{
    private readonly Dictionary<HeroType, Dictionary<int, HeroUniqueData>> _uniqueData;
    private readonly Dictionary<HeroType, Dictionary<int, HeroBaseData>> _baseData;

    public HeroDataQuery(
        Dictionary<HeroType, Dictionary<int, HeroUniqueData>> uniqueData,
        Dictionary<HeroType, Dictionary<int, HeroBaseData>> baseData)
    {
        _uniqueData = uniqueData;
        _baseData = baseData;
    }

    public HeroQuery GetHero(HeroType type)
    {
        return new HeroQuery(type, _uniqueData, _baseData);
    }

    public AllHeroQuery GetAllHeroes()
    {
        return new AllHeroQuery(_uniqueData);
    }
}

/// <summary>A single hero has been chosen; star level or property comes next.</summary>
public class HeroQuery
{
    private readonly HeroType _heroType;
    private readonly Dictionary<int, HeroUniqueData> _uniqueData;
    private readonly Dictionary<int, HeroBaseData> _baseData;

    public HeroQuery(
        HeroType type,
        Dictionary<HeroType, Dictionary<int, HeroUniqueData>> allUniqueData,
        Dictionary<HeroType, Dictionary<int, HeroBaseData>> allBaseData)
    {
        _heroType = type;

        // A hero with no sheet rows yet queries as empty rather than failing here,
        // so a half-filled balance sheet still loads in the editor.
        _uniqueData = allUniqueData.TryGetValue(type, out var unique)
            ? unique
            : new Dictionary<int, HeroUniqueData>();
        _baseData = allBaseData.TryGetValue(type, out var stats)
            ? stats
            : new Dictionary<int, HeroBaseData>();
    }

    public StarLevelQuery GetStarLevel(params int[] starLevels)
    {
        return new StarLevelQuery(_heroType, starLevels, _uniqueData, _baseData);
    }

    public PropertyQuery<T> GetProperty<T>(Func<HeroUniqueData, T> selector)
    {
        return new PropertyQuery<T>(selector, _uniqueData);
    }

    public IEnumerable<(int StarLevel, HeroUniqueData Data)> All()
    {
        return _uniqueData.Select(x => (x.Key, x.Value));
    }

    public IEnumerable<(int StarLevel, HeroBaseData Stat)> AllStats()
    {
        return _baseData.Select(x => (x.Key, x.Value));
    }
}

/// <summary>One or more star levels have been chosen for a hero.</summary>
public class StarLevelQuery
{
    private readonly HeroType _heroType;
    private readonly int[] _starLevels;
    private readonly Dictionary<int, HeroUniqueData> _uniqueData;
    private readonly Dictionary<int, HeroBaseData> _baseData;

    public StarLevelQuery(
        HeroType type,
        int[] starLevels,
        Dictionary<int, HeroUniqueData> uniqueData,
        Dictionary<int, HeroBaseData> baseData)
    {
        _heroType = type;
        _starLevels = starLevels;
        _uniqueData = uniqueData;
        _baseData = baseData;
    }

    public T[] GetProperty<T>(Func<HeroUniqueData, T> selector)
    {
        return _starLevels
            .Where(star => _uniqueData.ContainsKey(star))
            .Select(star => selector(_uniqueData[star]))
            .ToArray();
    }

    public HeroUniqueData[] GetData()
    {
        return _starLevels
            .Where(star => _uniqueData.ContainsKey(star))
            .Select(star => _uniqueData[star])
            .ToArray();
    }

    public HeroBaseData[] GetStats()
    {
        return _starLevels
            .Where(star => _baseData.ContainsKey(star))
            .Select(star => _baseData[star])
            .ToArray();
    }

    /// <summary>Unwraps the single-star case so callers do not index into a one-element array.</summary>
    public HeroUniqueData GetDataSingle()
    {
        RequireSingleStarLevel(nameof(GetDataSingle));
        return _uniqueData.TryGetValue(_starLevels[0], out var data) ? data : null;
    }

    public HeroBaseData GetStatSingle()
    {
        RequireSingleStarLevel(nameof(GetStatSingle));
        return _baseData.TryGetValue(_starLevels[0], out var stat) ? stat : null;
    }

    private void RequireSingleStarLevel(string caller)
    {
        if (_starLevels.Length != 1)
        {
            throw new InvalidOperationException(
                $"{caller} takes a single star level; {_heroType} was queried with {_starLevels.Length}.");
        }
    }
}

/// <summary>A property selector has been chosen; star levels come next.</summary>
public class PropertyQuery<T>
{
    private readonly Func<HeroUniqueData, T> _selector;
    private readonly Dictionary<int, HeroUniqueData> _uniqueData;

    public PropertyQuery(Func<HeroUniqueData, T> selector, Dictionary<int, HeroUniqueData> uniqueData)
    {
        _selector = selector;
        _uniqueData = uniqueData;
    }

    public T[] GetStarLevel(params int[] starLevels)
    {
        return starLevels
            .Where(star => _uniqueData.ContainsKey(star))
            .Select(star => _selector(_uniqueData[star]))
            .ToArray();
    }

    public T[] All()
    {
        return _uniqueData.Values
            .Select(_selector)
            .ToArray();
    }
}

/// <summary>Cross-hero reads, used by balance tooling and comparison tables.</summary>
public class AllHeroQuery
{
    private readonly Dictionary<HeroType, Dictionary<int, HeroUniqueData>> _uniqueData;

    public AllHeroQuery(Dictionary<HeroType, Dictionary<int, HeroUniqueData>> uniqueData)
    {
        _uniqueData = uniqueData;
    }

    public IEnumerable<(HeroType Type, HeroUniqueData Data)> GetStarLevel(int starLevel)
    {
        foreach (var hero in _uniqueData)
        {
            if (hero.Value.TryGetValue(starLevel, out var data))
                yield return (hero.Key, data);
        }
    }

    public IEnumerable<(HeroType Type, int StarLevel, HeroUniqueData Data)> All()
    {
        foreach (var hero in _uniqueData)
        {
            foreach (var star in hero.Value)
            {
                yield return (hero.Key, star.Key, star.Value);
            }
        }
    }
}
