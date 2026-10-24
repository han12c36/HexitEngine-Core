using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using static HexitEngine.MultiplayerService;

namespace HexitEngine
{
    /// <summary>
    /// [나만] 씬 전환 로딩 화면 관리. 엔진은 "언제" 보이고 걷을지만 정하고, "어떻게"(연출, 최소 표시 시간)는 각 로딩 화면(LoadingCanvasBase)이 정합니다.
    ///
    /// 로딩 화면
    /// - 화면은 CanvasService가 가지고 있습니다. (BootstrapConfig에 지정된 도착 씬별 프리팹을 부트스트랩 초기화 때 미리 생성)
    /// - 씬 전환마다 도착 씬에 맞는 것을 CanvasService에서 받아 보입니다. 없으면 화면 없이 흐름만 진행합니다.
    ///
    /// 언제 보이는지
    /// - 네트워크 씬 전환(MainMenu → Ready, Ready ⇄ Game): 내 컴퓨터에서 씬 로드(참가자는 씬 동기화)가 시작될 때
    /// - 세션 퇴장(Ready/Game → MainMenu): 퇴장을 시작할 때 (네트워크 종료 대기도 가림)
    /// - 앱 시작(Bootstrap → MainMenu): BootstrapConfig의 Show Loading On Boot가 켜져 있을 때만
    ///
    /// 언제 숨기는지: 아래 세 조건을 모두 만족하면 로딩 화면의 사라짐 연출(PlayHideAsync)을 시작합니다.
    /// 1. 내 씬 로드 완료
    /// 2. 씬의 매니저가 준비를 마침: 매니저가 Awake에서 HoldLoading, 준비가 끝나면 ReleaseLoading
    ///    - Ready: ReadyManagerBase (각자 자기 준비가 끝나면)
    ///    - Game: GameManagerBase (모든 플레이어 준비 후 서버 신호로 동시에)
    ///    - MainMenu처럼 매니저가 붙잡지 않는 씬은 로드가 끝나면 바로
    /// 3. 로딩 화면의 최소 표시 시간 경과
    /// 매니저가 놓지 못해도 최대 표시 시간(BootstrapConfig)이 지나면 경고 후 숨깁니다.
    /// 연결 끊김/강퇴는 메시지 창이 보이도록 연출 없이 즉시 숨깁니다.
    /// 사라지는 도중 새 로딩이 시작되면 연출을 멈추고 다시 보입니다.
    /// </summary>
    public partial class HEXITEngine
    {
        private const float DEFAULT_LOADING_TIMEOUT_SECONDS = 30f;

        /// <summary> 로딩 화면을 가지고 있는 서비스 (없으면 화면 없이 진행) </summary>
        private CanvasService canvasService;

        /// <summary> 지금 보이는 로딩 화면 </summary>
        private LoadingCanvasBase currentLoadingCanvas;

        /// <summary> 로딩 화면 연출(나타남/사라짐) 취소용. 새 연출이 시작되거나 즉시 걷을 때 이전 연출을 멈춥니다. </summary>
        private CancellationTokenSource loadingCanvasCts;

        /// <summary> 로딩을 붙잡고 있는 곳 </summary>
        private readonly HashSet<object> loadingHolders = new();

        /// <summary> 진행률을 읽을 씬 로드 작업 (없으면 진행률을 갱신하지 않음) </summary>
        private AsyncOperation loadingOperation;

        /// <summary> 새 로딩이 시작되면 증가. 이전 로딩의 대기 작업(최소 시간, 사라짐 연출, 시간 초과)을 무효화합니다. </summary>
        private int loadingVersion;

        private bool isLoadingSceneLoaded;

        /// <summary> 걷는 중인지 (최소 표시 시간 대기 또는 사라짐 연출 중) </summary>
        private bool isLoadingFinishing;

        private float loadingStartTime;

        /// <summary> [나만] 로딩 화면이 떠 있는지 (사라짐 연출 중에도 true) </summary>
        public bool IsLoading { get; private set; }

        /// <summary> [나만] 로딩 시작 </summary>
        public event EventHandler OnLoadingStarted;

        /// <summary> [나만] 로딩 끝 (사라짐 연출까지 끝나 화면이 완전히 걷힌 직후) </summary>
        public event EventHandler OnLoadingFinished;

        #region Public

        /// <summary>
        /// [나만] 로딩 화면을 붙잡습니다. 같은 holder로 여러 번 불러도 한 번으로 셉니다.
        /// 씬의 매니저가 Awake에서 부르고, 준비가 끝나면 ReleaseLoading을 부릅니다. (파괴될 때도 반드시 놓기)
        /// </summary>
        public void HoldLoading(object holder)
        {
            if (holder == null) return;
            loadingHolders.Add(holder);
        }

        /// <summary> [나만] 로딩 화면을 놓습니다. 모두 놓으면(그리고 씬 로드가 끝났으면) 로딩 화면이 걷힙니다. </summary>
        public void ReleaseLoading(object holder)
        {
            if (holder == null || !loadingHolders.Remove(holder)) return;
            TryFinishLoading();
        }

        #endregion

        #region Internal (Engine)

        /// <summary>
        /// 로딩을 시작합니다. sceneName은 도착 씬 이름이며 로딩 화면 선택에 사용합니다.
        /// 이미 떠 있으면 화면은 유지(도착 씬이 달라 화면이 다르면 교체, 사라지는 중이면 다시 보임)하고 씬 로드 조건만 처음부터 다시 셉니다.
        /// </summary>
        internal void BeginLoading(string sceneName, AsyncOperation operation = null)
        {
            int version = ++loadingVersion;
            bool wasFinishing = isLoadingFinishing;

            loadingOperation = operation;
            isLoadingSceneLoaded = false;
            isLoadingFinishing = false;

            // 이전 씬이 붙잡은 것은 의미가 없으므로 비움 (새 씬의 매니저는 이후 Awake에서 붙잡음)
            loadingHolders.Clear();

            var canvas = GetLoadingCanvas(sceneName);

            if (canvas != currentLoadingCanvas)
            {
                // 다른 화면으로 교체: 이전 화면은 연출 없이 끔
                CancelLoadingCanvasAnimation();
                if (currentLoadingCanvas != null) currentLoadingCanvas.Hide();

                currentLoadingCanvas = canvas;
                PlayLoadingCanvasShow();
            }
            else if (wasFinishing)
            {
                // 같은 화면이 걷히던 중: 사라짐 연출을 멈추고 다시 보임
                PlayLoadingCanvasShow();
            }

            if (!IsLoading)
            {
                IsLoading = true;
                loadingStartTime = Time.realtimeSinceStartup;

                OnLoadingStarted?.Invoke(this, EventArgs.Empty);
            }

            TrackLoadingProgressAsync(version, destroyCancellationToken).Forget();
            WatchLoadingTimeoutAsync(version, destroyCancellationToken).Forget();
        }

        /// <summary> 진행률을 읽을 씬 로드 작업을 지정합니다. (로딩을 먼저 띄운 뒤 씬 로드를 시작하는 경우) </summary>
        internal void SetLoadingOperation(AsyncOperation operation) => loadingOperation = operation;

        /// <summary> 내 씬 로드가 끝났습니다. 붙잡은 곳이 없으면 최소 표시 시간 뒤 로딩이 걷힙니다. </summary>
        internal void CompleteLoadingScene()
        {
            if (!IsLoading) return;

            isLoadingSceneLoaded = true;
            if (currentLoadingCanvas != null) currentLoadingCanvas.SetProgress(1f);

            TryFinishLoading();
        }

        /// <summary> 로딩을 연출 없이 즉시 걷습니다. (실패, 연결 끊김: 메시지 창이 가려지지 않도록) </summary>
        internal void CancelLoading()
        {
            if (!IsLoading) return;

            loadingVersion++;
            EndLoading();
        }

        #endregion

        #region Flow

        private void TryFinishLoading()
        {
            if (!IsLoading || !isLoadingSceneLoaded || isLoadingFinishing || loadingHolders.Count > 0) return;

            FinishLoadingAsync(loadingVersion, destroyCancellationToken).Forget();
        }

        /// <summary> 로딩 화면의 최소 표시 시간을 채운 뒤 사라짐 연출을 하고 로딩을 끝냅니다. </summary>
        private async UniTaskVoid FinishLoadingAsync(int version, CancellationToken ct)
        {
            isLoadingFinishing = true;

            // 1. 최소 표시 시간
            try
            {
                float minDisplaySeconds = currentLoadingCanvas != null ? currentLoadingCanvas.MinDisplaySeconds : 0f;
                float remaining = minDisplaySeconds - (Time.realtimeSinceStartup - loadingStartTime);

                if (remaining > 0f)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(remaining), ignoreTimeScale: true, cancellationToken: ct);
                }
            }
            catch (OperationCanceledException) { return; }

            // 기다리는 사이 새 로딩이 시작되었으면 그 로딩이 처리함
            if (version != loadingVersion) return;

            if (!IsLoading || !isLoadingSceneLoaded || loadingHolders.Count > 0)
            {
                isLoadingFinishing = false;
                return;
            }

            // 2. 사라짐 연출 (도중에 새 로딩이 시작되면 취소되고 BeginLoading이 다시 보임)
            var canvas = currentLoadingCanvas;

            if (canvas != null)
            {
                try
                {
                    await canvas.PlayHideAsync(RestartLoadingCanvasAnimation());
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e)
                {
                    Dev.LogWarn($"로딩 화면 사라짐 연출 중 오류 (화면은 끕니다): {e.Message}");
                }
            }

            if (version != loadingVersion) return;

            EndLoading();
        }

        private void EndLoading()
        {
            IsLoading = false;
            isLoadingSceneLoaded = false;
            isLoadingFinishing = false;
            loadingOperation = null;
            loadingHolders.Clear();

            // 연출이 남아 있으면 멈추고, override에서 끄지 않았어도 확실히 끔
            CancelLoadingCanvasAnimation();
            if (currentLoadingCanvas != null) currentLoadingCanvas.Hide();
            currentLoadingCanvas = null;

            OnLoadingFinished?.Invoke(this, EventArgs.Empty);
        }

        /// <summary> 씬 로드가 끝날 때까지 매 프레임 진행률을 전달합니다. </summary>
        private async UniTaskVoid TrackLoadingProgressAsync(int version, CancellationToken ct)
        {
            try
            {
                while (IsLoading && version == loadingVersion && !isLoadingSceneLoaded)
                {
                    // AsyncOperation.progress는 로드 완료 시 0.9, 활성화 후 1
                    if (currentLoadingCanvas != null && loadingOperation != null) currentLoadingCanvas.SetProgress(Mathf.Clamp01(loadingOperation.progress / 0.9f));

                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary> 최대 표시 시간이 지나도 걷히지 않으면 원인을 남기고 걷습니다. </summary>
        private async UniTaskVoid WatchLoadingTimeoutAsync(int version, CancellationToken ct)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(GetLoadingTimeoutSeconds()), ignoreTimeScale: true, cancellationToken: ct);
            }
            catch (OperationCanceledException) { return; }

            if (!IsLoading || version != loadingVersion) return;

            if (!isLoadingSceneLoaded) Dev.LogWarn("로딩 시간이 초과되었습니다. 씬 로드가 끝나지 않았지만 로딩 화면을 숨깁니다.");
            else Dev.LogWarn($"로딩 시간이 초과되었습니다. 로딩을 놓지 않은 곳: {DescribeLoadingHolders()}");

            loadingVersion++;
            EndLoading();
        }

        #endregion

        #region Loading Canvas Animation

        /// <summary> 현재 로딩 화면의 나타남 연출을 시작합니다. 씬 로드는 기다리지 않고 동시에 진행됩니다. </summary>
        private void PlayLoadingCanvasShow()
        {
            if (currentLoadingCanvas == null) return;

            var canvas = currentLoadingCanvas;
            PlayLoadingCanvasShowAsync(canvas, RestartLoadingCanvasAnimation()).Forget();

            // 연출의 첫 부분(Show)은 바로 실행되므로 여기서 진행률을 초기화할 수 있음
            canvas.SetProgress(0f);
        }

        private async UniTaskVoid PlayLoadingCanvasShowAsync(LoadingCanvasBase canvas, CancellationToken ct)
        {
            try
            {
                await canvas.PlayShowAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Dev.LogWarn($"로딩 화면 나타남 연출 중 오류 (화면은 켭니다): {e.Message}");
                if (IsLoading && canvas == currentLoadingCanvas) canvas.Show();
            }
        }

        /// <summary> 이전 연출을 멈추고 새 연출용 토큰을 만듭니다. (HEXITEngine이 파괴되면 함께 취소) </summary>
        private CancellationToken RestartLoadingCanvasAnimation()
        {
            CancelLoadingCanvasAnimation();

            loadingCanvasCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            return loadingCanvasCts.Token;
        }

        private void CancelLoadingCanvasAnimation()
        {
            if (loadingCanvasCts == null) return;

            loadingCanvasCts.Cancel();
            loadingCanvasCts.Dispose();
            loadingCanvasCts = null;
        }

        #endregion

        #region Network Scene Events

        /// <summary>
        /// 내 컴퓨터에서 네트워크 씬 로드 시작.
        /// 참가자의 첫 접속(씬 동기화)은 씬 이름이 오지 않지만, 게임 중에는 접속 승인에서 거절되므로 도착 씬은 항상 Ready입니다.
        /// </summary>
        private void HandleLocalSceneLoadStarted(object sender, OnSceneLoadEventArgs e)
        {
            string sceneName = e.isSynchronize ? nameof(Loader.Scene.Ready) : e.sceneName;
            BeginLoading(sceneName, e.asyncOperation);
        }

        /// <summary> 내 컴퓨터에서 네트워크 씬 로드(참가자는 씬 동기화) 완료 </summary>
        private void HandleLocalSceneLoadCompleted(object sender, OnSceneLoadEventArgs e) => CompleteLoadingScene();

        #endregion

        #region Helpers

        /// <summary> 도착 씬에 맞는 로딩 화면 (CanvasService가 보관). 없으면 null </summary>
        private LoadingCanvasBase GetLoadingCanvas(string sceneName) => canvasService != null ? canvasService.GetLoadingCanvas(sceneName) : null;

        private static float GetLoadingTimeoutSeconds()
        {
            var config = BootstrapConfig.Instance;
            return config != null ? config.LoadingTimeoutSeconds : DEFAULT_LOADING_TIMEOUT_SECONDS;
        }

        private string DescribeLoadingHolders()
        {
            var names = new List<string>();

            foreach (var holder in loadingHolders)
            {
                names.Add(holder is UnityEngine.Object unityObject && unityObject != null ? $"{unityObject.GetType().Name}({unityObject.name})" : holder.GetType().Name);
            }

            return string.Join(", ", names);
        }

        #endregion
    }
}