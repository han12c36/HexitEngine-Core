using UnityEngine;

namespace HexitEngine
{
    [DisallowMultipleComponent]
    public class LocalCanvas : MonoBehaviour
    {
        public bool IsVisible => gameObject.activeSelf;

        protected void Awake()
        {
            if (CanvasService.HasInstance) CanvasService.Singleton.RegisterLocalCanvas(this);

            OnInitialize();
        }

        protected void OnDestroy()
        {
            if (CanvasService.HasInstance) CanvasService.Singleton.UnregisterLocalCanvas(this);
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