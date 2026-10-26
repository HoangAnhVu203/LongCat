using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Contains every scene reference that belongs to one level prefab.
/// Put MapBG, the visual tilemaps and Cat below this object.
/// The Cat transform inside the prefab is also its spawn position.
/// </summary>
public sealed class Level : MonoBehaviour
{
    [SerializeField] private Tilemap mapBG;
    [SerializeField] private Tilemap mapVisual;
    [SerializeField] private Cat cat;

    public Tilemap MapBG
    {
        get
        {
            ResolveReferences();
            return mapBG;
        }
    }

    public Cat Cat
    {
        get
        {
            ResolveReferences();
            return cat;
        }
    }

    public Tilemap MapVisual
    {
        get
        {
            ResolveReferences();
            return mapVisual;
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveReferences();
    }
#endif

    private void ResolveReferences()
    {
        if (mapBG == null)
        {
            Tilemap[] tilemaps = GetComponentsInChildren<Tilemap>(true);
            foreach (Tilemap tilemap in tilemaps)
            {
                if (tilemap.name == "MapBG")
                {
                    mapBG = tilemap;
                    break;
                }
            }
        }

        if (mapVisual == null)
        {
            Tilemap[] tilemaps = GetComponentsInChildren<Tilemap>(true);
            foreach (Tilemap tilemap in tilemaps)
            {
                if (tilemap.name == "MapVisual")
                {
                    mapVisual = tilemap;
                    break;
                }
            }
        }

        if (cat == null)
        {
            cat = GetComponentInChildren<Cat>(true);
        }
    }
}
