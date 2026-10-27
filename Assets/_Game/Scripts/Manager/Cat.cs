using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Cat : MonoBehaviour
{
    [System.Serializable]
    private struct VisualShadowTile
    {
        public TileBase normalTile;
        public TileBase shadowTile;
    }

    [Header("Input")]
    [SerializeField, Min(1f)] private float dragThreshold = 35f;

    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f;

    [Header("Body sprites - 2 straight + 4 corners")]
    [SerializeField] private Sprite straightHorizontalSprite;
    [SerializeField] private Sprite straightVerticalSprite;
    [SerializeField] private Sprite cornerUpRightSprite;
    [SerializeField] private Sprite cornerUpLeftSprite;
    [SerializeField] private Sprite cornerDownRightSprite;
    [SerializeField] private Sprite cornerDownLeftSprite;
    [SerializeField] private int bodySortingOrder = 9;

    [Header("Shadow on MapVisual - light from top right")]
    [Tooltip("Optional: replace a visual tile with its painted shadow version.")]
    [SerializeField] private VisualShadowTile[] visualShadowTiles;
    [Tooltip("Used when a visual tile has no replacement above.")]
    [SerializeField] private Color fallbackShadowColor = new Color(0.62f, 0.48f, 0.48f, 1f);

    private readonly Dictionary<Vector3Int, SpriteRenderer> bodyRenderers =
        new Dictionary<Vector3Int, SpriteRenderer>();
    private readonly Dictionary<TileBase, TileBase> shadowTileLookup =
        new Dictionary<TileBase, TileBase>();

    private GameManager gameManager;
    private Tilemap mapVisual;
    private Transform bodyRoot;
    private SpriteRenderer headRenderer;
    private Vector3Int currentCell;
    private Vector3Int incomingDirection;
    private Vector2 dragStartPosition;
    private bool pointerDown;
    private bool gestureConsumed;
    private bool isMoving;
    private bool initialized;

    public Vector3Int CurrentCell => currentCell;
    public bool IsMoving => isMoving;

    public void Initialize(
        GameManager manager,
        Tilemap visualTilemap,
        Vector3Int startCell,
        Vector3 startPosition)
    {
        gameManager = manager;
        mapVisual = visualTilemap;
        currentCell = startCell;
        transform.position = startPosition;
        headRenderer = GetComponent<SpriteRenderer>();
        BuildShadowTileLookup();
        CreateBodyRoot();
        ApplyVisualShadow(startPosition);
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || isMoving || gameManager == null || !gameManager.CanAcceptInput)
        {
            return;
        }

        if (TryGetKeyboardDirection(out Vector3Int keyboardDirection))
        {
            TryStartMove(keyboardDirection);
            return;
        }

        ReadSwipeInput();
    }

    private void ReadSwipeInput()
    {
        if (!TryGetPointer(out Vector2 pointerPosition, out bool pressed, out bool held, out bool released))
        {
            return;
        }

        if (pressed)
        {
            pointerDown = true;
            gestureConsumed = false;
            dragStartPosition = pointerPosition;
        }

        if (pointerDown && held && !gestureConsumed)
        {
            Vector2 delta = pointerPosition - dragStartPosition;
            if (delta.sqrMagnitude >= dragThreshold * dragThreshold)
            {
                Vector3Int direction = GetCardinalDirection(delta);

                if (TryStartMove(direction))
                {
                    gestureConsumed = true;
                }
                else
                {
                    // Allows the player to change direction without releasing after
                    // dragging toward a wall or the cat's own body.
                    dragStartPosition = pointerPosition;
                }
            }
        }

        if (released)
        {
            pointerDown = false;
            gestureConsumed = false;
        }
    }

    private bool TryStartMove(Vector3Int direction)
    {
        if (!gameManager.TryBeginMove(currentCell, direction, out List<Vector3Int> path))
        {
            return false;
        }

        StartCoroutine(MoveAlongPath(path, direction));
        return true;
    }

    private IEnumerator MoveAlongPath(List<Vector3Int> path, Vector3Int moveDirection)
    {
        isMoving = true;

        foreach (Vector3Int nextCell in path)
        {
            Vector3Int bodyCell = currentCell;
            CreateBodySegment(bodyCell, incomingDirection, moveDirection);
            gameManager.OccupyBodyCell(bodyCell);

            Vector3 targetPosition = gameManager.GetCellCenterWorld(nextCell);
            while ((transform.position - targetPosition).sqrMagnitude > 0.000001f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    targetPosition,
                    moveSpeed * Time.deltaTime);
                yield return null;
            }

            transform.position = targetPosition;
            currentCell = nextCell;
            incomingDirection = moveDirection;
            ApplyVisualShadow(targetPosition);
        }

        isMoving = false;
        gameManager.FinishMove(currentCell);
    }

    private void BuildShadowTileLookup()
    {
        shadowTileLookup.Clear();

        if (visualShadowTiles == null)
        {
            return;
        }

        foreach (VisualShadowTile pair in visualShadowTiles)
        {
            if (pair.normalTile != null && pair.shadowTile != null)
            {
                shadowTileLookup[pair.normalTile] = pair.shadowTile;
            }
        }
    }

    private void ApplyVisualShadow(Vector3 worldPosition)
    {
        if (mapVisual == null)
        {
            return;
        }

        Vector3Int visualCell = mapVisual.WorldToCell(worldPosition);
        TileBase currentTile = mapVisual.GetTile(visualCell);
        if (currentTile == null)
        {
            return;
        }

        if (shadowTileLookup.TryGetValue(currentTile, out TileBase shadowTile))
        {
            mapVisual.SetTile(visualCell, shadowTile);
            return;
        }

        // Runtime tint is the fallback for levels that do not have a dedicated
        // painted shadow tile yet. Unlock the color because Tile assets lock it
        // by default. The shadow remains because every visited cell becomes body.
        mapVisual.SetTileFlags(visualCell, TileFlags.None);
        mapVisual.SetColor(visualCell, fallbackShadowColor);
    }

    private void CreateBodySegment(
        Vector3Int cell,
        Vector3Int enterDirection,
        Vector3Int exitDirection)
    {
        if (bodyRenderers.ContainsKey(cell))
        {
            return;
        }

        GameObject segment = new GameObject($"Body {cell.x},{cell.y}");
        segment.transform.SetParent(bodyRoot, false);
        segment.transform.position = gameManager.GetCellCenterWorld(cell);

        SpriteRenderer renderer = segment.AddComponent<SpriteRenderer>();
        if (headRenderer != null)
        {
            renderer.sortingLayerID = headRenderer.sortingLayerID;
            renderer.sharedMaterial = headRenderer.sharedMaterial;
        }

        renderer.sortingOrder = bodySortingOrder;
        renderer.sprite = SelectBodySprite(enterDirection, exitDirection);

        bodyRenderers.Add(cell, renderer);
    }

    private Sprite SelectBodySprite(Vector3Int enterDirection, Vector3Int exitDirection)
    {
        // enterDirection points into this cell, so the previous body connection
        // is on the opposite side. The first tail cell is treated as straight.
        Vector3Int previousConnection = enterDirection == Vector3Int.zero
            ? -exitDirection
            : -enterDirection;

        bool up = previousConnection == Vector3Int.up || exitDirection == Vector3Int.up;
        bool down = previousConnection == Vector3Int.down || exitDirection == Vector3Int.down;
        bool left = previousConnection == Vector3Int.left || exitDirection == Vector3Int.left;
        bool right = previousConnection == Vector3Int.right || exitDirection == Vector3Int.right;

        if (left && right)
        {
            return straightHorizontalSprite;
        }

        if (up && down)
        {
            return straightVerticalSprite;
        }

        if (up && right)
        {
            return cornerUpRightSprite;
        }

        if (up && left)
        {
            return cornerUpLeftSprite;
        }

        if (down && right)
        {
            return cornerDownRightSprite;
        }

        return cornerDownLeftSprite;
    }

    private void CreateBodyRoot()
    {
        GameObject rootObject = new GameObject("Cat Body");
        bodyRoot = rootObject.transform;
        bodyRoot.SetParent(transform.parent, false);
    }

    private static Vector3Int GetCardinalDirection(Vector2 delta)
    {
        if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
        {
            return delta.x > 0f ? Vector3Int.right : Vector3Int.left;
        }

        return delta.y > 0f ? Vector3Int.up : Vector3Int.down;
    }

    private static bool TryGetKeyboardDirection(out Vector3Int direction)
    {
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
        {
            direction = Vector3Int.up;
            return true;
        }

        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
        {
            direction = Vector3Int.down;
            return true;
        }

        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
        {
            direction = Vector3Int.left;
            return true;
        }

        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
        {
            direction = Vector3Int.right;
            return true;
        }

        direction = Vector3Int.zero;
        return false;
    }

    private static bool TryGetPointer(
        out Vector2 position,
        out bool pressed,
        out bool held,
        out bool released)
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            position = touch.position;
            pressed = touch.phase == TouchPhase.Began;
            held = touch.phase == TouchPhase.Began ||
                   touch.phase == TouchPhase.Moved ||
                   touch.phase == TouchPhase.Stationary;
            released = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
            return true;
        }

        position = Input.mousePosition;
        pressed = Input.GetMouseButtonDown(0);
        held = Input.GetMouseButton(0);
        released = Input.GetMouseButtonUp(0);
        return pressed || held || released;
    }
}
