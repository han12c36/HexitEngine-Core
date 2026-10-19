using Unity.Netcode.Components;
using UnityEngine;

namespace H_Netcode
{
    /// <summary>
    /// 클라이언트 권한 기반(Client Authority)의 Transform 동기화를 지원하는 컴포넌트입니다.
    /// 
    /// 이 클래스는 NetworkTransform을 상속하며, 서버 권한(Server Authority)이 아닌
    /// 소유한 클라이언트(Owner Client)가 Transform 정보를 전파할 수 있도록 구성됩니다.
    /// 
    /// 이를 통해 로컬(소유자) 클라이언트는 자신의 위치(Position), 회전(Rotation), 스케일(Scale)을 
    /// 서버 및 다른 클라이언트에게 직접 동기화할 수 있습니다.
    /// 
    /// 주의: 서버가 위치 제어 권한을 가지지 않기 때문에, 신뢰할 수 없는 클라이언트가 
    /// 잘못된 데이터를 보낼 수 있으므로 보안상 주의가 필요합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class OwnerNetworkTransform : NetworkTransform { protected override bool OnIsServerAuthoritative() => false; }
}