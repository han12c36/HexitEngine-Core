using UnityEngine;

namespace HexitEngine
{
    [DisallowMultipleComponent]
    public class LocalCanvas : MonoBehaviour
    {
        public bool IsVisible => gameObject.activeSelf;

        /// <summary> CanvasService에 등록할지. 엔진이 직접 관리하는 캔버스(로딩 화면 등)는 false </summary>
        protected virtual bool RegisterToCanvasService => true;

        /// <summary> [엔진 내부] CanvasService가 등록 여부를 확인할 때 사용 </summary>
        internal bool CanRegisterToCanvasService => RegisterToCanvasService;

        protected void Awake()
        {
            if (RegisterToCanvasService && CanvasService.HasInstance) CanvasService.Singleton.RegisterLocalCanvas(this);

            OnInitialize();
        }

        protected void OnDestroy()
        {
            if (RegisterToCanvasService && CanvasService.HasInstance) CanvasService.Singleton.UnregisterLocalCanvas(this);
        }

        public void Show()
        {
            if (IsVisible) return;

            gameObject.SetActive(true);
            OnShow();
        }

        public void Hide()
        {
            if (!IsVisible) return;

            gameObject.SetActive(false);
            OnHide();
        }

        protected virtual void OnInitialize() { }

        protected virtual void OnShow() { }

        protected virtual void OnHide() { }
    }
}