using System;
using System.Collections.Generic;

namespace HexitEngine
{
    public sealed class CanvasService : LocalSingleton<CanvasService>
    {
        private readonly Dictionary<Type, LocalCanvas> canvases = new();

        protected override void OnAwake()
        {
            foreach (var localCanvas in GetComponentsInChildren<LocalCanvas>(true))
            {
                RegisterLocalCanvas(localCanvas);
            }
        }

        protected override void OnDestroyed() { canvases.Clear(); }

        internal void RegisterLocalCanvas(LocalCanvas localCanvas)
        {
            if (localCanvas == null) return;

            var key = localCanvas.GetType();

            if (canvases.TryGetValue(key, out var registered) && registered != null)
            {
                if (!ReferenceEquals(registered, localCanvas))
                {
                    Dev.LogError($"{key.Name} 캔버스가 이미 등록되어 있어 '{localCanvas.name}'은(는) 등록하지 않습니다.");
                }

                return;
            }

            canvases[key] = localCanvas;
        }

        internal void UnregisterLocalCanvas(LocalCanvas localCanvas)
        {
            if (localCanvas == null) return;

            var key = localCanvas.GetType();

            if (canvases.TryGetValue(key, out var registered) && ReferenceEquals(registered, localCanvas))
            {
                canvases.Remove(key);
            }
        }

        public T GetCanvas<T>() where T : LocalCanvas
        {
            return canvases.TryGetValue(typeof(T), out var canvas) && canvas != null ? canvas as T : null;
        }

        public bool TryGetCanvas<T>(out T canvas) where T : LocalCanvas
        {
            canvas = GetCanvas<T>();
            return canvas != null;
        }

        public bool IsVisible<T>() where T : LocalCanvas
        {
            var canvas = GetCanvas<T>();
            return canvas != null && canvas.IsVisible;
        }

        public T Show<T>() where T : LocalCanvas
        {
            var canvas = GetCanvas<T>();

            if (canvas == null)
            {
                Dev.LogWarn($"{typeof(T).Name}을(를) 찾을 수 없습니다.");
                return null;
            }

            canvas.Show();
            return canvas;
        }

        public void Hide<T>() where T : LocalCanvas
        {
            var canvas = GetCanvas<T>();
            if (canvas != null) canvas.Hide();
        }

        public void HideAll()
        {
            var canvases = new List<LocalCanvas>(this.canvases.Values);

            foreach (var canvas in canvases)
            {
                if (canvas != null) canvas.Hide();
            }
        }
    }
}