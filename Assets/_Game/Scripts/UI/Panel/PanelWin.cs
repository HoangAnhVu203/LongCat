using UnityEngine;
using UnityEngine.UI;

public sealed class PanelWin : UICanvas
{
    [SerializeField] private Button nextLevelButton;

    public override void SetUp()
    {
        base.SetUp();

        if (nextLevelButton == null)
        {
            nextLevelButton = GetComponentInChildren<Button>(true);
        }

        if (nextLevelButton == null)
        {
            Debug.LogError("PanelWin khong tim thay Button Next Level.", this);
            return;
        }

        nextLevelButton.interactable = true;
        nextLevelButton.onClick.RemoveListener(GoToNextLevel);
        nextLevelButton.onClick.AddListener(GoToNextLevel);
    }

    private void GoToNextLevel()
    {
        nextLevelButton.interactable = false;
        LevelManager.Instance.LoadNextLevel();
    }
}
