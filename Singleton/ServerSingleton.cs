using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace HexitEngine
{
    /// <summary> 네트워크 싱글톤 베이스
    /// - LocalSingleton과 동일한 훅 규칙을 따릅니다.
    /// - 네트워크 시점 처리는 OnNetSpawn/OnNetDespawn을 override 합니다.
    /// - IsInitialized는 스폰 여부와 무관합니다. 부트스트랩 시점에는 스폰 전이므로,
    ///   OnInitializeAsync에는 스폰 없이 가능한 로컬 준비만 넣고, 네트워크 의존 작업은 OnNetSpawn에서 합니다.
    /// - OnActiveSceneChanged는 서버에서만 호출됩니다.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class ServerSingleton<T> : NetworkBehaviour, ISingleton where T : ServerSingleton<T>
    {
        /// <summary> 싱글톤 인스턴스. 종료 중에는 null. 스폰 여부는 IsSpawned로 확인합니다. </summary>
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

        /// <summary> 스폰과 무관한 로컬 초기화. </summary>
        protected virtual UniTask OnInitializeAsync(CancellationToken ct) => UniTask.CompletedTask;

        #endregion

        #region Unity / NGO Messages

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

        public sealed override void OnNetworkSpawn()
        {
            if (AppState.IsQuitting || !IsRegistered) return;
            OnNetSpawn();
        }

        public sealed override void OnNetworkDespawn()
        {
            if (AppState.IsQuitting || !IsRegistered) return;
            OnNetDespawn();
        }

        protected void OnDisable()
        {
            if (AppState.IsQuitting || !IsRegistered) return;
            OnDisabled();
        }

        // NGO의 OnDestroy는 base 호출이 필수이므로 잠금.
        public sealed override void OnDestroy()
        {
            if (IsRegistered)
            {
                SceneManager.activeSceneChanged -= HandleActiveSceneChanged;

                IsInitialized = false;
                OnDestroyed();
                SingletonCore<T>.Unregister((T)this);
            }

            base.OnDestroy();
        }

        private void HandleActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            // 서버 권한이 있을 때만 씬 변경 로직을 실행
            if (AppState.IsQuitting || !IsServer) return;
            OnActiveSceneChanged(oldScene, newScene);
        }

        #endregion

        #region Hooks

        protected virtual void OnAwake() { }
        protected virtual void OnEnabled() { }
        protected virtual void OnStart() { }
        protected virtual void OnNetSpawn() { }
        protected virtual void OnNetDespawn() { }
        protected virtual void OnDisabled() { }
        protected virtual void OnDestroyed() { }

        /// <summary> 활성 씬이 바뀔 때 서버에서만 호출됩니다. (종료 중 제외) </summary>
        protected virtual void OnActiveSceneChanged(Scene oldScene, Scene newScene) { }

        #endregion
    }
}