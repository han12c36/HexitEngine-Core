using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HexitEngine
{
    /// <summary>
    /// 게임 시작 시 서비스들을 정해진 순서로 초기화하고 다음 씬으로 이동합니다.
    /// 초기화 순서: NetworkManager 확인 → BootstrapConfig.ServiceOrder 순서 → (목록에 없는 서비스) → HEXITEngine
    /// 서비스는 BootstrapScene에 배치되어 있어야 합니다. 
    /// isPersistent로 DontDestroyOnLoad에 옮겨진 상태에서 찾습니다.
    /// 서비스 하나라도 실패하거나 시간 초과되면 부팅을 중단합니다.
    /// </summary>
    [AddComponentMenu("")]
    public class BootstrapRunner : MonoBehaviour
    {
        private const int STEP_TIMEOUT_SECONDS = 10;

        private static BootstrapRunner s_Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic() => s_Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InterceptAndBoot()
        {
            if (s_Instance != null) return;

            BootstrapConfig config = null;
            var assets = Resources.FindObjectsOfTypeAll<BootstrapConfig>();
            if (assets.Length > 0)
            {
                config = assets[0];
            }

#if UNITY_EDITOR
            if (config == null)
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(BootstrapConfig)}");
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    config = UnityEditor.AssetDatabase.LoadAssetAtPath<BootstrapConfig>(path);
                }
            }
#endif

            if (config == null)
            {
                Dev.LogError("<color=red>[Fatal]</color> BootstrapConfig를 찾을 수 없습니다!");
                return;
            }

            var go = new GameObject("[HexitEngine] BootstrapRunner") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<BootstrapRunner>();

            var destroyToken = s_Instance.GetCancellationTokenOnDestroy();
            s_Instance.BootSequenceAsync(config, destroyToken).Forget();
        }

        private async UniTaskVoid BootSequenceAsync(BootstrapConfig config, CancellationToken destroyToken)
        {
            try
            {
                // Phase 1: 빌드 세팅 유효성 검사 (강제 이동이 켜져있을 때만 검사)
                if (config.ForceSceneRouting)
                {
                    if (string.IsNullOrEmpty(config.BootstrapSceneName))
                        throw new Exception("BootstrapConfig에 Bootstrap Scene이 지정되지 않았습니다.");

                    if (SceneUtility.GetBuildIndexByScenePath(config.BootstrapSceneName) == -1)
                        throw new Exception($"부트스트랩 씬 '{config.BootstrapSceneName}'이 Build Settings에 없습니다.");

                    if (SceneUtility.GetBuildIndexByScenePath(config.NextSceneName) == -1)
                        throw new Exception($"타겟 씬 '{config.NextSceneName}'이 Build Settings에 없습니다.");
                }

                // Phase 2: 씬 격리 및 동기화 (강제 이동이 켜져있을 때만 부트스트랩 씬으로 이동)
                if (config.ForceSceneRouting && !config.IsBootstrapScene(SceneManager.GetActiveScene().name))
                {
                    await SceneManager.LoadSceneAsync(config.BootstrapSceneName)
                        .ToUniTask(timing: PlayerLoopTiming.Update, cancellationToken: destroyToken)
                        .Timeout(TimeSpan.FromSeconds(STEP_TIMEOUT_SECONDS));
                }

                // 씬 오브젝트가 완전히 준비될 때까지 대기 (모든 Awake 완료 → 싱글턴 등록 완료)
                await UniTask.WaitUntil(() => SceneManager.GetActiveScene().isLoaded, cancellationToken: destroyToken);
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: destroyToken);

                // Phase 3: [고정 1번] NetworkManager 확인
                if (NetworkManager.Singleton == null)
                {
                    throw new Exception($"{BootstrapConfig.FIRST_FIXED_NAME}가 부트스트랩 씬에 없습니다.");
                }

                // Phase 4: 서비스 순서대로 초기화 (결과는 리포트 하나로 출력)
                var services = BuildInitializationOrder(config);
                var report = new StringBuilder();
                long totalMs = 0;
                Exception failure = null;

                for (int i = 0; i < services.Count; i++)
                {
                    string serviceName = services[i].GetType().Name;

                    // 앞에서 실패했다면 나머지는 실행하지 않음
                    if (failure != null)
                    {
                        report.Append($"\n{i + 1}. {serviceName} - Skipped");
                        continue;
                    }

                    var result = await InitializeServiceAsync(services[i], destroyToken);
                    totalMs += result.ElapsedMs;

                    if (result.Error == null)
                    {
                        report.Append($"\n{i + 1}. {serviceName} - Succeeded ({result.ElapsedMs} ms)");
                    }
                    else
                    {
                        failure = result.Error;
                        report.Append($"\n{i + 1}. {serviceName} - Failed ({DescribeError(result.Error)})");
                    }
                }

                // 실패: 리포트와 원인 예외를 에러로 남기고 부팅 중단
                if (failure != null)
                {
                    Dev.LogError($"Singleton Init Report ({totalMs} ms){report}\n\n{failure}");
                    return;
                }

                // Phase 5: 최종 라우팅 (강제 이동이 켜져있을 때만 NextScene으로 이동)
                report.Append(config.ForceSceneRouting
                    ? $"\n→ 다음 씬: {config.NextSceneName}"
                    : "\n→ 씬 라우팅 꺼짐: 현재 씬 유지");

                Dev.Log($"Singleton Init Report ({totalMs} ms){report}");

                if (config.ForceSceneRouting)
                {
                    await LoadNextSceneAsync(config, destroyToken);
                }
            }
            catch (TimeoutException)
            {
                Dev.LogError("<color=red>[Critical]</color> 초기화 시간 초과로 부팅이 중단되었습니다.");
            }
            catch (OperationCanceledException)
            {
                Dev.LogWarn("시스템 종료로 인해 작업이 중단되었습니다.");
            }
            catch (Exception e)
            {
                Dev.LogError($"<color=red>[Fatal Error]</color> {e.Message}");
            }
            finally
            {
                if (gameObject != null) Destroy(gameObject);
            }
        }

        /// <summary>
        /// 다음 씬(보통 MainMenu)으로 이동합니다. 앱 시작 때 단 한 번만 실행됩니다.
        /// BootstrapConfig의 Show Loading On Boot가 켜져 있으면 로딩 화면을 보입니다. (HEXITEngine이 초기화된 뒤라 사용 가능)
        /// </summary>
        private static async UniTask LoadNextSceneAsync(BootstrapConfig config, CancellationToken destroyToken)
        {
            var hexit = config.ShowLoadingOnBoot && HEXITEngine.HasInstance ? HEXITEngine.Singleton : null;

            var operation = SceneManager.LoadSceneAsync(config.NextSceneName);
            if (operation == null) throw new Exception($"타겟 씬 '{config.NextSceneName}'을 로드하지 못했습니다.");

            if (hexit != null) hexit.BeginLoading(config.NextSceneName, operation);

            try
            {
                await operation
                    .ToUniTask(timing: PlayerLoopTiming.Update, cancellationToken: destroyToken)
                    .Timeout(TimeSpan.FromSeconds(STEP_TIMEOUT_SECONDS));

                if (hexit != null) hexit.CompleteLoadingScene();
            }
            catch
            {
                if (hexit != null) hexit.CancelLoading();
                throw;
            }
        }

        /// <summary> 서비스 하나의 초기화 결과. Error가 null이면 성공 </summary>
        private readonly struct InitResult
        {
            public readonly long ElapsedMs;
            public readonly Exception Error;

            public InitResult(long elapsedMs, Exception error)
            {
                ElapsedMs = elapsedMs;
                Error = error;
            }
        }

        /// <summary>
        /// 서비스 하나를 초기화하고 결과(걸린 시간, 실패 원인)를 반환합니다.
        /// 실패/시간 초과는 예외 대신 결과로 돌려주어 리포트에 담습니다. 앱 종료로 인한 취소만 그대로 올립니다.
        /// </summary>
        private static async UniTask<InitResult> InitializeServiceAsync(MonoBehaviour service, CancellationToken destroyToken)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                await ((ISingleton)service).InitializeAsync(destroyToken).Timeout(TimeSpan.FromSeconds(STEP_TIMEOUT_SECONDS));
                return new InitResult(stopwatch.ElapsedMilliseconds, null);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                return new InitResult(stopwatch.ElapsedMilliseconds, e);
            }
        }

        /// <summary> 리포트 한 줄에 들어갈 짧은 실패 사유 </summary>
        private static string DescribeError(Exception error)
        {
            if (error is TimeoutException) return $"Timeout {STEP_TIMEOUT_SECONDS}s";
            return $"{error.GetType().Name}: {error.Message}";
        }

        /// <summary>
        /// 초기화 순서를 만듭니다: Config 순서 → 목록에 없는 서비스(경고) → HEXITEngine.
        /// 비활성 오브젝트는 Awake가 실행되지 않아 싱글턴으로 등록되지 않으므로 건너뜁니다.
        /// </summary>
        private static List<MonoBehaviour> BuildInitializationOrder(BootstrapConfig config)
        {
            // 1. 현재 로드된 모든 씬(DontDestroyOnLoad 포함)에서 활성 상태인 ISingleton 수집
            var found = new Dictionary<string, MonoBehaviour>();
            //var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
            foreach (var behaviour in behaviours)
            {
                if (behaviour is not ISingleton) continue;

                if (!behaviour.gameObject.activeInHierarchy)
                {
                    Dev.LogWarn($"비활성 오브젝트의 서비스는 건너뜁니다: {behaviour.GetType().Name} ({behaviour.name})");
                    continue;
                }

                found.TryAdd(behaviour.GetType().FullName, behaviour);
            }

            var ordered = new List<MonoBehaviour>();

            // 2. Config 순서
            foreach (string typeName in config.ServiceOrder)
            {
                if (string.IsNullOrEmpty(typeName) || typeName == BootstrapConfig.LastFixedTypeName) continue;

                if (found.Remove(typeName, out var service)) ordered.Add(service);
                else Dev.LogWarn($"초기화 순서에 있지만 씬에서 찾을 수 없는 서비스: {typeName}");
            }

            // 3. 마지막 고정 항목은 따로 빼둠
            found.Remove(BootstrapConfig.LastFixedTypeName, out var hexitEngine);

            // 4. 목록에 없는 서비스: 경고 후 HEXITEngine 앞에 초기화 (빠뜨려도 초기화는 되도록)
            foreach (var pair in found)
            {
                Dev.LogWarn($"초기화 순서에 없는 서비스입니다. Settings > Bootstrap에서 BootstrapScene을 다시 불러오세요: {pair.Key}");
                ordered.Add(pair.Value);
            }

            // 5. [고정 마지막] HEXITEngine
            if (hexitEngine != null) ordered.Add(hexitEngine);
            else Dev.LogWarn("HEXITEngine이 씬에 없습니다.");

            return ordered;
        }
    }
}