using Unity.Netcode;
using UnityEngine;

namespace H_Netcode
{
    public static class NetBufferSize
    {
        /// <summary>1KB - 아주 간단한 상태 (bool 몇 개, int 몇 개)</summary>
        public const int Tiny = 1024;

        /// <summary>4KB - 일반적인 메시지, 소규모 데이터</summary>
        public const int Small = 4096;

        /// <summary>16KB - 중간 규모 (Small로 부족할 때의 1순위 대안)</summary>
        public const int Medium = 16384;

        /// <summary>32KB - 꽤 많은 데이터 (Medium과 Large 사이)</summary>
        public const int Large = 32768;

        /// <summary>64KB - 대규모 스냅샷 (기존 Normal)</summary>
        public const int ExtraLarge = 65536;

        /// <summary>
        /// 128KB - NGO 단일 RPC 패킷의 안전한 최대 한계선.
        /// 이 이상은 Chunking(분할) 필요.
        /// </summary>
        public const int MaxSafety = 131072;

        // --- 2. 스마트 진단 도구 ---

        public static void Analyze(FastBufferWriter writer, string usageName)
        {
            int usedBytes = writer.Position;   // 실제 사용량
            int totalBytes = writer.Capacity;  // 할당량
            float usageRate = (float)usedBytes / totalBytes;

            // 적절한 추천 사이즈 찾기 로직
            string recommendedConstant = GetRecommendedConstant(usedBytes);

            // 1. [위험/부족] 90% 초과 -> 터지기 직전
            if (usageRate > 0.9f)
            {
                Debug.LogError($"[Buffer 진단] '{usageName}' 버퍼가 폭발 직전입니다! (위험)\n" +
                               $"사용량: {usedBytes}/{totalBytes} bytes ({usageRate:P1})\n" +
                               $"권장: '{recommendedConstant}' 이상으로 늘리세요.");
            }
            // 2. [낭비] 30% 미만 사용 -> 너무 큰 그릇 (단, Tiny/Small은 원래 작으니 예외)
            else if (totalBytes > Small && usageRate < 0.3f)
            {
                // 사용량이 현재 할당량보다 훨씬 낮은 단계의 버퍼로도 커버 가능한지 확인
                if (CanFitInSmallerBuffer(usedBytes, totalBytes, out string smallerSuggestion))
                {
                    Debug.LogWarning($"[Buffer 진단]  '{usageName}' 버퍼가 너무 큽니다. (낭비)\n" +
                                     $"사용량: {usedBytes}/{totalBytes} bytes ({usageRate:P1})\n" +
                                     $"권장: '{smallerSuggestion}'으로 줄여도 충분합니다.");
                }
            }
            // 3. [적절]
            else
            {
                Debug.Log($"[Buffer 진단] '{usageName}' 크기가 적절합니다. ({GetSizeName(totalBytes)})\n" +
                          $"사용량: {usedBytes}/{totalBytes} bytes ({usageRate:P1})");
            }
        }

        public static void ReportOverflow(int currentSize, string usageName)
        {
            Debug.LogError($"[Buffer 진단] '{usageName}' 쓰기 실패! ({currentSize} bytes 초과)\n" +
                           $"해결책: NetBufferSize.{GetNextSizeName(currentSize)} 이상으로 변경하세요.");
        }

        // --- 3. 내부 헬퍼 함수 (추천 로직) ---

        private static string GetRecommendedConstant(int neededBytes)
        {
            if (neededBytes <= Tiny) return nameof(Tiny);
            if (neededBytes <= Small) return nameof(Small);
            if (neededBytes <= Medium) return nameof(Medium);
            if (neededBytes <= Large) return nameof(Large);
            if (neededBytes <= ExtraLarge) return nameof(ExtraLarge);
            return nameof(MaxSafety);
        }

        private static bool CanFitInSmallerBuffer(int usedBytes, int currentTotal, out string suggestion)
        {
            // 여유율(Safety Margin) 20%를 고려해서 추천
            int safeNeed = (int)(usedBytes * 1.2f);

            suggestion = "";
            if (currentTotal > Medium && safeNeed <= Medium) { suggestion = nameof(Medium); return true; }
            if (currentTotal > Small && safeNeed <= Small) { suggestion = nameof(Small); return true; }
            if (currentTotal > Tiny && safeNeed <= Tiny) { suggestion = nameof(Tiny); return true; }

            return false;
        }

        private static string GetSizeName(int bytes)
        {
            if (bytes == Tiny) return nameof(Tiny);
            if (bytes == Small) return nameof(Small);
            if (bytes == Medium) return nameof(Medium);
            if (bytes == Large) return nameof(Large);
            if (bytes == ExtraLarge) return nameof(ExtraLarge);
            if (bytes == MaxSafety) return nameof(MaxSafety);
            return "Custom";
        }

        private static string GetNextSizeName(int bytes)
        {
            if (bytes <= Tiny) return nameof(Small);
            if (bytes <= Small) return nameof(Medium);
            if (bytes <= Medium) return nameof(Large);
            if (bytes <= Large) return nameof(ExtraLarge);
            return nameof(MaxSafety);
        }
    }
}