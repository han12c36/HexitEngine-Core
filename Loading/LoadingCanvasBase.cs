using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HexitEngine
{
    /// <summary>
    /// [나만] 씬 전환 로딩 화면의 베이스. 화면 구성(이미지, 팁, 진행바 등)과 연출은 게임마다 자유롭게 만듭니다.
    /// 엔진(HEXITEngine)은 "언제" 보이고 걷을지만 정하고, "어떻게" 보이고 사라질지와 최소 표시 시간은 이 화면이 정합니다.
    ///
    /// 만들기
    /// - 이 클래스를 상속한 컴포넌트를 Canvas 프리팹의 루트에 붙여 Assets/Project/Loading/ 아래에 둡니다. (프리팹 하나 = Canvas 하나)
    /// - BootstrapConfig의 Loading 항목에 도착 씬별(MainMenu / Ready / Game) 프리팹을 지정합니다. 비우면 Default를 사용합니다.
    ///
    /// 코드가 강제하는 것 (개발자가 맞출 필요 없음. 인스펙터에서 바꿔도 다시 맞춰짐)
    /// - Canvas, GraphicRaycaster: 없으면 자동 추가 (RequireComponent)
    /// - Render Mode: Screen Space - Overlay. 씬마다 카메라가 바뀌므로 카메라에 의존하지 않음
    /// - Sort Order: SORTING_ORDER(32000) + Override Sorting. 모든 씬 UI 위를 덮음 (CanvasService 아래에 중첩되어도 적용)
    /// - 입력 차단: 화면 전체를 덮는 투명 이미지(INPUT_BLOCKER_NAME)를 맨 뒤에 자동 생성. 디자인과 상관없이 뒤쪽 클릭을 막음
    /// - 보일 때 선택 해제: 키보드/패드로 뒤쪽 버튼이 눌리지 않도록 EventSystem의 선택을 비움
    ///
    /// 연출 (선택)
    /// - PlayShowAsync / PlayHideAsync를 override 해서 페이드 등을 넣습니다. 기본은 즉시 켜고 끕니다.
    /// - 연출 도중 다른 로딩이 시작되거나 즉시 걷어야 하면(연결 끊김 등) ct가 취소됩니다.
    ///   이때 엔진이 Show()/Hide()로 상태를 바로 맞추므로, 다음 PlayShowAsync는 중간 상태(반쯤 투명 등)에서 시작될 수 있습니다.
    /// - 나타남 연출은 씬 로드와 동시에 진행됩니다. (네트워크 씬 전환은 기다려 주지 않음) 그래서 즉시 덮는 것을 권장합니다.
    ///
    /// 동작 (직접 부를 필요 없음)
    /// - CanvasService가 부트스트랩 초기화 때 BootstrapConfig에 지정된 프리팹을 미리 생성해 둡니다. (비활성)
    /// - 같은 클래스로 여러 프리팹을 만들 수 있도록 일반 캔버스 목록(타입당 하나)이 아니라 로딩 화면 목록(도착 씬별)에 따로 보관됩니다.
    /// - 순서: PlayShowAsync → SetProgress(0 ~ 1) → (씬 로드 + 매니저 준비 + 최소 표시 시간) → PlayHideAsync
    /// </summary>
    [RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
    public abstract class LoadingCanvasBase : LocalCanvas
    {
        /// <summary> 로딩 화면의 Sort Order. Canvas 최대값(32767) 근처라 일반 UI보다 항상 위. 이보다 높은 UI는 만들지 않습니다. </summary>
        public const int SORTING_ORDER = 32000;

        /// <summary> 자동으로 만드는 입력 차단 이미지 이름 </summary>
        public const string INPUT_BLOCKER_NAME = "[InputBlocker]";

        [Tooltip("최소 표시 시간(초). 로딩이 이보다 빨리 끝나면 이 시간까지 유지해 깜빡임을 막습니다. 느린 로딩을 더 늦추지는 않습니다.")]
        [SerializeField, Min(0f)] private float minDisplaySeconds = 0.5f;

        protected sealed override bool RegisterToCanvasService => false;

        /// <summary> 최소 표시 시간(초). 팁 길이 등에 따라 코드로 정하려면 override 합니다. </summary>
        public virtual float MinDisplaySeconds => minDisplaySeconds;

        #region Override Points

        /// <summary> 나타나기. 기본은 즉시 켭니다. 연출을 넣으려면 override 하고, 먼저 Show()를 호출한 뒤 연출합니다. </summary>
        public virtual UniTask PlayShowAsync(CancellationToken ct)
        {
            Show();
            return UniTask.CompletedTask;
        }

        /// <summary> 사라지기. 기본은 즉시 끕니다. 연출을 넣으려면 override 하고, 연출이 끝난 뒤 Hide()를 호출합니다. </summary>
        public virtual UniTask PlayHideAsync(CancellationToken ct)
        {
            Hide();
            return UniTask.CompletedTask;
        }

        /// <summary> 씬 로드 진행률 (0 ~ 1). 진행률을 보여주지 않는 화면이면 override 하지 않아도 됩니다. </summary>
        public virtual void SetProgress(float progress) { }

        /// <summary> 처음 생성될 때 (강제 설정이 적용된 뒤). OnInitialize 대신 사용 </summary>
        protected virtual void OnLoadingInitialize() { }

        /// <summary> 보일 때마다 (Show 직후). OnShow 대신 사용 </summary>
        protected virtual void OnLoadingShow() { }

        /// <summary> 숨겨질 때마다 (Hide 직후). OnHide 대신 사용 </summary>
        protected virtual void OnLoadingHide() { }

        #endregion

        #region LocalCanvas (Sealed)

        protected sealed override void OnInitialize()
        {
            ApplyCanvasSettings();
            EnsureInputBlocker();

            OnLoadingInitialize();
        }

        protected sealed override void OnShow()
        {
            // 키보드/패드 입력으로 뒤쪽 UI가 눌리지 않도록 선택 해제
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            OnLoadingShow();
        }

        protected sealed override void OnHide() => OnLoadingHide();

        #endregion

        #region Enforced Settings

        /// <summary> Render Mode, Sort Order를 강제합니다. </summary>
        private void ApplyCanvasSettings()
        {
            var canvas = GetComponent<Canvas>();

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = SORTING_ORDER;
        }

        /// <summary> 화면 전체를 덮는 투명 입력 차단 이미지를 맨 뒤(첫 자식)에 둡니다. 없으면 만듭니다. </summary>
        private void EnsureInputBlocker()
        {
            var blocker = transform.Find(INPUT_BLOCKER_NAME);

            if (blocker == null)
            {
                var blockerObject = new GameObject(INPUT_BLOCKER_NAME, typeof(RectTransform), typeof(Image));
                blocker = blockerObject.transform;
                blocker.SetParent(transform, false);
            }

            blocker.SetAsFirstSibling();

            var rect = (RectTransform)blocker;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // 투명해도 Raycast Target이면 클릭을 받음
            var image = blocker.GetComponent<Image>();
            if (image == null) image = blocker.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
        }

#if UNITY_EDITOR
        /// <summary> [에디터] 프리팹을 편집할 때도 강제 설정을 보이도록 맞춥니다. (입력 차단 이미지는 실행 시 생성) </summary>
        protected virtual void OnValidate()
        {
            if (TryGetComponent<Canvas>(out _)) ApplyCanvasSettings();
        }
#endif

        #endregion
    }
}