using System;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;
using UniRx;

/// <summary>
/// Timer on wall-clock time, so a cycle keeps completing while the app is closed and
/// the elapsed cycles are granted on the next launch. State lives in PlayerPrefs under
/// SaveKey; remaining time is a ReactiveProperty so the UI does not poll.
///
/// A MonoBehaviour because save and load hang off the app lifecycle.
/// </summary>
public class RealTimer : MonoBehaviour
{
    // ---- Reactive Properties ----

    private readonly ReactiveProperty<int> _remainingSeconds = new(0);
    /// <summary>Seconds remaining until the next cycle. Subscribable.</summary>
    public IReadOnlyReactiveProperty<int> RemainingSecondsReactive => _remainingSeconds;

    private readonly ReactiveProperty<int> _completedCycles = new(0);
    /// <summary>Number of cycles completed so far. Subscribable.</summary>
    public IReadOnlyReactiveProperty<int> CompletedCyclesReactive => _completedCycles;

    // ---- Configuration ----

    /// <summary>Duration of one cycle in seconds.</summary>
    public int IntervalSeconds { get; private set; }

    /// <summary>Maximum number of cycles (-1 for infinite).</summary>
    public int MaxCycles { get; private set; }

    /// <summary>PlayerPrefs save key (null disables auto save).</summary>
    public string SaveKey { get; private set; }

    // ---- State ----

    /// <summary>Time the last cycle completed.</summary>
    public DateTime LastCycleTime { get; private set; }

    /// <summary>Number of cycles completed so far.</summary>
    public int CompletedCycles
    {
        get => _completedCycles.Value;
        private set => _completedCycles.Value = value;
    }

    /// <summary>Seconds remaining until the next cycle.</summary>
    public int RemainingSeconds
    {
        get => _remainingSeconds.Value;
        private set
        {
            _remainingSeconds.Value = value;
            // Keep the externally bound ReactiveProperty in sync.
            if (_externalRemainingSeconds != null)
            {
                _externalRemainingSeconds.Value = value;
            }
        }
    }

    /// <summary>Whether the timer is currently running.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Whether the timer has finished (reached MaxCycles).</summary>
    public bool IsCompleted => MaxCycles > 0 && CompletedCycles >= MaxCycles;

    // ---- Callbacks ----

    private Action<int> _onCycleComplete;
    private Action<int> _onUpdateUI;
    private Action _onAllCyclesComplete;

    private ReactiveProperty<int> _externalRemainingSeconds; // optional external ReactiveProperty binding

    private CancellationTokenSource _cts;

    // ---- Creation (static factory) ----

    /// <summary>Creates and starts a timer.</summary>
    /// <param name="saveKey">PlayerPrefs key; null disables persistence.</param>
    /// <param name="maxCycles">-1 for infinite.</param>
    public static RealTimer Create(
        int intervalSeconds,
        string saveKey = null,
        int maxCycles = -1,
        Action<int> onCycleComplete = null,
        Action<int> onUpdateUI = null,
        Action onAllCyclesComplete = null,
        ReactiveProperty<int> externalRemainingSeconds = null)
    {
        if (intervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalSeconds), intervalSeconds, "A cycle has to have a length.");
        }

        if (maxCycles == 0 || maxCycles < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCycles), maxCycles, "Use a positive count, or -1 for infinite.");
        }

        GameObject go = new GameObject($"RealTimer_{saveKey ?? "NoKey"}");
        DontDestroyOnLoad(go);

        RealTimer timer = go.AddComponent<RealTimer>();
        timer.Init(intervalSeconds, saveKey, maxCycles, onCycleComplete, onUpdateUI, onAllCyclesComplete, externalRemainingSeconds);

        return timer;
    }

    // ---- Initialization ----

    private void Init(
        int intervalSeconds,
        string saveKey,
        int maxCycles,
        Action<int> onCycleComplete,
        Action<int> onUpdateUI,
        Action onAllCyclesComplete,
        ReactiveProperty<int> externalRemainingSeconds)
    {
        IntervalSeconds = intervalSeconds;
        SaveKey = saveKey;
        MaxCycles = maxCycles;
        _onCycleComplete = onCycleComplete;
        _onUpdateUI = onUpdateUI;
        _onAllCyclesComplete = onAllCyclesComplete;
        _externalRemainingSeconds = externalRemainingSeconds;

        // With a SaveKey, restore saved state (this also processes offline time).
        if (!string.IsNullOrEmpty(saveKey))
        {
            Load();
        }
        else
        {
            LastCycleTime = DateTime.UtcNow;
            CompletedCycles = 0;
        }

        // Sync the initial value into the external binding before the first tick.
        if (_externalRemainingSeconds != null)
        {
            _externalRemainingSeconds.Value = RemainingSeconds;
        }

        // Written once here so a timer that is created and then never reaches a cycle still
        // has a starting point on disk.
        Save();

        StartTimer();

        Log.d($"[RealTimer] Created - Interval: {IntervalSeconds}s, SaveKey: {SaveKey}, MaxCycles: {MaxCycles}");
    }

    // ---- Unity lifecycle ----

    /// <summary>
    /// Deliberately not named Start: this runs when Create says so, not when Unity gets round
    /// to the component.
    /// </summary>
    private void StartTimer()
    {
        if (IsRunning) return;
        if (IsCompleted) return;

        IsRunning = true;
        _cts = new CancellationTokenSource();
        RunTimerAsync(_cts.Token).Forget();

        Log.d($"[RealTimer] Started");
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            // Save when going to background.
            Save();
            Log.d($"[RealTimer] App paused - Saved");
        }
        else
        {
            // Reload when returning to foreground so offline time is processed.
            Load();
            Log.d($"[RealTimer] App resumed - Loaded");
        }
    }

    private void OnApplicationQuit()
    {
        Save();
        Log.d($"[RealTimer] App quit - Saved");
    }

    private void OnDestroy()
    {
        Pause();
        _remainingSeconds?.Dispose();
        _completedCycles?.Dispose();

        Log.d($"[RealTimer] Destroyed");
    }

    // ---- Control ----

    /// <summary>Pauses the timer.</summary>
    public void Pause()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        Log.d($"[RealTimer] Paused");
    }

    /// <summary>Resumes the timer.</summary>
    public void Resume()
    {
        if (IsRunning) return;
        StartTimer();
        Log.d($"[RealTimer] Resumed");
    }

    /// <summary>Stops the timer and resets its state.</summary>
    public void Stop()
    {
        Pause();
        CompletedCycles = 0;
        LastCycleTime = DateTime.UtcNow;
        RemainingSeconds = CalculateRemainingSeconds();

        Log.d($"[RealTimer] Stopped");
    }

    /// <summary>Completes the timer immediately, processing all remaining cycles.</summary>
    public void ForceComplete()
    {
        if (IsCompleted)
            return;

        int remainingCycles = MaxCycles > 0 ? MaxCycles - CompletedCycles : 1;

        if (remainingCycles > 0)
        {
            CompletedCycles += remainingCycles;
            RemainingSeconds = 0;

            _onCycleComplete?.Invoke(remainingCycles);

            if (IsCompleted)
            {
                _onAllCyclesComplete?.Invoke();
            }

            Save();

            Log.d($"[RealTimer] Force completed {remainingCycles} cycle(s)");
        }

        // Pause owns IsRunning and the token source; clearing IsRunning here would make it
        // return early and leave the source behind.
        Pause();
    }

    // ---- Internal logic ----

    /// <summary>Calculates the remaining seconds from wall-clock time.</summary>
    private int CalculateRemainingSeconds()
    {
        double elapsed = (DateTime.UtcNow - LastCycleTime).TotalSeconds;
        double remaining = IntervalSeconds - elapsed;
        return Mathf.Max(0, Mathf.CeilToInt((float)remaining));
    }

    /// <summary>Processes time that elapsed while the app was offline.</summary>
    private int ProcessOfflineTime(DateTime lastSavedTime)
    {
        bool wasCompleted = IsCompleted;

        double elapsedSeconds = (DateTime.UtcNow - lastSavedTime).TotalSeconds;

        // Guard against abnormal deltas (e.g. clock set to the future, or more than a year elapsed).
        if (elapsedSeconds <= 0 || elapsedSeconds > 365 * 24 * 60 * 60)
        {
            Log.w($"[RealTimer] Abnormal elapsed time detected: {elapsedSeconds}s. Resetting.");
            LastCycleTime = DateTime.UtcNow;
            return 0;
        }

        int offlineCycles = Mathf.FloorToInt((float)(elapsedSeconds / IntervalSeconds));

        if (offlineCycles > 0)
        {
            // Clamp to the MaxCycles limit.
            if (MaxCycles > 0)
            {
                int remainingCycles = MaxCycles - CompletedCycles;
                offlineCycles = Mathf.Min(offlineCycles, remainingCycles);
            }

            CompletedCycles += offlineCycles;

            _onCycleComplete?.Invoke(offlineCycles);

            Log.d($"[RealTimer] Offline: {elapsedSeconds:F0}s elapsed, {offlineCycles} cycles completed");
        }

        // Recompute LastCycleTime relative to now instead of chaining AddSeconds,
        // so the leftover partial-cycle time is preserved exactly.
        double totalCompletedSeconds = offlineCycles * (double)IntervalSeconds;
        double leftoverSeconds = elapsedSeconds - totalCompletedSeconds;

        // Subtract the leftover from now to get the last cycle completion time.
        LastCycleTime = DateTime.UtcNow.AddSeconds(-leftoverSeconds);

        // Only when this pass is what completed it. Loading a timer that finished long ago
        // must not hand out the completion again.
        if (!wasCompleted && IsCompleted)
        {
            _onAllCyclesComplete?.Invoke();
        }

        // Persist immediately after offline catch-up so the grant is not repeated.
        if (offlineCycles > 0)
        {
            Save();
        }

        return offlineCycles;
    }

    private async UniTaskVoid RunTimerAsync(CancellationToken token)
    {
        while (IsRunning && !token.IsCancellationRequested)
        {
            double elapsedSeconds = (DateTime.UtcNow - LastCycleTime).TotalSeconds;

            if (elapsedSeconds >= IntervalSeconds)
            {
                int cycleCount = Mathf.FloorToInt((float)(elapsedSeconds / IntervalSeconds));

                // Clamp to the MaxCycles limit.
                if (MaxCycles > 0)
                {
                    int remainingCycles = MaxCycles - CompletedCycles;
                    cycleCount = Mathf.Min(cycleCount, remainingCycles);
                }

                if (cycleCount > 0)
                {
                    CompletedCycles += cycleCount;

                    _onCycleComplete?.Invoke(cycleCount);

                    // Advance LastCycleTime by whole cycles only, preserving leftover partial-cycle time.
                    LastCycleTime = LastCycleTime.AddSeconds(cycleCount * IntervalSeconds);

                    Save();

                    Log.d($"[RealTimer] Cycle completed x{cycleCount} (Total: {CompletedCycles})");
                }

                if (IsCompleted)
                {
                    _onAllCyclesComplete?.Invoke();
                    IsRunning = false;
                    Log.d($"[RealTimer] All cycles completed");
                    break;
                }
            }

            RemainingSeconds = CalculateRemainingSeconds();

            _onUpdateUI?.Invoke(RemainingSeconds);

            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
        }
    }

    // ---- Save / Load ----

    /// <summary>Saves the current state to PlayerPrefs.</summary>
    private void Save()
    {
        if (string.IsNullOrEmpty(SaveKey))
            return;

        PlayerPrefs.SetString(SaveKey + "_LastCycleTime", LastCycleTime.ToString("o"));
        PlayerPrefs.SetInt(SaveKey + "_CompletedCycles", CompletedCycles);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Deletes all timer data stored under the given SaveKey.
    /// Use this to clear stale data before reusing a timer.
    /// </summary>
    /// <param name="saveKey">SaveKey of the timer to clear.</param>
    public static void ClearSavedData(string saveKey)
    {
        if (string.IsNullOrEmpty(saveKey))
            return;

        PlayerPrefs.DeleteKey(saveKey + "_LastCycleTime");
        PlayerPrefs.DeleteKey(saveKey + "_CompletedCycles");
        PlayerPrefs.Save();

        Log.d($"[RealTimer] Cleared saved data for key: {saveKey}");
    }

    /// <summary>Loads state from PlayerPrefs and processes offline time.</summary>
    private int Load()
    {
        if (string.IsNullOrEmpty(SaveKey))
            return 0;

        string lastTimeKey = SaveKey + "_LastCycleTime";
        string cyclesKey = SaveKey + "_CompletedCycles";

        if (!PlayerPrefs.HasKey(lastTimeKey))
        {
            LastCycleTime = DateTime.UtcNow;
            CompletedCycles = 0;
            return 0;
        }

        string savedTimeStr = PlayerPrefs.GetString(lastTimeKey);
        int savedCycles = PlayerPrefs.GetInt(cyclesKey, 0);

        // Round-trip ("o") format keeps the saved time culture-invariant and UTC-safe.
        if (DateTime.TryParseExact(savedTimeStr, "o", null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime savedTime))
        {
            CompletedCycles = savedCycles;
            int offlineCycles = ProcessOfflineTime(savedTime);

            Log.d($"[RealTimer] Loaded - Key: {SaveKey}, OfflineCycles: {offlineCycles}");
            return offlineCycles;
        }
        else
        {
            Log.e($"[RealTimer] Failed to parse time - Key: {SaveKey}, Value: {savedTimeStr}");
            LastCycleTime = DateTime.UtcNow;
            CompletedCycles = 0;
            return 0;
        }
    }
}
