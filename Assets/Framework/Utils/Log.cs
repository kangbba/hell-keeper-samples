using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Android-style logger. Calls are compiled out of release builds.
/// </summary>
public static class Log
{
    /// <summary>
    /// Debug log, editor and development builds only. Cyan.
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void d(object message)
    {
        Debug.Log($"<color=#00d7ff>{message}</color>");
    }

    /// <summary>
    /// Warning log, editor and development builds only.
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void w(object message)
    {
        Debug.LogWarning(message);
    }

    /// <summary>
    /// Error log, editor and development builds only.
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void e(object message)
    {
        Debug.LogError(message);
    }

    /// <summary>
    /// Tagged debug log, editor and development builds only. Cyan.
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void d(string tag, object message)
    {
        Debug.Log($"<color=#00d7ff>[{tag}]</color> {message}");
    }

    /// <summary>
    /// Tagged warning log, editor and development builds only.
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void w(string tag, object message)
    {
        Debug.LogWarning($"[{tag}] {message}");
    }

    /// <summary>
    /// Tagged error log, editor and development builds only.
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void e(string tag, object message)
    {
        Debug.LogError($"[{tag}] {message}");
    }
}
