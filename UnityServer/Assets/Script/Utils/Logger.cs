using UnityEngine;

public enum LogLevel
{
    System,
    Debug,
    Warning,
    Error,
}

public class Logger
{
    public static void Log(object message)
    {
        Debug.Log($"[System] {message}");
    }

    public static void Log(object message, Object context)
    {
        Debug.Log($"[System] {message}", context);
    }

    public static void Log(LogLevel level, object message)
    {
        switch (level)
        {
            case LogLevel.System:
                Debug.Log($"[{level}] {message}");
                break;
            case LogLevel.Debug:
                Debug.Log($"[{level}] {message}");
                break;
            case LogLevel.Warning:
                Debug.LogWarning($"[{level}] {message}");
                break;
            case LogLevel.Error:
                Debug.LogError($"[{level}] {message}");
                break;
            default:
                break;
        }
        
    }

    public static void Log(LogLevel level, object message, Object context)
    {
        switch (level)
        {
            case LogLevel.System:
                Debug.Log($"[{level}] {message}", context);
                break;
            case LogLevel.Debug:
                Debug.Log($"[{level}] {message}", context);
                break;
            case LogLevel.Warning:
                Debug.LogWarning($"[{level}] {message}", context);
                break;
            case LogLevel.Error:
                Debug.LogError($"[{level}] {message}", context);
                break;
            default:
                break;
        }
    }
}
