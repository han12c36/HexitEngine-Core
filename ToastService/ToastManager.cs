//using System;
//using System.Threading;
//using Cysharp.Threading.Tasks;
////using DG.Tweening;
//using H_Netcode;
//using TMPro;
//using UnityEngine;
//
//public class ToastManager : LocalSingleton<ToastManager> {
//    [SerializeField] private TMP_Text toastMessageText;
//    
//    private CancellationTokenSource _toastCts;
//
//    private void Start()
//    {
//        Color color = toastMessageText.color;
//        color.a = 0f;
//        toastMessageText.color = color;
//    }
//
//    public void ShowToastMessage(string message) {
//        if (_toastCts != null) {
//            _toastCts.Cancel();
//            _toastCts.Dispose();
//        }
//        
//        _toastCts = new CancellationTokenSource();
//
//        //ShowToastAsync(message, _toastCts.Token).Forget();
//    }
//
//    //private async UniTaskVoid ShowToastAsync(string message, CancellationToken token) 
//    //{
//    //    try 
//    //    {
//    //        toastMessageText.DOKill();
//    //        
//    //        toastMessageText.text = message;
//    //
//    //        toastMessageText.DOFade(1f, 0.1f).From(0);
//    //        await UniTask.Delay(TimeSpan.FromSeconds(0.1f), cancellationToken: token);
//    //
//    //        await UniTask.Delay(TimeSpan.FromSeconds(2f), cancellationToken: token);
//    //
//    //        toastMessageText.DOFade(0f, 1f);
//    //        await UniTask.Delay(TimeSpan.FromSeconds(.5f), cancellationToken: token);
//    //    }
//    //    catch (OperationCanceledException) {
//    //        
//    //    }
//    //}
//
//    private void OnDestroy() 
//    {
//        
//        if (_toastCts != null) {
//            _toastCts.Cancel();
//            _toastCts.Dispose();
//        }
//    }
//}