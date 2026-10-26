using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class GameManager : MonoBehaviour
{
    public enum GameState
    {
        Playing,
        Moving,
        Won,
        Stuck
    }

    [Header("Board")]
    [SerializeField] private Tilemap mapBG;
    [SerializeField] private Cat cat;
    [SerializeField, Min(1)] private int maxSlideCells = 256;

    private readonly HashSet<Vector3Int> playableCells = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();
    private bool inputLocked;

    public GameState State { get; private set; } = GameState.Playing;
    public bool CanAcceptInput => State == GameState.Playing && !inputLocked;
    public int PlayableCellCount => playableCells.Count;
    public int OccupiedCellCount => occupiedCells.Count;
    public event Action LevelWon;
    public event Action LevelLost;

    private static readonly Vector3Int[] CardinalDirections =
    {
        Vector3Int.up,
        Vector3Int.right,
        Vector3Int.down,
        Vector3Int.left
    };

    public bool StartLevel(Level level)
    {
        playableCells.Clear();
        occupiedCells.Clear();
        inputLocked = false;
        State = GameState.Playing;
        enabled = true;

        mapBG = level != null ? level.MapBG : null;
        cat = level != null ? level.Cat : null;

        if (mapBG == null || cat == null)
        {
            enabled = false;
            Debug.LogError("Level phai chua Tilemap ten 'MapBG' va mot Cat.", level);
            return false;
        }

        Vector3Int startCell = mapBG.WorldToCell(cat.transform.position);
        BuildPlayableArea(startCell);

        if (playableCells.Count == 0)
        {
            Debug.LogError("Khong tim thay vung trong nao trong MapBG tai vi tri Cat.", this);
            enabled = false;
            return false;
        }

        cat.Initialize(this, level.MapVisual, startCell, GetCellCenterWorld(startCell));
        return true;
    }

    public void LockInputForTransition()
    {
        inputLocked = true;
    }

    /// <summary>
    /// Finds the enclosed empty region that contains the cat. A MapBG tile is a wall;
    /// WalkableMap is deliberately ignored because it is visual-only.
    /// </summary>
    private void BuildPlayableArea(Vector3Int startCell)
    {
        playableCells.Clear();

        BoundsInt bounds = mapBG.cellBounds;
        if (!bounds.Contains(startCell) || mapBG.HasTile(startCell))
        {
            return;
        }

        Queue<Vector3Int> frontier = new Queue<Vector3Int>();
        frontier.Enqueue(startCell);
        playableCells.Add(startCell);

        while (frontier.Count > 0)
        {
            Vector3Int cell = frontier.Dequeue();

            foreach (Vector3Int direction in CardinalDirections)
            {
                Vector3Int neighbour = cell + direction;
                if (!bounds.Contains(neighbour) ||
                    mapBG.HasTile(neighbour) ||
                    !playableCells.Add(neighbour))
                {
                    continue;
                }

                frontier.Enqueue(neighbour);
            }
        }
    }

    public bool TryBeginMove(
        Vector3Int headCell,
        Vector3Int direction,
        out List<Vector3Int> path)
    {
        path = new List<Vector3Int>();

        if (!CanAcceptInput || !IsCardinalDirection(direction))
        {
            return false;
        }

        Vector3Int nextCell = headCell + direction;
        int checkedCells = 0;

        while (checkedCells < maxSlideCells && CanEnter(nextCell))
        {
            path.Add(nextCell);
            nextCell += direction;
            checkedCells++;
        }

        if (path.Count == 0)
        {
            return false;
        }

        State = GameState.Moving;
        return true;
    }

    public void OccupyBodyCell(Vector3Int cell)
    {
        if (playableCells.Contains(cell))
        {
            occupiedCells.Add(cell);
        }
    }

    public void FinishMove(Vector3Int headCell)
    {
        if (occupiedCells.Count + 1 >= playableCells.Count)
        {
            State = GameState.Won;
            Debug.Log("Level Complete! Long Cat da phu kin toan bo vung di duoc.", this);
            LevelWon?.Invoke();
            return;
        }

        State = HasAnyMove(headCell) ? GameState.Playing : GameState.Stuck;

        if (State == GameState.Stuck)
        {
            Debug.Log("Cat bi ket: khong con huong di hop le.", this);
            LevelLost?.Invoke();
        }
    }

    public Vector3 GetCellCenterWorld(Vector3Int cell)
    {
        return mapBG.GetCellCenterWorld(cell);
    }

    public bool IsOccupied(Vector3Int cell)
    {
        return occupiedCells.Contains(cell);
    }

    private bool HasAnyMove(Vector3Int headCell)
    {
        foreach (Vector3Int direction in CardinalDirections)
        {
            if (CanEnter(headCell + direction))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanEnter(Vector3Int cell)
    {
        return playableCells.Contains(cell) && !occupiedCells.Contains(cell);
    }

    private static bool IsCardinalDirection(Vector3Int direction)
    {
        return Mathf.Abs(direction.x) + Mathf.Abs(direction.y) == 1 && direction.z == 0;
    }
}
