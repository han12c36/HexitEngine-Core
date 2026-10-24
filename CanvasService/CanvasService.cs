using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace HexitEngine
{
    /// <summary>
    /// 캔버스 등록 / 조회 서비스. Bootstrap 씬에서 시작하며 전역 캔버스를 가지고 있습니다.
    ///
    /// - 일반 캔버스: 타입당 하나. 하위 전역 캔버스는 OnAwake에서, 각 씬의 캔버스는 LocalCanvas가 스스로 등록합니다.
    /// - 로딩 화면: 부트스트랩 초기화(OnInitializeAsync) 때 BootstrapConfig에 지정된 도착 씬별 프리팹을 자기 아래에 미리 생성해 보관합니다. (비활성)
    ///   같은 클래스로 여러 프리팹을 만들 수 있도록 일반 캔버스 목록과 따로 보관하며, 보이고 걷는 것은 HEXITEngine이 합니다.
    /// </summary>
    public sealed class CanvasService : LocalSingleton<CanvasService>
    {
        private readonly Dictionary<Type, LocalCanvas> canvases = new();

        /// <summary> 로딩 화면: 프리팹 → 미리 생성한 인스턴스 </summary>
        private readonly Dictionary<LoadingCanvasBase, LoadingCanvasBase> loadingCanvases = new();

        protected override void OnAwake()
        {
            foreach (var localCanvas in GetComponentsInChildren<LocalCanvas>(true))
            {
                RegisterLocalCanvas(localCanvas);
            }
        }

        /// <summary> BootstrapConfig에 지정된 로딩 화면을 미리 생성합니다. (부트스트랩 초기화 순서에 따라 HEXITEngine보다 먼저 실행) </summary>
        protected override UniTask OnInitializeAsync(CancellationToken ct)
        {
            CreateLoadingCanvases();
            return UniTask.CompletedTask;
        }

        protected override void OnDestroyed()
        {
            canvases.Clear();
            loadingCanvases.Clear();
        }

        internal void RegisterLocalCanvas(LocalCanvas localCanvas)
        {
            // 로딩 화면처럼 따로 보관하는 캔버스는 일반 목록에 등록하지 않음
            if (localCanvas == null || !localCanvas.CanRegisterToCanvasService) return;

            var key = localCanvas.GetType();

            if (canvases.TryGetValue(key, out var registered) && registered != null)
            {
                if (!ReferenceEquals(registered, localCanvas))
                {
                    Dev.LogError($"{key.Name} 캔버스가 이미 등록되어 있어 '{localCanvas.name}'은(는) 등록하지 않습니다.");
                }

                return;
            }

            canvases[key] = localCanvas;
        }

        internal void UnregisterLocalCanvas(LocalCanvas localCanvas)
        {
            if (localCanvas == null) return;

            var key = localCanvas.GetType();

            if (canvases.TryGetValue(key, out var registered) && ReferenceEquals(registered, localCanvas))
            {
                canvases.Remove(key);
            }
        }

        /// <summary>
        /// 등록된 캔버스를 가져옵니다. 없으면 null
        /// 정확한 타입이 없으면 상속한 타입에서 찾습니다.
        /// </summary>
        public T GetCanvas<T>() where T : LocalCanvas
        {
            if (canvases.TryGetValue(typeof(T), out var canvas) && canvas != null) return canvas as T;

            foreach (var registered in canvases.Values)
            {
                if (registered is T typed && typed != null) return typed;
            }

            return null;
        }

        public bool TryGetCanvas<T>(out T canvas) where T : LocalCanvas
        {
            canvas = GetCanvas<T>();
            return canvas != null;
        }

        public bool IsVisible<T>() where T : LocalCanvas
        {
            var canvas = GetCanvas<T>();
            return canvas != null && canvas.IsVisible;
        }

        public T Show<T>() where T : LocalCanvas
        {
            var canvas = GetCanvas<T>();

            if (canvas == null)
            {
                Dev.LogWarn($"{typeof(T).Name}을(를) 찾을 수 없습니다.");
                return null;
            }

            canvas.Show();
            return canvas;
        }

        public void Hide<T>() where T : LocalCanvas
        {
            var canvas = GetCanvas<T>();
            if (canvas != null) canvas.Hide();
        }

        /// <summary> 등록된 일반 캔버스를 모두 숨깁니다. (로딩 화면은 HEXITEngine이 관리하므로 제외) </summary>
        public void HideAll()
        {
            var canvases = new List<LocalCanvas>(this.canvases.Values);

            foreach (var canvas in canvases)
            {
                if (canvas != null) canvas.Hide();
            }
        }

        #region Loading Canvas

        /// <summary> 도착 씬에 맞는 로딩 화면. 지정되지 않았으면 Default, 그것도 없으면 null </summary>
        public LoadingCanvasBase GetLoadingCanvas(string sceneName)
        {
            var config = BootstrapConfig.Instance;
            var prefab = config != null ? config.GetLoadingCanvasPrefab(sceneName) : null;

            if (prefab == null) return null;

            return loadingCanvases.TryGetValue(prefab, out var instance) && instance != null ? instance : null;
        }

        private void CreateLoadingCanvases()
        {
            var config = BootstrapConfig.Instance;

            if (config == null)
            {
                Dev.LogWarn("BootstrapConfig가 없어 로딩 화면을 만들지 않습니다.");
                return;
            }

            foreach (var prefab in config.LoadingCanvasPrefabs)
            {
                if (loadingCanvases.ContainsKey(prefab)) continue;

                var instance = Instantiate(prefab, transform);
                instance.name = prefab.name;
                instance.gameObject.SetActive(false);

                loadingCanvases.Add(prefab, instance);
            }

            if (loadingCanvases.Count == 0) Dev.LogWarn("BootstrapConfig에 로딩 화면이 지정되지 않아 로딩 화면 없이 씬을 전환합니다.");
        }

        #endregion
    }
}