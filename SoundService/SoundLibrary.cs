using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HexitEngine
{
    /// <summary>
    /// SoundData 목록. SoundService가 id로 SoundData를 찾을 때 사용합니다.
    /// 목록 관리(자동 수집 / 생성 / 편집 / 삭제)는 Settings > Sound 탭에서 합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundLibrary", menuName = "Audio/SoundLibrary")]
    public class SoundLibrary : ScriptableObject
    {
        public List<SoundData> sounds = new List<SoundData>();

        private Dictionary<string, SoundData> soundCache;

        private void OnEnable()
        {
            soundCache = null;
        }

        private void InitCache()
        {
            soundCache = new Dictionary<string, SoundData>();

            foreach (var s in sounds)
            {
                if (s == null || string.IsNullOrEmpty(s.id)) continue;

                if (!soundCache.ContainsKey(s.id))
                    soundCache[s.id] = s;
            }
        }

        public SoundData GetSoundData(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (soundCache == null) InitCache();

            if (soundCache.TryGetValue(id, out SoundData cachedData))
            {
                return cachedData;
            }

            SoundData foundData = sounds.FirstOrDefault(s => s != null && s.id.Contains(id));

            if (foundData != null)
            {
                soundCache[id] = foundData;
            }

            return foundData;
        }
    }
}