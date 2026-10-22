using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace HexitEngine
{
    public enum LogType { Default, Warn, Error }

    public static class Dev
    {
        private static DevLogConfig _config;
        private static readonly Dictionary<string, string[]> headerCache = new();
        private static readonly object _lock = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            lock (_lock) { _config = null; headerCache.Clear(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (_config != null) return;
            var configs = Resources.FindObjectsOfTypeAll<DevLogConfig>();
            foreach (var c in configs)
            {
                if (!string.IsNullOrEmpty(c.name)) { _config = c; break; }
            }
        }

        public static void ClearCache() { lock (_lock) { headerCache.Clear(); } }

        //UnityEngine.Object context = null
        /// <summary> 'UNITY_EDITOR' or 'DEVELOPMENT_BUILD'인 경우에만 컴파일에 포함됩니다. </summary>
        [HideInCallstack]
        //[Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNITY_EDITOR"), Conditional("UNITY_ENABLE_CHECKS")]
        public static void Log(object message, LogType type = LogType.Default, [CallerFilePath] string path = "")
        {
            if (_config == null) Initialize();

            string header = GetHeader(path, type);
            string finalMsg = $"{header} {message}";

            switch (type)
            {
                case LogType.Warn: UnityEngine.Debug.LogWarning(finalMsg); break;
                case LogType.Error: UnityEngine.Debug.LogError(finalMsg); break;
                default: UnityEngine.Debug.Log(finalMsg); break;
            }
        }

        [HideInCallstack]
        //[Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNITY_EDITOR"), Conditional("UNITY_ENABLE_CHECKS")]
        public static void LogError(object message, [CallerFilePath] string path = "")
        {
            Log(message, LogType.Error, path);
        }

        [HideInCallstack]
        //[Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNITY_EDITOR"), Conditional("UNITY_ENABLE_CHECKS")]
        public static void LogWarn(object message, [CallerFilePath] string path = "")
        {
            Log(message, LogType.Warn, path);
        }

        private static string GetHeader(string path, LogType type)
        {
            lock (_lock)
            {
                if (!headerCache.TryGetValue(path, out string[] headers))
                {
                    headers = new string[Enum.GetNames(typeof(LogType)).Length];
                    headerCache[path] = headers;
                }

                int typeIdx = (int)type;
                if (string.IsNullOrEmpty(headers[typeIdx]))
                {
                    string className = Path.GetFileNameWithoutExtension(path);
                    if (Application.isEditor)
                    {
                        string hex = GetCachedHex(type);
                        headers[typeIdx] = $"<b><color=#{hex}>[{className}]</color></b>";
                    }
                    else
                    {
                        headers[typeIdx] = $"[{className}] ({type})";
                    }
                }
                return headers[typeIdx];
            }
        }

        private static string GetCachedHex(LogType type)
        {
            if (_config == null) return "FFFFFF";
            return type switch
            {
                LogType.Default => _config.defaultHex,
                LogType.Warn => _config.warningHex,
                LogType.Error => _config.errorHex,
                _ => _config.defaultHex
            };
        }
    }
}