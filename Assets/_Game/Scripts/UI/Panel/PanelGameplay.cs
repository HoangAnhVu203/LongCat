using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public sealed class PanelGameplay : UICanvas
{
    [Header("References")]
    [SerializeField] private Button replayButton;
    [SerializeField] private Image tvImage;

    [Header("TV transition")]
    [SerializeField, Min(0.05f)] private float fadeToBlackDuration = 0.3f;
    [SerializeField, Min(0f)] private float blackHoldDuration = 0.08f;
    [SerializeField, Min(0.05f)] private float fadeFromBlackDuration = 0.35f;

    private bool isTransitioning;

    public override void SetUp()
    {
        base.SetUp();
        ResolveReferences();

        if (replayButton != null)
        {
            replayButton.interactable = !isTransitioning;
            replayButton.onClick.RemoveListener(ReplayCurrentLevel);
            replayButton.onClick.AddListener(ReplayCurrentLevel);
        }

        if (!isTransitioning)
        {
            SetTVVisible(false, 0f);
        }
    }

    public void ReplayCurrentLevel()
    {
        if (!isTransitioning)
        {
            StartCoroutine(ReloadWithTVTransition());
        }
    }

    public void ReplayAfterLose()
    {
        ReplayCurrentLevel();
    }

    private IEnumerator ReloadWithTVTransition()
    {
        isTransitioning = true;
        LevelManager.Instance.LockGameplayInput();
        if (replayButton != null)
        {
            replayButton.interactable = false;
        }

        if (tvImage == null)
        {
            LevelManager.Instance.ReloadCurrentLevel();
            FinishTransition();
            yield break;
        }

        tvImage.transform.SetAsLastSibling();
        SetTVVisible(true, 0f);
        yield return FadeTV(0f, 1f, fadeToBlackDuration);

        LevelManager.Instance.ReloadCurrentLevel();

        if (blackHoldDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(blackHoldDuration);
        }

        yield return FadeTV(1f, 0f, fadeFromBlackDuration);
        SetTVVisible(false, 0f);
        FinishTransition();
    }

    private IEnumerator FadeTV(float from, float to, float duration)
    {
        float elapsed = 0f;
        SetTVAlpha(from);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float easedProgress = progress * progress * (3f - 2f * progress);
            SetTVAlpha(Mathf.Lerp(from, to, easedProgress));
            yield return null;
        }

        SetTVAlpha(to);
    }

    private void ResolveReferences()
    {
        if (replayButton == null)
        {
            foreach (Button button in GetComponentsInChildren<Button>(true))
            {
                if (button.name == "ReplayBtn")
                {
                    replayButton = button;
                    break;
                }
            }
        }

        if (tvImage == null)
        {
            foreach (Image image in GetComponentsInChildren<Image>(true))
            {
                if (image.name == "TV")
                {
                    tvImage = image;
                    break;
                }
            }
        }

        if (replayButton == null)
        {
            Debug.LogError("PanelGameplay khong tim thay ReplayBtn.", this);
        }

        if (tvImage == null)
        {
            Debug.LogError("PanelGameplay khong tim thay Image TV.", this);
            return;
        }

        RectTransform tvRect = tvImage.rectTransform;
        tvRect.anchorMin = Vector2.zero;
        tvRect.anchorMax = Vector2.one;
        tvRect.offsetMin = Vector2.zero;
        tvRect.offsetMax = Vector2.zero;
        tvRect.localScale = Vector3.one;
        tvImage.raycastTarget = true;
    }

    private void SetTVVisible(bool visible, float alpha)
    {
        if (tvImage == null)
        {
            return;
        }

        tvImage.gameObject.SetActive(visible);
        SetTVAlpha(alpha);
    }

    private void SetTVAlpha(float alpha)
    {
        if (tvImage == null)
        {
            return;
        }

        tvImage.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
    }

    private void FinishTransition()
    {
        isTransitioning = false;
        if (replayButton != null)
        {
            replayButton.interactable = true;
        }
    }
}
