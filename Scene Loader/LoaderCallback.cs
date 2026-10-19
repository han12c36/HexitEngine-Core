using UnityEngine;

public class LoaderCallback : MonoBehaviour
{
    [SerializeField] private float minimumLoadingTime = 1f;

    //private bool isFirstUpdate = true;

    private IntervalTimer loadingTimer;

    private void Awake()
    {
        loadingTimer = new IntervalTimer(minimumLoadingTime);
    }

    //private void Update()
    //{
    //    if (isFirstUpdate && loadingTimer.Tick(Time.deltaTime))
    //    {
    //        isFirstUpdate = false;
    //
    //        Loader.LoaderCallback();
    //    }
    //}
}