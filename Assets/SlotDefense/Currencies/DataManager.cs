using UnityEngine;
using UniRx;

/// <summary>
/// Sole owner of player currencies and stage progress. Fields are private
/// ReactiveProperty; the outside world sees IReadOnlyReactiveProperty and can only
/// write through the validated methods below, each of which persists immediately —
/// so memory, save file and every subscribed UI always agree.
/// </summary>
// TODO: if this ever needs tests or a second storage backend, make it an instance and inject it.
public static class DataManager
{
    private const string STAGE_NUMBER_KEY = "PLAYER_STAGE_NUMBER";
    private const string COIN_KEY = "PLAYER_COIN";
    private const string DIAMOND_KEY = "PLAYER_DIAMOND";
    // Element is run-scoped and volatile — deliberately never persisted.

    private static readonly ReactiveProperty<int> _coin = new(0);
    public static IReadOnlyReactiveProperty<int> Coin => _coin;

    private static readonly ReactiveProperty<int> _diamond = new(0);
    public static IReadOnlyReactiveProperty<int> Diamond => _diamond;

    private static readonly ReactiveProperty<int> _stageNumber = new(1);
    public static IReadOnlyReactiveProperty<int> StageNumber => _stageNumber;

    private static readonly ReactiveProperty<int> _element = new(0);
    public static IReadOnlyReactiveProperty<int> Element => _element;

    public static void Initialize()
    {
        _coin.Value = PlayerPrefs.GetInt(COIN_KEY, 0);
        _diamond.Value = PlayerPrefs.GetInt(DIAMOND_KEY, 0);
        _stageNumber.Value = PlayerPrefs.GetInt(STAGE_NUMBER_KEY, 1);
    }

    public static void Release()
    {
        _coin.Dispose();
        _diamond.Dispose();
        _stageNumber.Dispose();
        _element.Dispose();
    }

    private static void SaveValue(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        PlayerPrefs.Save();
    }

    // ============ Coin ============
    public static void AddCoin(int amount)
    {
        _coin.Value += amount;
        SaveValue(COIN_KEY, _coin.Value);
    }

    public static bool SpendCoin(int amount)
    {
        if (_coin.Value < amount) return false;
        _coin.Value -= amount;
        SaveValue(COIN_KEY, _coin.Value);
        return true;
    }

    // ============ Diamond ============
    public static void AddDiamond(int amount)
    {
        _diamond.Value += amount;
        SaveValue(DIAMOND_KEY, _diamond.Value);
    }

    public static bool SpendDiamond(int amount)
    {
        if (_diamond.Value < amount) return false;
        _diamond.Value -= amount;
        SaveValue(DIAMOND_KEY, _diamond.Value);
        return true;
    }

    // ============ StageNumber ============
    public static void SetStageNumber(int stageNumber)
    {
        _stageNumber.Value = Mathf.Max(1, stageNumber);
        SaveValue(STAGE_NUMBER_KEY, _stageNumber.Value);
    }

    public static void AddStageNumber(int add)
    {
        _stageNumber.Value = Mathf.Max(1, _stageNumber.Value + add);
        SaveValue(STAGE_NUMBER_KEY, _stageNumber.Value);
    }

    /// <summary>Advance to the next stage, called on victory.</summary>
    public static void AdvanceToNextStage()
    {
        _stageNumber.Value++;
        SaveValue(STAGE_NUMBER_KEY, _stageNumber.Value);
    }

    // ============ Element (volatile, per-run) ============
    public static void SetElement(int value)
    {
        _element.Value = Mathf.Max(0, value);
    }

    public static void AddElement(int amount)
    {
        _element.Value = Mathf.Max(0, _element.Value + amount);
    }

    public static bool SpendElement(int amount)
    {
        if (_element.Value < amount) return false;
        _element.Value -= amount;
        return true;
    }
}
