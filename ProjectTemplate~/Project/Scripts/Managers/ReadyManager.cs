using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using HexitEngine;

/// <summary>
/// [게임] Ready 씬 매니저. Ready 씬에 NetworkObject와 함께 배치합니다. (Is Persistent 끄기)
/// 템플릿이 제공하는 기본 형태이며, 게임마다 이 파일에 게임 내용을 작성합니다.
///
/// 로딩 화면: 각자 자기 준비가 끝나면 ReleaseLoading()으로 걷습니다.
/// - 기본(캐릭터 없음): 스폰되어 준비 목록을 받으면 바로 걷습니다.
/// - 캐릭터가 있는 게임: 내 캐릭터와 이미 있는 다른 플레이어 캐릭터가 모두 생성되었을 때 ReleaseLoading()을 호출합니다.
///
/// 게임 시작: 방장이 Play를 누르면 상태가 얼려진 채(새 참가, 레디 변경 불가) START_DELAY_SECONDS 동안 기다린 뒤 Game으로 이동합니다.
/// - 그 사이 인원이 최소 인원 미만이 되면 엔진이 자동으로 취소합니다. (대기도 함께 멈춤)
/// - 카운트다운이 필요한 게임은 아래 대기 부분에 카운트다운을 작성합니다. (화면 표시는 ReadyManagerBase.StartingElapsedSeconds 사용)
/// </summary>
public class ReadyManager : ReadyManagerBase
{
    /// <summary> Play 후 Game으로 이동하기까지 기다리는 시간(초) </summary>
    private const float START_DELAY_SECONDS = 1f;

    protected override void OnReadyManagerSpawn()
    {
        ReleaseLoading();
    }

    protected override void OnLoadingReleasedHook()
    {
        Dev.Log("Ready 씬 준비 완료: 로딩 화면이 걷혔습니다.");
    }

    #region Game Start

    protected override void OnServerStartingBegan(CancellationToken ct)
    {
        StartAfterDelayAsync(ct).Forget();
    }

    /// <summary> [서버만] 기다린 뒤 Game으로 이동. 시작 준비가 취소되면 ct가 취소되어 이동하지 않습니다. </summary>
    private async UniTaskVoid StartAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(START_DELAY_SECONDS), ignoreTimeScale: true, cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }

        ProceedToGame_OnlyServer();
    }

    protected override void OnServerStartingCanceled()
    {
        Dev.Log("인원이 부족해 게임 시작이 취소되었습니다.");
    }

    #endregion
}