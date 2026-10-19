using UnityEngine;

namespace HexitEngine 
{
    /// <summary>
    /// LocalSingleton과 ServerSingleton이 공유하는 싱글톤 등록/해제 로직.
    /// 두 베이스는 부모가 MonoBehaviour / NetworkBehaviour로 달라 공통 부모를 둘 수 없으므로,
    /// 실제 로직은 이 클래스 한 곳에 두고 양쪽 베이스는 호출만.
    /// 제네릭 static 클래스는 제네릭 인자 마다 별도의 static 저장소를 가짐을 활용.
    /// 모든 메서드는 Unity 메인 스레드에서만 호출된다고 가정합니다. (Unity 오브젝트는 메인 스레드 전용이므로 lock 불필요)
    /// </summary>
    internal static class SingletonCore<T> where T : Component
    {
        /// <summary> 실제로 등록된 인스턴스 </summary>
        private static T instance;

        /// <summary>외부 공개용 인스턴스. 종료 중에는 null 반환.</summary>
        public static T Instance => AppState.IsQuitting ? null : instance;

        /// <summary> 외부에서 사용 가능한 인스턴스가 있는지 여부. 종료 중에는 false 반환. </summary>
        public static bool HasInstance => Instance != null;

        /// <summary> 자신을 싱글톤으로 등록합니다. 베이스 클래스의 Awake에서 호출됩니다. </summary>
        /// <returns> false면 자신은 파괴 예정이므로, 호출한 베이스는 OnAwake 등 어떤 훅도 실행하면 안 됩니다. </returns>
        public static bool TryRegister(T self, bool persistent)
        {
            // 1. 종료 중에 새로 생성된 인스턴스는 등록하지 않습니다.
            if (AppState.IsQuitting)
            {
                DestroyRejected(self, "종료 중 생성");
                return false;
            }

            // 2. 이미 같은 인스턴스로 등록되어 있으면 성공으로 처리합니다. (중복 호출 방어)
            if (instance == self) return true;

            // 3. 다른 인스턴스가 이미 살아 있으면 자신을 파괴합니다.
            if (instance != null)
            {
                DestroyRejected(self, $"중복 인스턴스 (기존: {instance.gameObject.name}, 씬: {instance.gameObject.scene.name})");
                return false;
            }

            // 4. 등록
            instance = self;

            // 5. persistent 처리
            if (persistent)
            {
                if (self.transform.parent != null) self.transform.SetParent(null);
                Object.DontDestroyOnLoad(self.gameObject);
            }

            return true;
        }

        /// <summary> self가 현재 등록된 인스턴스인지 여부. 종료 중에도 실제 값으로 판단합니다. </summary>
        public static bool IsRegistered(T self) => self != null && instance == self;

        /// <summary> 등록을 해제합니다. 베이스 클래스의 OnDestroy에서 호출됩니다. </summary>
        public static void Unregister(T self)
        {
            if (instance == self) instance = null;
        }

        /// <summary> 등록이 거절된 인스턴스를 파괴합니다. </summary>
        private static void DestroyRejected(T self, string reason)
        {
            Debug.LogWarning($"[{typeof(T).Name}] 등록 거절 후 파괴: {self.gameObject.name} ({reason})", self);

            self.gameObject.SetActive(false);
            Object.Destroy(self.gameObject);
        }
    }
}