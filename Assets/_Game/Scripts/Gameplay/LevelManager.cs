using System.Collections.Generic;
using UnityEngine;

public sealed class LevelManager : Singleton<LevelManager>
{
    [Header("Level flow")]
    [SerializeField] private List<Level> levelPrefabs = new List<Level>();
    [SerializeField, Min(0)] private int startLevelIndex;
    [SerializeField] private bool loopAfterLastLevel = true;

    [Header("Scene references")]
    [SerializeField] private Transform levelRoot;
    [SerializeField] private GameManager gameManager;

    private Level currentLevel;
    private int currentLevelIndex = -1;
    private bool isChangingLevel;

    public int CurrentLevelIndex => currentLevelIndex;
    public int LevelCount => levelPrefabs.Count;

    private void Awake()
    {
        ResolveSceneReferences();

        if (gameManager != null)
        {
            gameManager.LevelWon += HandleLevelWon;
            gameManager.LevelLost += HandleLevelLost;
        }
    }

    private void Start()
    {
        if (levelPrefabs.Count == 0)
        {
            Debug.LogError("LevelManager chua co prefab level nao trong Level Prefabs.", this);
            return;
        }

        LoadLevel(Mathf.Clamp(startLevelIndex, 0, levelPrefabs.Count - 1));
        UIManager.Instance.OpenUI<PanelGameplay>();
    }

    private void OnDestroy()
    {
        if (gameManager != null)
        {
            gameManager.LevelWon -= HandleLevelWon;
            gameManager.LevelLost -= HandleLevelLost;
        }
    }

    public void LoadNextLevel()
    {
        if (isChangingLevel || levelPrefabs.Count == 0)
        {
            return;
        }

        int nextIndex = currentLevelIndex + 1;
        if (nextIndex >= levelPrefabs.Count)
        {
            if (!loopAfterLastLevel)
            {
                return;
            }

            nextIndex = 0;
        }

        UIManager.Instance.CloseUIDirectly<PanelWin>();
        LoadLevel(nextIndex);
    }

    public void ReloadCurrentLevel()
    {
        if (isChangingLevel || levelPrefabs.Count == 0)
        {
            return;
        }

        UIManager.Instance.CloseUIDirectly<PanelWin>();
        LoadLevel(currentLevelIndex < 0 ? 0 : currentLevelIndex);
    }

    public void LockGameplayInput()
    {
        if (gameManager != null)
        {
            gameManager.LockInputForTransition();
        }
    }

    public void LoadLevel(int index)
    {
        if (isChangingLevel || index < 0 || index >= levelPrefabs.Count)
        {
            return;
        }

        Level prefab = levelPrefabs[index];
        if (prefab == null)
        {
            Debug.LogError($"Level prefab o vi tri {index} dang bi trong.", this);
            return;
        }

        isChangingLevel = true;
        ClearLevelRoot();

        currentLevel = Instantiate(prefab, levelRoot);
        currentLevel.name = prefab.name;
        currentLevel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        currentLevelIndex = index;

        bool started = gameManager != null && gameManager.StartLevel(currentLevel);
        isChangingLevel = false;

        if (!started)
        {
            Debug.LogError($"Khong the khoi tao level '{prefab.name}'.", currentLevel);
        }
    }

    private void HandleLevelWon()
    {
        UIManager.Instance.OpenUI<PanelWin>();
    }

    private void HandleLevelLost()
    {
        PanelGameplay gameplayPanel = UIManager.Instance.OpenUI<PanelGameplay>();
        gameplayPanel.ReplayAfterLose();
    }

    private void ClearLevelRoot()
    {
        if (levelRoot == null)
        {
            return;
        }

        // Disable immediately so the old level cannot render or receive input during
        // the one frame Unity waits before completing Destroy().
        for (int i = levelRoot.childCount - 1; i >= 0; i--)
        {
            GameObject oldLevel = levelRoot.GetChild(i).gameObject;
            oldLevel.SetActive(false);
            Destroy(oldLevel);
        }

        currentLevel = null;
    }

    private void ResolveSceneReferences()
    {
        if (levelRoot == null)
        {
            GameObject rootObject = GameObject.Find("LevelRoot");
            if (rootObject != null)
            {
                levelRoot = rootObject.transform;
            }
        }

        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }

        if (levelRoot == null)
        {
            GameObject rootObject = new GameObject("LevelRoot");
            levelRoot = rootObject.transform;
        }
    }
}
