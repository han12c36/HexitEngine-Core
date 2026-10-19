using UnityEngine;

/// <summary>
/// 일정 간격마다 true를 반환하는 타이머 클래스
/// </summary>
public class IntervalTimer
{
    /// <summary>
    /// 반복 주기 (s)
    /// </summary>
    private float interval;

    /// <summary>
    /// 다음 주기까지 남은 시간 (s)
    /// </summary>
    private float timeLeft;

    public float TimeLeft => timeLeft;

    /// <param name="intervalSec">주기 (s)</param>
    public IntervalTimer(float intervalSec)
    {
        interval = intervalSec;
        timeLeft = intervalSec;
    }

    /// <summary>
    /// 시간을 감소시키고, 주기가 도달했는지 여부를 반환합니다.
    /// </summary>
    /// <param name="deltaTime">경과 시간 (Time.deltaTime)</param>
    /// <returns>true: 주기 도달 (작업 실행 시점), false: 대기 중</returns>
    public bool Tick(float deltaTime)
    {
        timeLeft -= deltaTime;

        if (timeLeft > 0f)
            return false;

        timeLeft = interval;
        return true;
    }

    /// <summary>
    /// 타이머를 초기 상태로 되돌립니다.
    /// </summary>
    public void Reset() => timeLeft = interval;
}
