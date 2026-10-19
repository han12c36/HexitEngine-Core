using UnityEngine;

namespace HexitEngine
{
    public class LocalObjectPool : BaseObjectPool<ILocalPoolable>
    {
        public LocalObjectPool(GameObject prefab, int prewarm, int poolKey, Transform parent = null) : base(prefab, poolKey, parent)
        {
            InitializePrewarm(prewarm);
        }

        protected override ILocalPoolable CreateNewInstance()
        {
            var poolable = CreateBaseInstance();
            if (poolable == null) return null;

            poolable.GameObject.SetActive(false);
            poolable.OnCreate();

            return poolable;
        }

        /// <summary> 대기열에서 꺼내거나, 비었으면 새로 만들어 지정 위치에 활성화합니다. </summary>
        public ILocalPoolable Get(Vector3 position, Quaternion rotation)
        {
            var poolable = DequeueAlive() ?? CreateNewInstance();
            if (poolable == null) return null;

            poolable.Transform.SetPositionAndRotation(position, rotation);
            poolable.GameObject.SetActive(true);

            poolable.OnRental(position, rotation);

            return poolable;
        }

        protected override void HandleReturn(ILocalPoolable item)
        {
            item.GameObject.SetActive(false);
        }

        /// <summary> 이 풀이 만든 모든 인스턴스를 파괴합니다. (대여 중인 것 포함) </summary>
        public override void Clear()
        {
            foreach (var item in allInstances)
            {
                if (item is Component comp && comp != null) Object.Destroy(comp.gameObject);
            }

            queue.Clear();
            allInstances.Clear();
        }
    }
}