using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DEMO_TEST.Scripts
{
    public class CanvasLoading : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image progressFillerImage;

        private void Start()
        {
            Load().Forget();
        }

        private async UniTask Load()
        {
            await CreateFillAnimation();
            await FillFull();
            DoFade().Forget();
            SceneManager.LoadSceneAsync(1, LoadSceneMode.Additive);
        }

        private async UniTask CreateFillAnimation()
        {
            await LMotion.Create(0f, 0.9f, 2.5f)
                .WithEase(Ease.Linear)
                .Bind(UpdateProgress)
                .ToUniTask();
        }

        private async UniTask FillFull()
        {
            await LMotion.Create(0.9f, 1f, 0.5f)
                .WithEase(Ease.Linear)
                .Bind(UpdateProgress)
                .ToUniTask();
           
        }

        private async UniTask DoFade()
        {
            await LMotion.Create(1f, 0f, 1f)
                .WithEase(Ease.Linear)
                .Bind(value => canvasGroup.alpha = value)
                .ToUniTask();
            await SceneManager.UnloadSceneAsync(0);
        }
        
        private void UpdateProgress(float progress)
        {
            progressFillerImage.fillAmount = progress;
        }
    }
}