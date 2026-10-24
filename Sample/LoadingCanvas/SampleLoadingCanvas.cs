using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    /// <summary>
    /// [샘플] LoadingCanvasBase의 기능을 모두 사용한 로딩 화면. 게임에서는 이 클래스를 참고해 자기 로딩 화면을 만듭니다.
    ///
    /// 기능 (모든 참조는 선택: 비워 두면 그 기능만 꺼짐)
    /// - 배경: 보일 때마다 배경 이미지 중 하나를 무작위로 사용
    /// - 진행바 + 퍼센트: 엔진이 주는 진행률을 부드럽게 따라감
    /// - 상태 문구: "로딩 중..." (점 애니메이션). 로드가 끝났는데 계속 떠 있으면 "다른 플레이어를 기다리는 중..."
    ///   (Game 씬은 모든 플레이어가 준비될 때까지 로딩이 유지되므로)
    /// - 팁: 인스펙터의 문구 중 무작위로 보여주고, 일정 시간마다 교체. 팁이 있으면 읽을 시간만큼 최소 표시 시간을 늘림 (MinDisplaySeconds override)
    /// - 페이드: 나타남/사라짐 (PlayShowAsync / PlayHideAsync override)
    ///
    /// 정렬 순서, 렌더 모드, 입력 차단, 선택 해제는 LoadingCanvasBase가 강제하므로 여기서는 화면과 연출만 다룹니다.
    ///
    /// 문구: 샘플이므로 번역 테이블을 쓰지 않고 영어로 고정합니다. (상태 문구는 상수, 팁은 인스펙터 값)
    /// 게임에서 번역이 필요하면 자기 로딩 화면에서 Localization을 사용합니다.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class SampleLoadingCanvas : LoadingCanvasBase
    {
        private const string LOADING_TEXT = "Loading";
        private const string WAITING_TEXT = "Waiting for other players";

        [Header("Background")]
        [SerializeField] private Image backgroundImage;
        [Tooltip("보일 때마다 이 중 하나를 무작위로 사용합니다. 비우면 배경 이미지를 그대로 둡니다.")]
        [SerializeField] private List<Sprite> backgroundSprites = new();

        [Header("Progress")]
        [Tooltip("Image Type을 Filled로 지정한 진행바")]
        [SerializeField] private Image progressFill;
        [SerializeField] private TMP_Text progressText;
        [Tooltip("진행바가 따라가는 속도 (초당). 0이면 즉시")]
        [SerializeField, Min(0f)] private float progressSmoothSpeed = 2f;

        [Header("Status")]
        [SerializeField] private TMP_Text statusText;
        [Tooltip("로드가 끝나고(진행 100%) 이 시간이 지나도 떠 있으면 '기다리는 중' 문구로 바꿉니다.")]
        [SerializeField, Min(0f)] private float waitingDelaySeconds = 1f;
        [Tooltip("점(...) 애니메이션 간격(초). 0이면 점을 표시하지 않음")]
        [SerializeField, Min(0f)] private float dotIntervalSeconds = 0.4f;

        [Header("Tips")]
        [SerializeField] private TMP_Text tipText;
        [Tooltip("보여줄 팁 문구. 비우면 팁 칸을 숨깁니다.")]
        [SerializeField, TextArea]
        private List<string> tips = new()
        {
            "Press Ready when you are prepared. The host starts the game once everyone is ready.",
            "Invite friends from the Steam overlay or share the room code.",
            "Voice chat starts automatically when you join a room.",
        };
        [Tooltip("팁 교체 간격(초). 0이면 교체하지 않음")]
        [SerializeField, Min(0f)] private float tipIntervalSeconds = 5f;
        [Tooltip("팁을 보여줄 때의 최소 표시 시간(초). 기본 최소 표시 시간보다 길면 이 값을 사용합니다.")]
        [SerializeField, Min(0f)] private float tipMinReadSeconds = 1.5f;

        [Header("Fade")]
        [Tooltip("나타날 때 페이드 시간(초). 씬 로드와 동시에 진행되므로 0(즉시 덮기)을 권장합니다.")]
        [SerializeField, Min(0f)] private float fadeInSeconds = 0f;
        [Tooltip("사라질 때 페이드 시간(초)")]
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.3f;

        private CanvasGroup canvasGroup;

        private float targetProgress;
        private float displayProgress;

        /// <summary> 진행 100%에 도달한 시각. 도달 전이면 -1 </summary>
        private float fullProgressTime = -1f;

        private int dotCount;
        private float nextDotTime;
        private bool isWaitingShown;

        private bool hasTip;
        private int tipIndex = -1;
        private float nextTipTime;

        #region LoadingCanvasBase

        /// <summary> 팁이 보이면 읽을 시간만큼 최소 표시 시간을 늘립니다. </summary>
        public override float MinDisplaySeconds => hasTip ? Mathf.Max(base.MinDisplaySeconds, tipMinReadSeconds) : base.MinDisplaySeconds;

        public override async UniTask PlayShowAsync(CancellationToken ct)
        {
            Show();
            await FadeAsync(1f, fadeInSeconds, ct);
        }

        public override async UniTask PlayHideAsync(CancellationToken ct)
        {
            // 사라지기 전에 진행바를 끝까지 채움
            SetDisplayProgress(1f);

            await FadeAsync(0f, fadeOutSeconds, ct);
            Hide();
        }

        public override void SetProgress(float progress)
        {
            targetProgress = Mathf.Clamp01(progress);

            // 새 로딩으로 진행률이 줄었거나 부드럽게 따라가지 않으면 바로 맞춤
            if (progressSmoothSpeed <= 0f || targetProgress < displayProgress) SetDisplayProgress(targetProgress);
        }

        #endregion

        #region Loading Hooks

        protected override void OnLoadingInitialize() => canvasGroup = GetComponent<CanvasGroup>();

        protected override void OnLoadingShow()
        {
            // 보일 때마다 처음 상태로
            targetProgress = 0f;
            SetDisplayProgress(0f);

            dotCount = 0;
            nextDotTime = Time.unscaledTime + dotIntervalSeconds;
            RefreshStatus();

            PickBackground();

            tipIndex = -1;
            ShowNextTip();
        }

        #endregion

        #region Update

        private void Update()
        {
            float now = Time.unscaledTime;

            // 진행바: 목표를 부드럽게 따라감
            if (displayProgress < targetProgress)
            {
                SetDisplayProgress(Mathf.MoveTowards(displayProgress, targetProgress, progressSmoothSpeed * Time.unscaledDeltaTime));
            }

            // 상태 문구: 점 애니메이션
            if (dotIntervalSeconds > 0f && now >= nextDotTime)
            {
                dotCount = (dotCount + 1) % 4;
                nextDotTime = now + dotIntervalSeconds;
                RefreshStatus();
            }
            // 상태 문구: 기다리는 중 전환 (점 애니메이션을 끈 경우에도 바뀌도록)
            else if (IsWaiting() != isWaitingShown)
            {
                RefreshStatus();
            }

            // 팁 교체
            if (hasTip && tipIntervalSeconds > 0f && tips.Count > 1 && now >= nextTipTime)
            {
                ShowNextTip();
            }
        }

        #endregion

        #region Progress / Status

        private void SetDisplayProgress(float progress)
        {
            displayProgress = progress;

            if (progressFill != null) progressFill.fillAmount = progress;
            if (progressText != null) progressText.text = $"{Mathf.RoundToInt(progress * 100f)}%";

            if (progress >= 1f)
            {
                if (fullProgressTime < 0f) fullProgressTime = Time.unscaledTime;
            }
            else fullProgressTime = -1f;
        }

        private void RefreshStatus()
        {
            isWaitingShown = IsWaiting();

            if (statusText == null) return;

            string text = isWaitingShown ? WAITING_TEXT : LOADING_TEXT;

            statusText.text = dotIntervalSeconds > 0f ? text + new string('.', dotCount) : text;
        }

        /// <summary> 로드가 끝났는데(진행 100%) 일정 시간 이상 떠 있는지 = 다른 플레이어를 기다리는 중 </summary>
        private bool IsWaiting() => fullProgressTime >= 0f && Time.unscaledTime - fullProgressTime >= waitingDelaySeconds;

        #endregion

        #region Background / Tips

        private void PickBackground()
        {
            if (backgroundImage == null || backgroundSprites.Count == 0) return;

            var sprite = backgroundSprites[Random.Range(0, backgroundSprites.Count)];
            if (sprite == null) return;

            backgroundImage.sprite = sprite;
            backgroundImage.color = Color.white;
        }

        /// <summary> 직전과 다른 팁을 무작위로 보여줍니다. 팁이 없으면 팁 칸을 숨깁니다. </summary>
        private void ShowNextTip()
        {
            nextTipTime = Time.unscaledTime + tipIntervalSeconds;

            string text = null;

            if (tipText != null && tips.Count > 0)
            {
                int index = Random.Range(0, tips.Count);
                if (tips.Count > 1 && index == tipIndex) index = (index + 1) % tips.Count;

                tipIndex = index;
                text = tips[index];
            }

            hasTip = !string.IsNullOrEmpty(text);

            if (tipText == null) return;

            tipText.gameObject.SetActive(hasTip);
            if (hasTip) tipText.text = text;
        }

        #endregion

        #region Fade

        /// <summary> 현재 alpha에서 target까지 (중간 상태에서 시작해도 자연스럽게 이어짐). 시간 정지(timeScale 0)와 무관 </summary>
        private async UniTask FadeAsync(float target, float duration, CancellationToken ct)
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

            if (duration <= 0f)
            {
                canvasGroup.alpha = target;
                return;
            }

            float start = canvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, target, elapsed / duration);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            canvasGroup.alpha = target;
        }

        #endregion
    }
}