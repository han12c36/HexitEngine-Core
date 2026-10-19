using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace HexitEngine
{
    /// <summary> 비네트워크 싱글톤 베이스
    /// - Awake/OnEnable/Start/OnDisable/OnDestroy를 직접 선언하지 말고 OnAwake/OnEnabled/OnStart/OnDisabled/OnDestroyed를 override 합니다.
    /// - OnAwake: 싱글톤 등록 직후. 자기 자신의 참조 준비만 하고 다른 서비스는 참조하지 않습니다.
    /// - OnInitializeAsync: 실제 초기화. BootstrapRunner가 순서대로 호출하고 완료를 기다립니다.
    /// - 종료 중(AppState.IsQuitting)에는 OnEnabled/OnStart/OnDisabled/OnActiveSceneChanged가 호출되지 않습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class LocalSingleton<T> : MonoBehaviour, ISingleton where T : LocalSingleton<T>
    {
        /// <summary> 싱글톤 인스턴스. 종료 중에는 null. </summary>
        public static T Singleton => SingletonCore<T>.Instance;

        /// <summary> 사용 가능한 인스턴스가 있는지 여부. 종료 중에는 false. </summary>
        public static bool HasInstance => SingletonCore<T>.HasInstance;

        /// <summary> 씬 전환 후 생존 여부. </summary>
        [SerializeField] private bool isPersistent = true;

        private bool isInitializing;

        /// <summary> OnInitializeAsync가 끝났는지 여부 </summary>
        public bool IsInitialized { get; private set; }

        /// <summary> 내가 등록된 진짜 싱글톤인지 여부. (중복으로 거절된 인스턴스는 false) </summary>
        private bool IsRegistered => SingletonCore<T>.IsRegistered((T)this);

        #region ISingleton

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            if (!IsRegistered || IsInitialized) return;

            // 중복 호출 시 진행 중인 초기화가 끝날 때까지 대기
            if (isInitializing)
            {
                await UniTask.WaitUntil(() => IsInitialized, cancellationToken: ct);
                return;
            }

            isInitializing = true;

            try
            {
                await OnInitializeAsync(ct);
                IsInitialized = true;
            }
            finally { isInitializing = false; }
        }

        /// <summary> 실제 초기화. 동기 서비스는 override 하지 않거나 UniTask.CompletedTask를 반환합니다. </summary>
        protected virtual UniTask OnInitializeAsync(CancellationToken ct) => UniTask.CompletedTask;

        #endregion

        #region Unity Messages

        protected void Awake()
        {
            // 거절되면 이미 파괴 예정이므로 어떤 훅도 실행하지 않습니다.
            if (!SingletonCore<T>.TryRegister((T)this, isPersistent)) return;

            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;

            OnAwake();
        }

        protected void OnEnable()
        {
            if (AppState.IsQuitting || !IsRegistered) return;
            OnEnabled();
        }

        protected void Start()
        {
            if (AppState.IsQuitting || !IsRegistered) return;
            OnStart();
        }

        protected void OnDisable()
        {
            if (AppState.IsQuitting || !IsRegistered) return;
            OnDisabled();
        }

        protected void OnDestroy()
        {
            // 중복 인스턴스는 OnAwake를 실행하지 않았으므로 정리도 하지 않습니다.
            if (!IsRegistered) return;

            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;

            IsInitialized = false;
            OnDestroyed();
            SingletonCore<T>.Unregister((T)this);
        }

        private void HandleActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            if (AppState.IsQuitting) return;
            OnActiveSceneChanged(oldScene, newScene);
        }

        #endregion

        #region Hooks

        protected virtual void OnAwake() { }
        protected virtual void OnEnabled() { }
        protected virtual void OnStart() { }
        protected virtual void OnDisabled() { }
        protected virtual void OnDestroyed() { }

        /// <summary> 활성 씬이 바뀔 때 호출됩니다. (종료 중 제외) </summary>
        protected virtual void OnActiveSceneChanged(Scene oldScene, Scene newScene) { }

        #endregion
    }
}