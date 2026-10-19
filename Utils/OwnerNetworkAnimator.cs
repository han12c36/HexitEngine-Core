using Unity.Netcode.Components;

namespace H_Lib.Netcode.OwnerAuthoritativeMode
{
    /// <summary>
    /// 애니메이터 상태를 클라이언트(소유자)가 제어하는 Owner 권한 방식의 NetworkAnimator입니다.
    /// 서버가 아닌 로컬 플레이어(소유자)가 애니메이션 상태를 직접 업데이트하도록 설정합니다.
    /// 이 방식은 로컬 플레이어에게 빠른 애니메이션 피드백을 제공할 수 있지만,
    /// 비소유 클라이언트나 서버와의 상태 불일치 위험이 있으므로 주의가 필요합니다.
    /// </summary>
    public class OwnerNetworkAnimator : NetworkAnimator { protected override bool OnIsServerAuthoritative() => false; }
}