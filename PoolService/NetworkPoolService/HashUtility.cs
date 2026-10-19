namespace HexitEngine 
{
    public static class HashUtility
    {
        /// <summary> 플랫폼과 실행 환경에 상관없이 항상 동일한 값을 반환하는 FNV-1a 32-bit 해시 함수입니다. </summary>
        public static int GetHashCode(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;

            unchecked // 오버플로우를 허용하여 성능을 높이고 에러를 방지합니다.
            {
                uint hash = 2166136261; // FNV 오프셋 (기본값)

                foreach (char c in str)
                {
                    hash = (hash ^ c) * 16777619; // FNV 소수 (Prime)
                }

                return (int)hash;
            }
        }
    }

}