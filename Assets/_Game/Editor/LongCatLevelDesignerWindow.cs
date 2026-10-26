using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class LongCatLevelDesignerWindow : EditorWindow
{
    private const string DefaultTemplatePath = "Assets/_Game/Prefabs/_Level/Level1.prefab";
    private const string OutputFolder = "Assets/_Game/Prefabs/Level";
    private const string DefaultTileFolder = "Assets/_Game/TileMap";
    private const int MaxBoardSize = 20;

    private enum EditLayer
    {
        MapBG,
        WalkableMap,
        MapVisual,
        CatSpawn
    }

    private enum BrushMode
    {
        Pencil,
        FloodFill,
        Eraser
    }

    [SerializeField] private Level templatePrefab;
    [SerializeField] private Level levelToEdit;
    [SerializeField] private string levelName = "Level3";
    [SerializeField] private int width = 5;
    [SerializeField] private int height = 5;
    [SerializeField] private Vector2Int gridOrigin = new Vector2Int(-2, -2);
    [SerializeField] private Vector2Int catCell = new Vector2Int(2, 2);
    [SerializeField] private TileBase[] mapBGTiles;
    [SerializeField] private TileBase[] walkableTiles;
    [SerializeField] private TileBase[] visualTiles;
    [SerializeField] private EditLayer activeLayer;
    [SerializeField] private BrushMode brushMode;
    [SerializeField] private TileBase selectedTile;
    [SerializeField] private TileBase outsideBGTile;
    [SerializeField] private bool compositePreview = true;
    [SerializeField] private bool addToLevelManager = true;
    [SerializeField] private string tileSearch = string.Empty;

    private readonly List<TileBase> paletteTiles = new List<TileBase>();
    private Vector2 mainScroll;
    private Vector2 paletteScroll;
    private string loadedAssetPath;
    private string statusMessage;
    private MessageType statusType = MessageType.Info;
    private int lastPaintedIndex = -1;

    [MenuItem("Tools/Long Cat/Level Designer")]
    public static void Open()
    {
        LongCatLevelDesignerWindow window = GetWindow<LongCatLevelDesignerWindow>();
        window.titleContent = new GUIContent("Long Cat Level");
        window.minSize = new Vector2(760f, 620f);
        window.Show();
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        if (templatePrefab == null)
        {
            templatePrefab = AssetDatabase.LoadAssetAtPath<Level>(DefaultTemplatePath);
        }

        ReloadPalette();
        if (!ArraysAreValid())
        {
            if (templatePrefab != null)
            {
                LoadLayout(templatePrefab, false);
            }
            else
            {
                CreateBlankGrid(width, height);
            }
        }
    }

    private void OnGUI()
    {
        mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Long Cat - Level Designer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Chon layer, chon tile trong Palette, sau do click/keo tren tung o. " +
            "MapBG: co tile = vat can, khong co tile = meo duoc di. WalkableMap va MapVisual chi la hinh trang tri.",
            MessageType.Info);

        DrawSourceSection();
        EditorGUILayout.Space(8f);
        DrawBoardSettings();
        EditorGUILayout.Space(8f);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(390f)))
            {
                DrawTools();
                DrawBoard();
            }

            GUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(300f)))
            {
                DrawPalette();
            }
        }

        EditorGUILayout.Space(8f);
        DrawSaveSection();
        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.HelpBox(statusMessage, statusType);
        }

        EditorGUILayout.EndScrollView();
        if (AssetPreview.IsLoadingAssetPreviews())
        {
            Repaint();
        }
    }

    private void DrawSourceSection()
    {
        EditorGUILayout.LabelField("1. Prefab nguon", EditorStyles.boldLabel);
        templatePrefab = (Level)EditorGUILayout.ObjectField(
            new GUIContent("Template", "Prefab mau co Grid, MapBG, cac Tilemap va Cat."),
            templatePrefab, typeof(Level), false);
        levelToEdit = (Level)EditorGUILayout.ObjectField(
            new GUIContent("Level can sua", "Keo prefab level da luu vao day de sua tiep."),
            levelToEdit, typeof(Level), false);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = templatePrefab != null;
            if (GUILayout.Button("Tao level moi tu Template"))
            {
                levelToEdit = null;
                loadedAssetPath = null;
                LoadLayout(templatePrefab, true);
                levelName = FindNextLevelName();
            }

            GUI.enabled = levelToEdit != null;
            if (GUILayout.Button("Nap level can sua"))
            {
                loadedAssetPath = AssetDatabase.GetAssetPath(levelToEdit);
                levelName = levelToEdit.name;
                LoadLayout(levelToEdit, true);
            }
            GUI.enabled = true;
        }
    }

    private void DrawBoardSettings()
    {
        EditorGUILayout.LabelField("2. Kich thuoc", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            int newWidth = EditorGUILayout.IntSlider("Width", width, 1, MaxBoardSize);
            int newHeight = EditorGUILayout.IntSlider("Height", height, 1, MaxBoardSize);
            if (newWidth != width || newHeight != height)
            {
                ResizeGrid(newWidth, newHeight);
            }
        }

        gridOrigin = EditorGUILayout.Vector2IntField(
            new GUIContent("Cell Origin", "Toa do cell cua o duoi-cung ben-trai."), gridOrigin);
    }

    private void DrawTools()
    {
        EditorGUILayout.LabelField("3. Ve level", EditorStyles.boldLabel);
        activeLayer = (EditLayer)GUILayout.Toolbar((int)activeLayer, new[]
        {
            "MapBG (CAN)", "Walkable", "Visual", "CAT"
        });

        if (activeLayer != EditLayer.CatSpawn)
        {
            brushMode = (BrushMode)GUILayout.Toolbar((int)brushMode, new[]
            {
                "But ve", "Do vung", "Tay"
            });
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            compositePreview = EditorGUILayout.ToggleLeft("Xem tong hop 3 layer", compositePreview, GUILayout.Width(165f));
            GUILayout.FlexibleSpace();
            if (activeLayer != EditLayer.CatSpawn && GUILayout.Button("Xoa layer dang chon", GUILayout.Width(135f)))
            {
                if (EditorUtility.DisplayDialog("Xoa layer?", $"Xoa tat ca tile cua {activeLayer}?", "Xoa", "Huy"))
                {
                    Array.Clear(GetActiveTiles(), 0, width * height);
                }
            }
        }
    }

    private void DrawBoard()
    {
        float availableWidth = Mathf.Max(300f, position.width - 370f);
        float cellSize = Mathf.Clamp(Mathf.Min((availableWidth - 30f) / width, 54f), 22f, 54f);
        Rect boardRect = GUILayoutUtility.GetRect(width * cellSize, height * cellSize,
            GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

        EditorGUI.DrawRect(boardRect, new Color(0.12f, 0.12f, 0.12f));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int drawY = height - 1 - y;
                Rect cellRect = new Rect(
                    boardRect.x + x * cellSize,
                    boardRect.y + drawY * cellSize,
                    cellSize,
                    cellSize);
                DrawCell(cellRect, x, y);
            }
        }

        HandleBoardInput(boardRect, cellSize);
        EditorGUILayout.LabelField(
            $"Cat: cell ({gridOrigin.x + catCell.x}, {gridOrigin.y + catCell.y})  |  Chuot phai luon la tay",
            EditorStyles.miniLabel);

        int playableCount = CountPlayableCells();
        bool connected = ArePlayableCellsConnected();
        string state = playableCount == 0
            ? "Chua co o nao de di (MapBG dang kin)."
            : connected
                ? $"{playableCount} o co the di, tat ca lien thong."
                : $"{playableCount} o co the di nhung dang bi tach thanh nhieu vung.";
        EditorGUILayout.HelpBox(state, playableCount > 0 && connected ? MessageType.Info : MessageType.Warning);
    }

    private void DrawCell(Rect rect, int x, int y)
    {
        int index = GetIndex(x, y);
        TileBase bg = mapBGTiles[index];
        TileBase walkable = walkableTiles[index];
        TileBase visual = visualTiles[index];

        Color baseColor = bg == null
            ? new Color(0.20f, 0.50f, 0.25f)
            : new Color(0.28f, 0.28f, 0.28f);
        EditorGUI.DrawRect(rect, baseColor);

        if (compositePreview)
        {
            DrawTilePreview(rect, bg);
            DrawTilePreview(rect, walkable);
            DrawTilePreview(rect, visual);
        }
        else if (activeLayer != EditLayer.CatSpawn)
        {
            DrawTilePreview(rect, GetActiveTiles()[index]);
        }

        if (catCell.x == x && catCell.y == y)
        {
            Rect badge = new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, 18f);
            EditorGUI.DrawRect(badge, new Color(1f, 0.68f, 0.08f, 0.95f));
            GUIStyle catStyle = new GUIStyle(EditorStyles.boldLabel);
            catStyle.alignment = TextAnchor.MiddleCenter;
            catStyle.normal.textColor = Color.black;
            GUI.Label(badge, "CAT", catStyle);
        }

        Handles.DrawSolidRectangleWithOutline(rect, Color.clear,
            activeLayer == EditLayer.MapBG && bg == null
                ? new Color(0.35f, 0.95f, 0.42f, 0.9f)
                : new Color(0f, 0f, 0f, 0.55f));
    }

    private static void DrawTilePreview(Rect rect, TileBase tile)
    {
        if (tile == null)
        {
            return;
        }

        Texture preview = AssetPreview.GetAssetPreview(tile);
        if (preview == null)
        {
            preview = AssetPreview.GetMiniThumbnail(tile);
        }
        if (preview != null)
        {
            GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f),
                preview, ScaleMode.ScaleToFit, true);
        }
    }

    private void HandleBoardInput(Rect boardRect, float cellSize)
    {
        Event current = Event.current;
        bool pointerEvent = current.type == EventType.MouseDown || current.type == EventType.MouseDrag;
        if (!pointerEvent || !boardRect.Contains(current.mousePosition) || (current.button != 0 && current.button != 1))
        {
            if (current.type == EventType.MouseUp)
            {
                lastPaintedIndex = -1;
            }
            return;
        }

        int x = Mathf.FloorToInt((current.mousePosition.x - boardRect.x) / cellSize);
        int drawY = Mathf.FloorToInt((current.mousePosition.y - boardRect.y) / cellSize);
        int y = height - 1 - drawY;
        if (!IsInsideGrid(x, y))
        {
            return;
        }

        int index = GetIndex(x, y);
        if (index != lastPaintedIndex || current.type == EventType.MouseDown)
        {
            PaintCell(x, y, current.button == 1);
            lastPaintedIndex = index;
        }
        current.Use();
    }

    private void PaintCell(int x, int y, bool forceErase)
    {
        if (activeLayer == EditLayer.CatSpawn)
        {
            catCell = new Vector2Int(x, y);
            Repaint();
            return;
        }

        TileBase[] tiles = GetActiveTiles();
        int index = GetIndex(x, y);
        TileBase replacement = forceErase || brushMode == BrushMode.Eraser ? null : selectedTile;
        if (brushMode == BrushMode.FloodFill && !forceErase)
        {
            FloodFill(tiles, x, y, replacement);
        }
        else
        {
            tiles[index] = replacement;
        }
        Repaint();
    }

    private void DrawPalette()
    {
        EditorGUILayout.LabelField("TILE PALETTE", EditorStyles.boldLabel);
        if (activeLayer == EditLayer.CatSpawn)
        {
            EditorGUILayout.HelpBox("Click vao mot o ben trai de dat vi tri Cat.", MessageType.Info);
            return;
        }

        selectedTile = (TileBase)EditorGUILayout.ObjectField("Tile dang chon", selectedTile, typeof(TileBase), false);
        if (activeLayer == EditLayer.MapBG)
        {
            SyncOutsideBGTile();
            GUI.enabled = false;
            EditorGUILayout.ObjectField(
                new GUIContent("Nen ngoai (tu dong)", "Tile xuat hien nhieu nhat trong MapBG cua board."),
                outsideBGTile, typeof(TileBase), false);
            GUI.enabled = true;
            EditorGUILayout.HelpBox(
                "Nen ben ngoai se tu dong dung tile xuat hien nhieu nhat trong MapBG ban da ve.",
                MessageType.None);
        }
        tileSearch = EditorGUILayout.TextField("Tim tile", tileSearch);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Tay / O trong"))
            {
                selectedTile = null;
                brushMode = BrushMode.Eraser;
            }
            if (GUILayout.Button("Nap lai Palette"))
            {
                ReloadPalette();
            }
        }

        EditorGUILayout.LabelField($"{paletteTiles.Count} tile trong {DefaultTileFolder}", EditorStyles.miniLabel);
        paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll, GUILayout.Height(390f));

        const float itemSize = 62f;
        int columns = Mathf.Max(1, Mathf.FloorToInt(270f / itemSize));
        int column = 0;
        EditorGUILayout.BeginHorizontal();
        foreach (TileBase tile in paletteTiles)
        {
            if (!string.IsNullOrEmpty(tileSearch) &&
                tile.name.IndexOf(tileSearch, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            Texture preview = AssetPreview.GetAssetPreview(tile);
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(tile);
            }

            Color oldColor = GUI.backgroundColor;
            GUI.backgroundColor = selectedTile == tile ? new Color(1f, 0.75f, 0.15f) : Color.white;
            GUIContent content = new GUIContent(preview, $"{tile.name}\n{AssetDatabase.GetAssetPath(tile)}");
            if (GUILayout.Button(content, GUILayout.Width(itemSize), GUILayout.Height(itemSize)))
            {
                selectedTile = tile;
                if (brushMode == BrushMode.Eraser)
                {
                    brushMode = BrushMode.Pencil;
                }
            }
            GUI.backgroundColor = oldColor;

            column++;
            if (column >= columns)
            {
                column = 0;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
    }

    private void DrawSaveSection()
    {
        EditorGUILayout.LabelField("4. Luu prefab", EditorStyles.boldLabel);
        levelName = EditorGUILayout.TextField("Level Name", levelName);
        addToLevelManager = EditorGUILayout.ToggleLeft(
            "Tu dong them vao LevelManager cua scene hien tai", addToLevelManager);
        EditorGUILayout.LabelField("Noi luu", OutputFolder, EditorStyles.miniLabel);

        GUI.enabled = templatePrefab != null;
        if (GUILayout.Button("LUU LEVEL PREFAB", GUILayout.Height(38f)))
        {
            SaveLevelPrefab();
        }
        GUI.enabled = true;
    }

    private void SaveLevelPrefab()
    {
        SyncOutsideBGTile();
        if (!ValidateBeforeSave(out string validationError))
        {
            SetStatus(validationError, MessageType.Error);
            return;
        }

        EnsureOutputFolder();
        string safeName = MakeSafeFileName(levelName);
        string targetPath = $"{OutputFolder}/{safeName}.prefab";
        bool targetExists = AssetDatabase.LoadAssetAtPath<Level>(targetPath) != null;
        bool editingSameAsset = targetPath == loadedAssetPath;
        if (targetExists && !editingSameAsset &&
            !EditorUtility.DisplayDialog("Ghi de level?", $"Prefab '{targetPath}' da ton tai.", "Ghi de", "Huy"))
        {
            return;
        }

        string sourcePath = editingSameAsset || targetExists
            ? targetPath
            : AssetDatabase.GetAssetPath(templatePrefab);
        GameObject root = PrefabUtility.LoadPrefabContents(sourcePath);
        try
        {
            Level level = root.GetComponent<Level>();
            Tilemap mapBG = level != null ? level.MapBG : FindTilemap(root, "MapBG");
            Tilemap walkable = FindTilemap(root, "WalkableMap");
            Tilemap visual = FindTilemap(root, "MapVisual");
            Cat cat = level != null ? level.Cat : root.GetComponentInChildren<Cat>(true);
            if (mapBG == null || walkable == null || visual == null || cat == null)
            {
                SetStatus("Prefab nguon can co MapBG, WalkableMap, MapVisual va Cat.", MessageType.Error);
                return;
            }

            BoundsInt oldBounds = GetDesignerBounds(mapBG, walkable, visual, cat);
            BoundsInt newBounds = new BoundsInt(gridOrigin.x, gridOrigin.y, 0, width, height, 1);
            BoundsInt backgroundBounds = EncapsulateBounds(mapBG.cellBounds, newBounds);
            ClearBounds(walkable, oldBounds);
            ClearBounds(visual, oldBounds);
            ClearBounds(walkable, newBounds);
            ClearBounds(visual, newBounds);

            FillOutsideBackground(mapBG, backgroundBounds, newBounds, outsideBGTile);
            WriteLayer(mapBG, mapBGTiles);
            WriteLayer(walkable, walkableTiles);
            WriteLayer(visual, visualTiles);
            cat.transform.position = mapBG.GetCellCenterWorld(ToMapCell(catCell.x, catCell.y));
            root.name = safeName;
            PrefabUtility.SaveAsPrefabAsset(root, targetPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Level savedLevel = AssetDatabase.LoadAssetAtPath<Level>(targetPath);
        levelToEdit = savedLevel;
        loadedAssetPath = targetPath;
        levelName = safeName;
        if (addToLevelManager)
        {
            AddToCurrentLevelManager(savedLevel);
        }

        Selection.activeObject = savedLevel;
        EditorGUIUtility.PingObject(savedLevel);
        SetStatus($"Da luu: {targetPath}", MessageType.Info);
    }

    private void LoadLayout(Level prefab, bool showStatus)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Level level = root.GetComponent<Level>();
            Tilemap mapBG = level != null ? level.MapBG : FindTilemap(root, "MapBG");
            Tilemap walkable = FindTilemap(root, "WalkableMap");
            Tilemap visual = FindTilemap(root, "MapVisual");
            Cat cat = level != null ? level.Cat : root.GetComponentInChildren<Cat>(true);
            if (mapBG == null || walkable == null || visual == null || cat == null)
            {
                SetStatus("Prefab can co MapBG, WalkableMap, MapVisual va Cat.", MessageType.Error);
                return;
            }

            BoundsInt bounds = GetDesignerBounds(mapBG, walkable, visual, cat);
            width = Mathf.Clamp(bounds.size.x, 1, MaxBoardSize);
            height = Mathf.Clamp(bounds.size.y, 1, MaxBoardSize);
            gridOrigin = new Vector2Int(bounds.xMin, bounds.yMin);
            AllocateTileArrays();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = GetIndex(x, y);
                    Vector3Int cell = ToMapCell(x, y);
                    mapBGTiles[index] = mapBG.GetTile(cell);
                    walkableTiles[index] = walkable.GetTile(cell);
                    visualTiles[index] = visual.GetTile(cell);
                }
            }

            Vector3Int spawn = mapBG.WorldToCell(cat.transform.position);
            catCell = new Vector2Int(
                Mathf.Clamp(spawn.x - gridOrigin.x, 0, width - 1),
                Mathf.Clamp(spawn.y - gridOrigin.y, 0, height - 1));
            outsideBGTile = FindMostUsedTileOutsideBounds(mapBG, bounds) ?? FindMostUsedTile(mapBG);
            selectedTile = FirstTile(walkableTiles) ?? FirstTile(visualTiles) ?? FirstTile(mapBGTiles);
            if (showStatus)
            {
                SetStatus($"Da nap dung tile va vi tri Cat tu {path}", MessageType.Info);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static BoundsInt GetDesignerBounds(Tilemap mapBG, Tilemap walkable, Tilemap visual, Cat cat)
    {
        bool found = false;
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        IncludeUsedTileBounds(walkable, ref found, ref minX, ref minY, ref maxX, ref maxY);
        IncludeUsedTileBounds(visual, ref found, ref minX, ref minY, ref maxX, ref maxY);

        if (!found)
        {
            Vector3Int start = mapBG.WorldToCell(cat.transform.position);
            HashSet<Vector3Int> emptyArea = FindConnectedEmptyArea(mapBG, start);
            foreach (Vector3Int cell in emptyArea)
            {
                found = true;
                minX = Mathf.Min(minX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxX = Mathf.Max(maxX, cell.x);
                maxY = Mathf.Max(maxY, cell.y);
            }
        }

        if (!found)
        {
            Vector3Int start = mapBG.WorldToCell(cat.transform.position);
            return new BoundsInt(start.x, start.y, 0, 1, 1, 1);
        }
        return new BoundsInt(minX, minY, 0, maxX - minX + 1, maxY - minY + 1, 1);
    }

    private static void IncludeUsedTileBounds(Tilemap tilemap, ref bool found,
        ref int minX, ref int minY, ref int maxX, ref int maxY)
    {
        foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
        {
            if (!tilemap.HasTile(cell))
            {
                continue;
            }
            found = true;
            minX = Mathf.Min(minX, cell.x);
            minY = Mathf.Min(minY, cell.y);
            maxX = Mathf.Max(maxX, cell.x);
            maxY = Mathf.Max(maxY, cell.y);
        }
    }

    private static HashSet<Vector3Int> FindConnectedEmptyArea(Tilemap map, Vector3Int start)
    {
        HashSet<Vector3Int> result = new HashSet<Vector3Int>();
        BoundsInt bounds = map.cellBounds;
        if (!bounds.Contains(start) || map.HasTile(start))
        {
            return result;
        }

        Queue<Vector3Int> queue = new Queue<Vector3Int>();
        queue.Enqueue(start);
        result.Add(start);
        Vector3Int[] directions = { Vector3Int.up, Vector3Int.right, Vector3Int.down, Vector3Int.left };
        while (queue.Count > 0 && result.Count <= MaxBoardSize * MaxBoardSize)
        {
            Vector3Int cell = queue.Dequeue();
            foreach (Vector3Int direction in directions)
            {
                Vector3Int next = cell + direction;
                if (bounds.Contains(next) && !map.HasTile(next) && result.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }
        return result;
    }

    private void ReloadPalette()
    {
        paletteTiles.Clear();
        string[] guids = AssetDatabase.FindAssets("t:TileBase", new[] { DefaultTileFolder });
        foreach (string guid in guids)
        {
            TileBase tile = AssetDatabase.LoadAssetAtPath<TileBase>(AssetDatabase.GUIDToAssetPath(guid));
            if (tile != null)
            {
                paletteTiles.Add(tile);
            }
        }
        paletteTiles.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
    }

    private void ResizeGrid(int newWidth, int newHeight)
    {
        TileBase[] oldBG = mapBGTiles;
        TileBase[] oldWalkable = walkableTiles;
        TileBase[] oldVisual = visualTiles;
        int oldWidth = width;
        int oldHeight = height;
        width = newWidth;
        height = newHeight;
        AllocateTileArrays();

        int copyWidth = Mathf.Min(oldWidth, width);
        int copyHeight = Mathf.Min(oldHeight, height);
        for (int y = 0; y < copyHeight; y++)
        {
            for (int x = 0; x < copyWidth; x++)
            {
                int oldIndex = y * oldWidth + x;
                int newIndex = GetIndex(x, y);
                mapBGTiles[newIndex] = oldBG[oldIndex];
                walkableTiles[newIndex] = oldWalkable[oldIndex];
                visualTiles[newIndex] = oldVisual[oldIndex];
            }
        }
        catCell.x = Mathf.Clamp(catCell.x, 0, width - 1);
        catCell.y = Mathf.Clamp(catCell.y, 0, height - 1);
    }

    private void CreateBlankGrid(int newWidth, int newHeight)
    {
        width = Mathf.Clamp(newWidth, 1, MaxBoardSize);
        height = Mathf.Clamp(newHeight, 1, MaxBoardSize);
        AllocateTileArrays();
        catCell = new Vector2Int(width / 2, height / 2);
    }

    private void AllocateTileArrays()
    {
        mapBGTiles = new TileBase[width * height];
        walkableTiles = new TileBase[width * height];
        visualTiles = new TileBase[width * height];
    }

    private bool ArraysAreValid()
    {
        int length = width * height;
        return mapBGTiles != null && mapBGTiles.Length == length &&
               walkableTiles != null && walkableTiles.Length == length &&
               visualTiles != null && visualTiles.Length == length;
    }

    private TileBase[] GetActiveTiles()
    {
        switch (activeLayer)
        {
            case EditLayer.MapBG: return mapBGTiles;
            case EditLayer.WalkableMap: return walkableTiles;
            case EditLayer.MapVisual: return visualTiles;
            default: return mapBGTiles;
        }
    }

    private void FloodFill(TileBase[] tiles, int startX, int startY, TileBase replacement)
    {
        TileBase target = tiles[GetIndex(startX, startY)];
        if (target == replacement)
        {
            return;
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            int index = GetIndex(cell.x, cell.y);
            if (tiles[index] != target)
            {
                continue;
            }
            tiles[index] = replacement;
            TryQueueFill(tiles, target, cell.x + 1, cell.y, queue);
            TryQueueFill(tiles, target, cell.x - 1, cell.y, queue);
            TryQueueFill(tiles, target, cell.x, cell.y + 1, queue);
            TryQueueFill(tiles, target, cell.x, cell.y - 1, queue);
        }
    }

    private void TryQueueFill(TileBase[] tiles, TileBase target, int x, int y, Queue<Vector2Int> queue)
    {
        if (IsInsideGrid(x, y) && tiles[GetIndex(x, y)] == target)
        {
            queue.Enqueue(new Vector2Int(x, y));
        }
    }

    private bool ValidateBeforeSave(out string error)
    {
        if (templatePrefab == null)
        {
            error = "Hay chon Template prefab.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(levelName))
        {
            error = "Level Name khong duoc de trong.";
            return false;
        }
        if (outsideBGTile == null)
        {
            error = "Hay chon Tile nen ben ngoai cho MapBG.";
            return false;
        }
        if (mapBGTiles[GetIndex(catCell.x, catCell.y)] != null)
        {
            error = "Cat phai nam tren o MapBG trong (o co the di).";
            return false;
        }
        if (CountPlayableCells() == 0)
        {
            error = "Level phai co it nhat mot o MapBG trong.";
            return false;
        }
        if (!ArePlayableCellsConnected())
        {
            error = "Tat ca o MapBG trong phai lien thong de GameManager xac dinh dung vung choi.";
            return false;
        }
        error = null;
        return true;
    }

    private int CountPlayableCells()
    {
        int count = 0;
        foreach (TileBase tile in mapBGTiles)
        {
            if (tile == null)
            {
                count++;
            }
        }
        return count;
    }

    private bool ArePlayableCellsConnected()
    {
        int total = CountPlayableCells();
        if (total == 0)
        {
            return false;
        }

        int start = Array.FindIndex(mapBGTiles, tile => tile == null);
        Queue<int> queue = new Queue<int>();
        HashSet<int> visited = new HashSet<int>();
        queue.Enqueue(start);
        visited.Add(start);
        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % width;
            int y = index / width;
            TryVisitPlayable(x + 1, y, queue, visited);
            TryVisitPlayable(x - 1, y, queue, visited);
            TryVisitPlayable(x, y + 1, queue, visited);
            TryVisitPlayable(x, y - 1, queue, visited);
        }
        return visited.Count == total;
    }

    private void TryVisitPlayable(int x, int y, Queue<int> queue, HashSet<int> visited)
    {
        if (!IsInsideGrid(x, y))
        {
            return;
        }
        int index = GetIndex(x, y);
        if (mapBGTiles[index] == null && visited.Add(index))
        {
            queue.Enqueue(index);
        }
    }

    private void WriteLayer(Tilemap tilemap, TileBase[] tiles)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                tilemap.SetTile(ToMapCell(x, y), tiles[GetIndex(x, y)]);
            }
        }
        tilemap.CompressBounds();
    }

    private static void ClearBounds(Tilemap tilemap, BoundsInt bounds)
    {
        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            tilemap.SetTile(cell, null);
        }
    }

    private static void FillOutsideBackground(
        Tilemap mapBG,
        BoundsInt backgroundBounds,
        BoundsInt gameplayBounds,
        TileBase backgroundTile)
    {
        foreach (Vector3Int cell in backgroundBounds.allPositionsWithin)
        {
            mapBG.SetTile(cell, gameplayBounds.Contains(cell) ? null : backgroundTile);
        }
    }

    private static BoundsInt EncapsulateBounds(BoundsInt first, BoundsInt second)
    {
        if (first.size.x <= 0 || first.size.y <= 0)
        {
            return second;
        }

        int minX = Mathf.Min(first.xMin, second.xMin);
        int minY = Mathf.Min(first.yMin, second.yMin);
        int maxX = Mathf.Max(first.xMax, second.xMax);
        int maxY = Mathf.Max(first.yMax, second.yMax);
        return new BoundsInt(minX, minY, 0, maxX - minX, maxY - minY, 1);
    }

    private static TileBase FindMostUsedTileOutsideBounds(Tilemap tilemap, BoundsInt excludedBounds)
    {
        Dictionary<TileBase, int> counts = new Dictionary<TileBase, int>();
        foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
        {
            if (excludedBounds.Contains(cell))
            {
                continue;
            }

            TileBase tile = tilemap.GetTile(cell);
            if (tile == null)
            {
                continue;
            }

            counts.TryGetValue(tile, out int count);
            counts[tile] = count + 1;
        }

        return FindMostUsedTile(counts);
    }

    private static TileBase FindMostUsedTile(Tilemap tilemap)
    {
        Dictionary<TileBase, int> counts = new Dictionary<TileBase, int>();
        foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
        {
            TileBase tile = tilemap.GetTile(cell);
            if (tile == null)
            {
                continue;
            }

            counts.TryGetValue(tile, out int count);
            counts[tile] = count + 1;
        }

        return FindMostUsedTile(counts);
    }

    private static TileBase FindMostUsedTile(TileBase[] tiles)
    {
        Dictionary<TileBase, int> counts = new Dictionary<TileBase, int>();
        if (tiles == null)
        {
            return null;
        }

        foreach (TileBase tile in tiles)
        {
            if (tile == null)
            {
                continue;
            }

            counts.TryGetValue(tile, out int count);
            counts[tile] = count + 1;
        }

        return FindMostUsedTile(counts);
    }

    private static TileBase FindMostUsedTile(Dictionary<TileBase, int> counts)
    {
        TileBase result = null;
        int highestCount = 0;
        foreach (KeyValuePair<TileBase, int> pair in counts)
        {
            if (pair.Value <= highestCount)
            {
                continue;
            }

            result = pair.Key;
            highestCount = pair.Value;
        }

        return result;
    }

    private void SyncOutsideBGTile()
    {
        TileBase mostUsedMapBGTile = FindMostUsedTile(mapBGTiles);
        if (mostUsedMapBGTile != null)
        {
            outsideBGTile = mostUsedMapBGTile;
        }
    }

    private void AddToCurrentLevelManager(Level level)
    {
        LevelManager manager = UnityEngine.Object.FindObjectOfType<LevelManager>();
        if (manager == null)
        {
            SetStatus("Da luu prefab, nhung scene hien tai khong co LevelManager.", MessageType.Warning);
            return;
        }

        Undo.RecordObject(manager, "Add Long Cat Level");
        SerializedObject serializedManager = new SerializedObject(manager);
        SerializedProperty prefabs = serializedManager.FindProperty("levelPrefabs");
        for (int i = 0; i < prefabs.arraySize; i++)
        {
            if (prefabs.GetArrayElementAtIndex(i).objectReferenceValue == level)
            {
                return;
            }
        }
        int newIndex = prefabs.arraySize;
        prefabs.InsertArrayElementAtIndex(newIndex);
        prefabs.GetArrayElementAtIndex(newIndex).objectReferenceValue = level;
        serializedManager.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
    }

    private static Tilemap FindTilemap(GameObject root, string objectName)
    {
        foreach (Tilemap tilemap in root.GetComponentsInChildren<Tilemap>(true))
        {
            if (tilemap.name == objectName)
            {
                return tilemap;
            }
        }
        return null;
    }

    private static TileBase FirstTile(TileBase[] tiles)
    {
        return tiles == null ? null : Array.Find(tiles, tile => tile != null);
    }

    private static void EnsureOutputFolder()
    {
        string[] parts = OutputFolder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static string MakeSafeFileName(string rawName)
    {
        string result = rawName.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalid, '_');
        }
        return string.IsNullOrEmpty(result) ? "Level" : result;
    }

    private static string FindNextLevelName()
    {
        EnsureOutputFolder();
        int index = 1;
        while (AssetDatabase.LoadAssetAtPath<Level>($"{OutputFolder}/Level{index}.prefab") != null ||
               AssetDatabase.LoadAssetAtPath<Level>($"Assets/_Game/Prefabs/_Level/Level{index}.prefab") != null)
        {
            index++;
        }
        return $"Level{index}";
    }

    private Vector3Int ToMapCell(int localX, int localY)
    {
        return new Vector3Int(gridOrigin.x + localX, gridOrigin.y + localY, 0);
    }

    private int GetIndex(int x, int y)
    {
        return y * width + x;
    }

    private bool IsInsideGrid(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    private void SetStatus(string message, MessageType type)
    {
        statusMessage = message;
        statusType = type;
        Repaint();
    }
}
