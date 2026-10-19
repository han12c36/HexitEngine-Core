using System.Collections.Generic;
using UnityEngine;

namespace HexitEngine
{
    public abstract class BaseObjectPool<T> where T : class, IPoolable
    {
        /// <summary> 이 풀의 키 (프리팹 이름의 해시) </summary>
        protected readonly int poolKey;

        /// <summary> 복제할 원본 프리팹 </summary>
        protected readonly GameObject prefab;

        /// <summary> 생성된 인스턴스의 부모. null이면 현재 활성 씬의 루트에 생성됩니다. </summary>
        protected readonly Transform parent;

        /// <summary> 대여 가능한 인스턴스 </summary>
        protected readonly Queue<T> queue = new();

        /// <summary> 이 풀이 만든 모든 인스턴스 (Clear 시 일괄 정리용) </summary>
        protected readonly List<T> allInstances = new();

        protected BaseObjectPool(GameObject prefab, int poolKey, Transform parent = null)
        {
            this.prefab = prefab;
            this.poolKey = poolKey;
            this.parent = parent;
        }

        /// <summary> 지정 개수만큼 미리 생성해 대기열에 넣습니다. </summary>
        protected void InitializePrewarm(int prewarm)
        {
            for (int i = 0; i < prewarm; i++)
            {
                var instance = CreateNewInstance();
                if (instance != null) queue.Enqueue(instance);
            }
        }

        /// <summary> 프리팹을 복제하고 T를 찾아 풀 키를 기록합니다. T가 없으면 파괴 후 null. </summary>
        protected T CreateBaseInstance()
        {
            var go = parent != null ? Object.Instantiate(prefab, parent) : Object.Instantiate(prefab);

            go.name = prefab.name;

            if (!go.TryGetComponent<T>(out var poolable))
            {
                Dev.LogError($"[Pool] '{prefab.name}'에 {typeof(T).Name} 컴포넌트가 없습니다.");
                Object.Destroy(go);
                return null;
            }

            poolable.PoolKey = poolKey;
            allInstances.Add(poolable);

            return poolable;
        }

        /// <summary> 대기열에서 살아 있는 인스턴스를 꺼냅니다. 외부에서 파괴된 인스턴스는 건너뛰며, 비었으면 null. </summary>
        protected T DequeueAlive()
        {
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                if (IsAlive(item)) return item;
            }

            return null;
        }

        /// <summary>
        /// 파괴되지 않은 인스턴스인지 검사합니다.
        /// 인터페이스 타입 변수의 == null은 Unity의 파괴 판정을 거치지 않으므로, UnityEngine.Object로 바꿔서 검사합니다.
        /// </summary>
        protected static bool IsAlive(T item)
        {
            return item is Object unityObject ? unityObject != null : item != null;
        }

        protected abstract T CreateNewInstance();

        /// <summary> 인스턴스를 풀에 반납합니다. 파괴되었거나 이미 반납된 인스턴스는 무시합니다. </summary>
        public void Return(T poolable)
        {
            if (!IsAlive(poolable) || queue.Contains(poolable)) return;

            poolable.OnReturn();
            HandleReturn(poolable);

            queue.Enqueue(poolable);
        }

        /// <summary> 반납 시 자식 클래스별 처리 (비활성화, 위치 초기화 등) </summary>
        protected abstract void HandleReturn(T item);

        /// <summary> 이 풀이 만든 모든 인스턴스를 정리합니다. </summary>
        public abstract void Clear();
    }
}