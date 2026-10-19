using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

public static class AsyncHelper 
{
    /// <summary>
    /// 반환값 없는 비동기 래퍼
    /// </summary>
    /// <param name="taskFunc"></param>
    /// <param name="caller"></param>
    /// <returns></returns>
    public static async Task TryCatchAsync(Func<Task> taskFunc, object caller = null)
    {
        try
        {
            await taskFunc();
        }
        catch (Exception e)
        {
            LogException(e, caller);
        }
    }

    public static async Task TryCatchAsync(Func<Task> taskFunc, Action onFail = null, object caller = null)
    {
        try
        {
            await taskFunc();
        }
        catch (Exception e)
        {
            LogException(e, caller);
            onFail?.Invoke();
        }
    }

    /// <summary>
    /// 반환값 있는 비동기 래퍼
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="taskFunc"></param>
    /// <param name="defaultValue"></param>
    /// <param name="caller"></param>
    /// <returns></returns>
    public static async Task<T> TryCatchAsync<T>(Func<Task<T>> taskFunc, T defaultValue = default, object caller = null)
    {
        try
        {
            return await taskFunc();
        }
        catch (Exception e)
        {
            LogException(e, caller);
            return defaultValue;
        }
    }

    /// <summary>
    /// 실패를 아예 무시하고 싶을 때
    /// </summary>
    /// <param name="taskFunc"></param>
    /// <param name="caller"></param>
    /// <returns></returns>
    public static async Task TryIgnoreAsync(Func<Task> taskFunc, object caller = null)
    {
        try { await taskFunc(); }
        catch (Exception e) { LogException(e, caller); }
    }

    /// <summary>
    /// 내부 공통 로깅 함수
    /// </summary>
    /// <param name="e"></param>
    /// <param name="caller"></param>
    private static void LogException(Exception e, object caller)
    {
        string tag = caller != null ? caller.GetType().Name : "UnknownClass";
        Debug.LogError($"[{tag}] : {e}");
    }
}