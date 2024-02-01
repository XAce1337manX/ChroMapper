using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Beatmap.Base;
using Beatmap.Base.Customs;
using Beatmap.Comparers;
using Beatmap.Containers;
using Beatmap.Enums;
using Beatmap.Helper;
using Beatmap.Shared;
using Beatmap.V2;
using UnityEngine;

public abstract class BeatmapObjectContainerCollection : MonoBehaviour
{
    public static readonly int ChunkSize = 5;

    public static float Epsilon = 0.001f;
    public static float TranslucentCull = -0.001f;

    private static BookmarkManager bookmarkManager;
    private static BookmarkManager bookmarkManagerInstance
        => bookmarkManager = bookmarkManager != null
            ? bookmarkManager
            : FindObjectOfType<BookmarkManager>();

    private static readonly Dictionary<ObjectType, BeatmapObjectContainerCollection> loadedCollections =
        new Dictionary<ObjectType, BeatmapObjectContainerCollection>();

    public event Action<BaseObject> ContainerSpawnedEvent;
    public event Action<BaseObject> ContainerDespawnedEvent;
    public AudioTimeSyncController AudioTimeSyncController;

    /// <summary>
    ///     Loaded objects in this collection.
    /// </summary>
    [Obsolete("LoadedObjects allocates a copy of the backing list of objects. Please avoid this unless you absolutely cannot grab a more precise type.")]
    public abstract List<BaseObject> LoadedObjects { get; }

    public BeatmapObjectCallbackController SpawnCallbackController;
    public BeatmapObjectCallbackController DespawnCallbackController;

    public Transform GridTransform;
    public Transform PoolTransform;
    public bool UseChunkLoadingWhenPlaying;
    public int ChunksLoadedWhilePlaying = 2;
    public bool IgnoreTrackFilter;

    private readonly Queue<ObjectContainer> pooledContainers = new Queue<ObjectContainer>();

    /// <summary>
    ///     A dictionary of all active BeatmapObjectContainers by the data they are attached to.
    /// </summary>
    public Dictionary<BaseObject, ObjectContainer> LoadedContainers =
        new Dictionary<BaseObject, ObjectContainer>();

    private float previousAtscBeat = -1;
    private int previousChunk = -1;

    public static string TrackFilterID { get; private set; }

    public abstract ObjectType ContainerType { get; }

    private void Awake()
    {
        ObjectContainer.FlaggedForDeletionEvent += DeleteObject;
        if (loadedCollections.ContainsKey(ContainerType))
            loadedCollections[ContainerType] = this;
        else
            loadedCollections.Add(ContainerType, this);
        SubscribeToCallbacks();
    }

    private void Start()
    {
        UpdateEpsilon(Settings.Instance.TimeValueDecimalPrecision);
        Settings.NotifyBySettingName("TimeValueDecimalPrecision", UpdateEpsilon);
        EditorScaleController.EditorScaleChangedEvent += UpdateTranslucentCull;
    }

    internal virtual void LateUpdate()
    {
        if ((AudioTimeSyncController.IsPlaying && !UseChunkLoadingWhenPlaying)
            || AudioTimeSyncController.CurrentSongBpmTime == previousAtscBeat)
        {
            return;
        }

        previousAtscBeat = AudioTimeSyncController.CurrentSongBpmTime;
        var nearestChunk = (int)Math.Round(previousAtscBeat / (double)ChunkSize, MidpointRounding.AwayFromZero);
        if (nearestChunk != previousChunk)
        {
            RefreshPool();
            previousChunk = nearestChunk;
        }
    }

    private void OnDestroy()
    {
        ObjectContainer.FlaggedForDeletionEvent -= DeleteObject;
        loadedCollections.Remove(ContainerType);
        UnsubscribeToCallbacks();
    }

    private void UpdateEpsilon(object precision)
    {
        Epsilon = 1 / Mathf.Pow(10, (int)precision);
        UpdateTranslucentCull(EditorScaleController.EditorScale);
    }

    // TODO(Caeden): Remove (unneeded)
    private void UpdateTranslucentCull(float editorScale) => TranslucentCull = -editorScale * Epsilon;

    /// <summary>
    ///     Grab a <see cref="BeatmapObjectContainerCollection" /> whose <see cref="ContainerType" /> matches the given type.
    ///     To grab an inherited class, consider using <see cref="GetCollectionForType{T}(BaseObject.ObjectType)" />.
    /// </summary>
    /// <param name="type">The specific type of <see cref="BaseObject" /> that the collection must contain.</param>
    /// <returns>A generic <see cref="BeatmapObjectContainerCollection" />.</returns>
    public static BeatmapObjectContainerCollection GetCollectionForType(ObjectType type)
    {
        loadedCollections.TryGetValue(type, out var collection);
        return collection;
    }

    /// <summary>
    ///     Grab a <see cref="BeatmapObjectContainerCollection" /> whose <see cref="ContainerType" /> matches the given type.
    /// </summary>
    /// <typeparam name="T">A specific inheriting class to cast to.</typeparam>
    /// <param name="type">The specific type of <see cref="BaseObject" /> that the collection must contain.</param>
    /// <returns>A casted <see cref="BeatmapObjectContainerCollection" />.</returns>
    public static T GetCollectionForType<T>(ObjectType type) where T : BeatmapObjectContainerCollection
    {
        loadedCollections.TryGetValue(type, out var collection);
        return collection as T;
    }

    /// <summary>
    ///     Grab a <see cref="BeatmapObjectContainerCollection" /> whose <see cref="ContainerType" /> matches the given type.
    /// </summary>
    /// <typeparam name="T">A specific inheriting class to cast to.</typeparam>
    /// <param name="type">The specific type of <see cref="BaseObject" /> that the collection must contain.</param>
    /// <returns>A casted <see cref="BeatmapObjectContainerCollection" />.</returns>
    public static T GetCollectionForType<T, TBaseObject>() where T : BeatmapObjectContainerCollection where TBaseObject : BaseObject
    {
        // god C# please let us switch directly by types instead of this garbage workaround
        var type = typeof(TBaseObject) switch
        {
            Type t when t == typeof(BaseNote) => ObjectType.Note,
            Type t when t == typeof(BaseObstacle) => ObjectType.Obstacle,
            Type t when t == typeof(BaseEvent) => ObjectType.Event,
            Type t when t == typeof(BaseArc) => ObjectType.Arc,
            Type t when t == typeof(BaseChain) => ObjectType.Chain,
            Type t when t == typeof(BaseBpmEvent) => ObjectType.BpmChange,
            Type t when t == typeof(BaseCustomEvent) => ObjectType.CustomEvent,
            Type t when t == typeof(BaseBookmark) => ObjectType.Bookmark,
            _ => throw new ArgumentException(nameof(TBaseObject))
        };

        loadedCollections.TryGetValue(type, out var collection);
        return collection as T;
    }

    /// <summary>
    ///     Refreshes pools of all active <see cref="BeatmapObjectContainerCollection" />
    /// </summary>
    /// <param name="forceRefresh">
    ///     Whether or not to forcefully recycle all containers and spawn them again. This will cause
    ///     quite a bit of lag.
    /// </param>
    public static void RefreshAllPools(bool forceRefresh = false)
    {
        foreach (var collection in loadedCollections.Values) collection.RefreshPool(forceRefresh);
    }

    /// <summary>
    ///     Refreshes the pool, with lower and upper bounds being automatically defined by chunks or spawn/despawn offsets.
    /// </summary>
    /// <param name="forceRefresh">All currently active containers will be recycled, even if they shouldn't be.</param>
    public virtual void RefreshPool(bool forceRefresh = false)
    {
        var epsilon = Mathf.Pow(10, -9);
        if (AudioTimeSyncController.IsPlaying)
        {
            var spawnOffset = UseChunkLoadingWhenPlaying
                ? ChunksLoadedWhilePlaying * ChunkSize
                : SpawnCallbackController.Offset;
            var despawnOffset = UseChunkLoadingWhenPlaying
                ? -ChunksLoadedWhilePlaying * ChunkSize
                : DespawnCallbackController.Offset;
            RefreshPool(AudioTimeSyncController.CurrentSongBpmTime + despawnOffset - epsilon,
                AudioTimeSyncController.CurrentSongBpmTime + spawnOffset + epsilon, forceRefresh);
        }
        else
        {
            var nearestChunk = (int)Math.Round(previousAtscBeat / (double)ChunkSize, MidpointRounding.AwayFromZero);
            // Since ChunkDistance is the amount of total chunks, we divide by two so that the total amount of loaded chunks
            // both before and after the current one equal to the ChunkDistance setting
            var chunks = Mathf.RoundToInt(Settings.Instance.ChunkDistance / 2);
            RefreshPool(((nearestChunk - chunks) * ChunkSize) - epsilon,
                ((nearestChunk + chunks) * ChunkSize) + epsilon, forceRefresh);
        }
    }

    /// <summary>
    ///     Refreshes the pool with a defined lower and upper bound.
    /// </summary>
    /// <param name="lowerBound">Objects below this point in time will not be given a container.</param>
    /// <param name="upperBound">Objects above this point in time will not be given a container.</param>
    /// <param name="forceRefresh">All currently active containers will be recycled, even if they shouldn't be.</param>
    public abstract void RefreshPool(float lowerBound, float upperBound, bool forceRefresh = false);

    /// <summary>
    ///     Dequeues a container from the pool and attaches it to a provided <see cref="BaseObject" />
    /// </summary>
    /// <param name="obj">Object to store within the container.</param>
    protected void CreateContainerFromPool(BaseObject obj)
    {
        if (obj.HasAttachedContainer) return;
        //Debug.Log($"Creating container with hash code {obj.GetHashCode()}");
        if (!pooledContainers.Any()) CreateNewObject();
        var dequeued = pooledContainers.Dequeue();
        dequeued.ObjectData = obj;
        dequeued.transform.localEulerAngles = Vector3.zero;
        dequeued.UpdateGridPosition();
        dequeued.SafeSetActive(true);
        UpdateContainerData(dequeued, obj);
        dequeued.OutlineVisible = SelectionController.IsObjectSelected(obj);
        PluginLoader.BroadcastEvent<ObjectLoadedAttribute, ObjectContainer>(dequeued);
        LoadedContainers.Add(obj, dequeued);
        obj.HasAttachedContainer = true;
        OnContainerSpawn(dequeued, obj);
        ContainerSpawnedEvent?.Invoke(obj);
    }

    /// <summary>
    ///     Recycles the container belonging to a provided <see cref="BaseObject" />, putting it back into the container
    ///     pool for future use.
    /// </summary>
    /// <param name="obj">Object whose container will be recycled.</param>
    protected internal void RecycleContainer(BaseObject obj)
    {
        if (!obj.HasAttachedContainer) return;
        //Debug.Log($"Recycling container with hash code {obj.GetHashCode()}");
        var container = LoadedContainers[obj];
        container.ObjectData = null;
        container.SafeSetActive(false);
        //container.transform.SetParent(PoolTransform);
        LoadedContainers.Remove(obj);
        pooledContainers.Enqueue(container);
        OnContainerDespawn(container, obj);
        obj.HasAttachedContainer = false;
        ContainerDespawnedEvent?.Invoke(obj);
    }

    private void CreateNewObject()
    {
        var baseContainer = CreateContainer();
        baseContainer.gameObject.SetActive(false);
        baseContainer.Setup();
        //baseContainer.transform.SetParent(PoolTransform);
        baseContainer.transform.SetParent(GridTransform);
        pooledContainers.Enqueue(baseContainer);
    }

    /// <summary>
    ///     Given a list of objects, remove all existing ones that conflict.
    /// </summary>
    /// <param name="newObjects">Enumerable of new objects</param>
    public void RemoveConflictingObjects(IEnumerable<BaseObject> newObjects) => RemoveConflictingObjects(newObjects, out _);

    /// <summary>
    ///     Given a list of objects, remove all existing ones that conflict.
    /// </summary>
    /// <param name="newObjects">Enumerable of new objects</param>
    /// <param name="conflicting">Enumerable of all existing objects that were deleted as a conflict.</param>
    public abstract void RemoveConflictingObjects(IEnumerable<BaseObject> newObjects, out List<BaseObject> conflicting);

    /// <summary>
    ///     Given a <see cref="ObjectContainer" />, delete its attached object.
    /// </summary>
    /// <param name="obj">To delete.</param>
    /// <param name="triggersAction">Whether or not it triggers a <see cref="BeatmapObjectDeletionAction" /></param>
    /// <param name="comment">A comment that provides further description on why it was deleted.</param>
    /// <param name="inCollectionOfDeletes">
    ///     Whether or not spawning is part of a collection of spawns
    ///     Set to true and call <see cref="DoPostObjectsDeleteWorkflow()" /> after to optimise spawning many objects
    ///</param>
    public void DeleteObject(ObjectContainer obj, bool triggersAction = true, string comment = "No comment.", bool inCollectionOfDeletes = false) =>
        DeleteObject(obj.ObjectData, triggersAction, true, comment);

    // Identical to above but I need this as the action doesn't work with option parameters
    public void DeleteObject(ObjectContainer obj, bool triggersAction = true, string comment = "No comment.") =>
        DeleteObject(obj.ObjectData, triggersAction, true, comment);

    /// <summary>
    ///     Deletes a <see cref="BaseObject" />.
    /// </summary>
    /// <param name="obj">To delete.</param>
    /// <param name="triggersAction">Whether or not it triggers a <see cref="BeatmapObjectDeletionAction" /></param>
    /// <param name="refreshesPool">Whether or not the pool will be refreshed as a result of this deletion.</param>
    /// <param name="comment">A comment that provides further description on why it was deleted.</param>
    /// <param name="inCollectionOfDeletes">
    ///     Whether or not spawning is part of a collection of spawns
    ///     Set to true and call <see cref="DoPostObjectsDeleteWorkflow()" /> after to optimise spawning many objects
    ///</param>
    ///<param name="deselect">Whether or not this object is immediately deselected upon deletion.</param>
    public abstract void DeleteObject(BaseObject obj, bool triggersAction = true, bool refreshesPool = true,
        string comment = "No comment.", bool inCollectionOfDeletes = false, bool deselect = true);

    public abstract void SilentRemoveObject(BaseObject obj);
    
    protected void SetTrackFilter() =>
        PersistentUI.Instance.ShowInputBox("Filter notes and obstacles shown while editing to a certain track ID.\n\n" +
                                           "If you dont know what you're doing, turn back now.", HandleTrackFilter);

    private void HandleTrackFilter(string res) =>
        TrackFilterID = string.IsNullOrEmpty(res) || string.IsNullOrWhiteSpace(res) ? null : res;

    /// <summary>
    ///     Spawns an object into the collection.
    /// </summary>
    /// <param name="obj">To spawn.</param>
    /// <param name="removeConflicting">
    ///     Whether or not <see cref="RemoveConflictingObjects(IEnumerable{BaseObject})" /> will
    ///     be called.
    /// </param>
    /// <param name="refreshesPool">Whether or not the pool will be refreshed.</param>
    /// <param name="inCollectionOfSpawns">Whether OnObjectSpawned will be called.</param>
    /// <param name="inCollectionOfSpawns">
    ///     Whether or not spawning is part of a collection of spawns
    ///     Set to true and call <see cref="DoPostObjectsSpawnedWorkflow()" /> after to optimise spawning many objects
    ///</param>
    public void SpawnObject(BaseObject obj, bool removeConflicting = true, bool refreshesPool = true, bool inCollectionOfSpawns = false) =>
        SpawnObject(obj, out _, removeConflicting, refreshesPool, inCollectionOfSpawns);

    /// <summary>
    ///     Spawns an object into the collection.
    /// </summary>
    /// <param name="obj">To spawn.</param>
    /// <param name="conflicting">An enumerable of all objects that were deleted as a conflict.</param>
    /// <param name="removeConflicting">
    ///     Whether or not
    ///     <see cref="RemoveConflictingObjects(IEnumerable{BaseObject}, out IEnumerable{BaseObject})" /> will be called.
    /// </param>
    /// <param name="refreshesPool">Whether or not the pool will be refreshed.</param>
    /// <param name="inCollectionOfSpawns">
    ///     Whether or not spawning is part of a collection of spawns.
    ///     Set to true and call <see cref="DoPostObjectsSpawnedWorkflow()" /> after to optimise spawning many objects
    ///</param>
    public abstract void SpawnObject(BaseObject obj, out List<BaseObject> conflicting, bool removeConflicting = true,
        bool refreshesPool = true, bool inCollectionOfSpawns = false);

    /// <summary>
    /// Returns <c>true</c> if the given object exists within this collection, and <c>false</c> otherwise.
    /// </summary>
    public abstract bool ContainsObject(BaseObject obj);

    public static void RefreshFutureObjectsPosition(float jsonTime)
    {
        foreach (var objectType in System.Enum.GetValues(typeof(Beatmap.Enums.ObjectType)))
        {
            var collection = BeatmapObjectContainerCollection.GetCollectionForType((Beatmap.Enums.ObjectType)objectType);
            if (collection == null) continue;
            // REVIEW: not sure if allocation is avoidable
            foreach (var obj in collection.LoadedObjects)
            {
                if (obj.JsonTime > jsonTime)
                {
                    obj.RecomputeSongBpmTime();
                }
                else if (collection is ChainGridContainer || collection is ArcGridContainer)
                {
                    if ((obj as BaseSlider).TailJsonTime > jsonTime)
                    {
                        obj.RecomputeSongBpmTime();
                    }
                }
            }
            foreach (var container in collection.LoadedContainers)
            {
                if (container.Key.JsonTime > jsonTime)
                {
                    container.Value.UpdateGridPosition();
                }
                else if (collection is ObstacleGridContainer)
                {
                    if (container.Key.JsonTime + (container.Key as BaseObstacle).Duration > jsonTime)
                    {
                        container.Value.UpdateGridPosition();
                    }
                }
                else if (collection is ChainGridContainer || collection is ArcGridContainer)
                {
                    if ((container.Key as BaseSlider).TailJsonTime > jsonTime)
                    {
                        container.Value.UpdateGridPosition();
                    }
                }
            }
        }

        // Bookmarks aren't in the ContainerCollection yet so we have this
        foreach (var bookmark in bookmarkManagerInstance.bookmarkContainers)
        {
            if (bookmark.Data.JsonTime > jsonTime)
            {
                bookmark.Data.RecomputeSongBpmTime();
            }
        }
        bookmarkManagerInstance.RefreshBookmarkTimelinePositions();
    }

    protected virtual void UpdateContainerData(ObjectContainer con, BaseObject obj) { }

    protected virtual void OnObjectDelete(BaseObject obj, bool inCollection = false) { }

    protected virtual void OnObjectSpawned(BaseObject obj, bool inCollection = false) { }

    protected virtual void OnContainerSpawn(ObjectContainer container, BaseObject obj) { }

    protected virtual void OnContainerDespawn(ObjectContainer container, BaseObject obj) { }

    public virtual void DoPostObjectsSpawnedWorkflow() { }

    public virtual void DoPostObjectsDeleteWorkflow() { }

    public abstract ObjectContainer CreateContainer();

    internal abstract void SubscribeToCallbacks();

    internal abstract void UnsubscribeToCallbacks();
}

public abstract class BeatmapObjectContainerCollection<T> : BeatmapObjectContainerCollection where T : BaseObject
{
    public event Action<T> ObjectSpawnedEvent;
    public event Action<T> ObjectDeletedEvent;

    [Obsolete("LoadedObjects allocates a copy of the backing list of objects. Please avoid this unless you absolutely cannot grab a more precise type.")]
    public override List<BaseObject> LoadedObjects => MapObjects.ConvertAll(it => it as BaseObject);

    public List<T> MapObjects = new();

    public List<T> GetBetween(float jsonTime, float jsonTime2)
    {
        return MapObjects.FindAll(it => it.JsonTime >= jsonTime - Epsilon && it.JsonTime <= jsonTime2 + Epsilon);
        /*
        // Considering we're only concerned with time, we'll use a time-based comparer here.
        var startIdx = MapObjects.BinarySearchBy(jsonTime, obj => obj.JsonTime);
        var endIdx = MapObjects.BinarySearchBy(jsonTime2, obj => obj.JsonTime);

        if (startIdx < 0) startIdx = ~startIdx;
        if (endIdx < 0) endIdx = ~endIdx;

        return MapObjects.GetRange(startIdx, endIdx - startIdx);
        */
    }

    /// <summary>
    ///     Given a list of objects, remove all existing ones that conflict.
    /// </summary>
    /// <param name="newObjects">Enumerable of new objects</param>
    public void RemoveConflictingObjects(IEnumerable<T> newObjects) =>
        RemoveConflictingObjects(newObjects, out _);

    /// <inheritdoc/>
    public override void RemoveConflictingObjects(IEnumerable<BaseObject> newObjects, out List<BaseObject> conflicting)
    {
        RemoveConflictingObjects(newObjects.OfType<T>(), out var localConflicting);

        conflicting = localConflicting.ConvertAll(it => it as BaseObject);
    }

    /// <summary>
    ///     Given a list of objects, remove all existing ones that conflict.
    /// </summary>
    /// <param name="newObjects">Enumerable of new objects</param>
    /// <param name="conflicting">Enumerable of all existing objects that were deleted as a conflict.</param>
    public void RemoveConflictingObjects(IEnumerable<T> newObjects, out List<T> conflicting)
    {
        conflicting = new List<T>();

        foreach (var newObject in newObjects)
        {
            Debug.Log($"Performing conflicting check at {newObject.JsonTime}");

            var localWindow = GetBetween(newObject.JsonTime - 0.1f, newObject.JsonTime + 0.1f);

            for (var i = 0; i < localWindow.Count; i++)
            {
                var obj = localWindow[i];

                if (obj.IsConflictingWith(newObject) && newObject != obj) conflicting.Add(obj);
            }
        }

        conflicting.ForEach(conflict => DeleteObject(conflict, false, false));

        Debug.Log($"Removed {conflicting.Count} conflicting {ContainerType}s.");
    }

    /// <inheritdoc/>
    public override void RefreshPool(float lowerBound, float upperBound, bool forceRefresh = false)
    {
        // TODO: Convert to Span<> iteration
        for (var i = 0; i < MapObjects.Count; i++)
        {
            var obj = MapObjects[i];

            if (forceRefresh) RecycleContainer(obj);

            switch (obj)
            {
                // Create container if obj is within bounds
                case not null when obj.SongBpmTime >= lowerBound && obj.SongBpmTime <= upperBound:
                    CreateContainerFromPool(obj);
                    continue;

                // Handle special cases for certain objects exist over a period of time
                case BaseObstacle obs when obs.SongBpmTime < lowerBound && obs.SongBpmTime + obs.Duration >= lowerBound:
                    CreateContainerFromPool(obj);
                    continue;
                case BaseArc arc when arc.SongBpmTime < lowerBound && arc.TailSongBpmTime >= lowerBound:
                    CreateContainerFromPool(obj);
                    continue;
                case BaseChain chain when chain.SongBpmTime < lowerBound && chain.TailSongBpmTime >= lowerBound:
                    CreateContainerFromPool(obj);
                    continue;

                // Outside of bounds; recycle
                default:
                    RecycleContainer(obj);
                    break;
            }
        }
    }

    /// <inheritdoc/>
    public override void DeleteObject(BaseObject obj, bool triggersAction = true, bool refreshesPool = true,
        string comment = "No comment.", bool inCollectionOfDeletes = false, bool deselect = true)
    {
        if (obj is not T localObj) return;

        DeleteObject(localObj, triggersAction, refreshesPool, comment, inCollectionOfDeletes, deselect);
    }

    /// <inheritdoc/>
    // TODO(Caeden): Overload to delete/spawn without recycling or creating a container
    public void DeleteObject(T obj, bool triggersAction = true, bool refreshesPool = true,
        string comment = "No comment.", bool inCollectionOfDeletes = false, bool deselect = true)
    {
        var search = MapObjects.BinarySearch(obj);
        
        if (!HasFoundCorrectObject(search, obj)) return;
        
        RecycleContainer(obj);
        
        MapObjects.RemoveAt(search);

        if (deselect) SelectionController.Deselect(obj, triggersAction);

        if (triggersAction) BeatmapActionContainer.AddAction(new BeatmapObjectDeletionAction(obj, comment));

        if (refreshesPool) RefreshPool();

        OnObjectDelete(obj, inCollectionOfDeletes);
        ObjectDeletedEvent?.Invoke(obj);
    }
    
    // Removes object from MapObjects while retaining container and data in it
    public override void SilentRemoveObject(BaseObject obj)
    {
        if (obj is not T tObj) return;
        
        var search = MapObjects.BinarySearch(tObj);

        if (!HasFoundCorrectObject(search, tObj)) return;
        
        MapObjects.RemoveAt(search);
    }

    private bool HasFoundCorrectObject(int search, BaseObject obj)
    {
        // Unhappy path: Binary Search returns negative number
        if (search < 0)
        {
            // The objects are not in the collection, but are still being removed.
            // This could be because of ghost blocks, so let's try forcefully recycling that container.
            Debug.LogError($"This object is not in the collection and appears to be a ghost. Please report this.");
            
            return false;
        }

        // Unhappy path: Binary Search returns an object, but turns out to be the incorrect object.
        if (MapObjects[search] != obj)
        {
            // Binary Search returned a value, but this value is not the object we're looking to delete.
            Debug.LogError("Binary Search returned incorrect object. Please report this.");

            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override void SpawnObject(BaseObject obj, out List<BaseObject> conflicting, bool removeConflicting = true,
        bool refreshesPool = true, bool inCollectionOfSpawns = false)
    {
        conflicting = new List<BaseObject>();

        if (obj is not T localObj) return;

        SpawnObject(localObj, out var localConflicting, removeConflicting, refreshesPool, inCollectionOfSpawns);

        for (var i = 0; i < localConflicting.Count; i++)
        {
            conflicting.Add(localConflicting[i]);
        }
    }

    /// <inheritdoc/>
    public void SpawnObject(T obj, bool removeConflicting = true, bool refreshesPool = true, bool inCollectionOfSpawns = false) =>
        SpawnObject(obj, out _, removeConflicting, refreshesPool, inCollectionOfSpawns);

    /// <inheritdoc/>
    // TODO(Caeden): Overload to delete/spawn without recycling or creating a container
    public void SpawnObject(T obj, out List<T> conflicting, bool removeConflicting = true,
        bool refreshesPool = true, bool inCollectionOfSpawns = false)
    {
        //Debug.Log($"Spawning object with hash code {obj.GetHashCode()}");
        if (removeConflicting)
        {
            RemoveConflictingObjects(new T[] { obj }, out conflicting);
        }
        else
        {
            conflicting = new List<T>();
        }

        var search = MapObjects.BinarySearch(obj);
        var insertIdx = search >= 0 ? search : ~search;
        MapObjects.Insert(insertIdx, obj);

        OnObjectSpawned(obj, inCollectionOfSpawns);
        ObjectSpawnedEvent?.Invoke(obj);

        //Debug.Log($"Total object count: {LoadedObjects.Count}");
        if (refreshesPool) RefreshPool();
    }

    /// <inheritdoc/>
    public override bool ContainsObject(BaseObject obj) => obj is T localObj && ContainsObject(localObj);

    /// <inheritdoc/>
    public bool ContainsObject(T obj) => MapObjects.BinarySearch(obj) >= 0;
}
