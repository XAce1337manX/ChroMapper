﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapLoader : MonoBehaviour
{
    [SerializeField] private TracksManager manager;
    [SerializeField] private NoteLanesController noteLanesController;

    [Space] [SerializeField] private Transform containerCollectionsContainer;

    private BeatSaberMap map;
    private int noteLaneSize = 2;
    private int noteLayerSize = 3;

    public void UpdateMapData(BeatSaberMap map)
    {
        if (map is BeatSaberMapV3)
        {
            var copy = new BeatSaberMapV3
            {
                CustomData = map.CustomData.Clone(),
                Notes = new List<BeatmapNote>(map.Notes),
                Obstacles = new List<BeatmapObstacle>(map.Obstacles),
                Arcs = new List<BeatmapArc>((map as BeatSaberMapV3).Arcs),
                Chains = new List<BeatmapChain>((map as BeatSaberMapV3).Chains),
                Events = new List<MapEvent>(map.Events),
                BpmChanges = new List<BeatmapBPMChange>(map.BpmChanges),
                ColorBoostBeatmapEvents = new List<ColorBoostEvent>((map as BeatSaberMapV3).ColorBoostBeatmapEvents),
                CustomEvents = new List<BeatmapCustomEvent>(map.CustomEvents)
            };
            this.map = copy;
        }
        else
        {
            var copy = new BeatSaberMap
            {
                CustomData = map.CustomData.Clone(),
                Notes = new List<BeatmapNote>(map.Notes),
                Obstacles = new List<BeatmapObstacle>(map.Obstacles),
                Events = new List<MapEvent>(map.Events),
                BpmChanges = new List<BeatmapBPMChange>(map.BpmChanges),
                CustomEvents = new List<BeatmapCustomEvent>(map.CustomEvents)
            };
            this.map = copy;
        }

    }

    public IEnumerator HardRefresh()
    {
        if (Settings.Instance.Load_Notes) yield return StartCoroutine(LoadObjects(map.Notes));
        if (Settings.Instance.Load_Obstacles) yield return StartCoroutine(LoadObjects(map.Obstacles));
        if (Settings.Instance.Load_Events) yield return StartCoroutine(LoadObjects(map.Events));
        if (Settings.Instance.Load_Others)
        {
            yield return StartCoroutine(LoadObjects(map.BpmChanges));
            yield return StartCoroutine(LoadObjects(map.CustomEvents));
        }
        if (Settings.Instance.Load_MapV3)
        {
            yield return StartCoroutine(LoadObjects((map as BeatSaberMapV3).Arcs));
            yield return StartCoroutine(LoadObjects((map as BeatSaberMapV3).Chains));
        }

        PersistentUI.Instance.LevelLoadSliderLabel.text = "Finishing up...";
        manager.RefreshTracks();
        SelectionController.RefreshMap();
        PersistentUI.Instance.LevelLoadSlider.gameObject.SetActive(false);
    }

    public IEnumerator LoadObjects<T>(IEnumerable<T> objects) where T : BeatmapObject
    {
        if (!objects.Any()) yield break;
        var collection = BeatmapObjectContainerCollection.GetCollectionForType(objects.First().BeatmapType);
        if (collection == null) yield break;
        foreach (var obj in collection.LoadedObjects.ToArray()) collection.DeleteObject(obj, false, false);
        PersistentUI.Instance.LevelLoadSlider.gameObject.SetActive(true);
        collection.LoadedObjects = new SortedSet<BeatmapObject>(objects, new BeatmapObjectComparer());
        collection.UnsortedObjects = collection.LoadedObjects.ToList();
        UpdateSlider<T>();
        if (typeof(T) == typeof(BeatmapNote) || typeof(T) == typeof(BeatmapObstacle))
        {
            for (var i = 0; i < objects.Count(); i++)
            {
                BeatmapObject data = objects.ElementAt(i);
                if (data is BeatmapNote noteData)
                {
                    if (noteData.LineIndex >= 1000 || noteData.LineIndex <= -1000 || noteData.LineLayer >= 1000 ||
                        noteData.LineLayer <= -1000)
                    {
                        continue;
                    }

                    if (2 - noteData.LineIndex > noteLaneSize) noteLaneSize = 2 - noteData.LineIndex;
                    if (noteData.LineIndex - 1 > noteLaneSize) noteLaneSize = noteData.LineIndex - 1;
                    if (noteData.LineLayer + 1 > noteLayerSize) noteLayerSize = noteData.LineLayer + 1;
                }
                else if (data is BeatmapObstacle obstacleData)
                {
                    if (obstacleData.LineIndex >= 1000 || obstacleData.LineIndex <= -1000) continue;
                    if (2 - obstacleData.LineIndex > noteLaneSize) noteLaneSize = 2 - obstacleData.LineIndex;
                    if (obstacleData.LineIndex - 1 > noteLaneSize) noteLaneSize = obstacleData.LineIndex - 1;
                }
            }

            if (Settings.NonPersistentSettings.ContainsKey("NoteLanes"))
                Settings.NonPersistentSettings["NoteLanes"] = (noteLaneSize * 2).ToString();
            else
                Settings.NonPersistentSettings.Add("NoteLanes", (noteLaneSize * 2).ToString());
            noteLanesController.UpdateNoteLanes((noteLaneSize * 2).ToString());
        }

        if (typeof(T) == typeof(MapEvent))
        {
            manager.RefreshTracks();
            var events = collection as EventsContainer;
            events.AllRotationEvents = objects.Cast<MapEvent>().Where(x => x.IsRotationEvent).ToList();
            events.AllBoostEvents = objects.Cast<MapEvent>().Where(x => x.Type == MapEvent.EventTypeBoostLights)
                .ToList();

            var AllLightEvents = objects.Cast<MapEvent>().Where(x => !x.IsUtilityEvent).ToList();
            events.EventsSplitByType = new Dictionary<int, List<MapEvent>>();
            events.EventsSplitByTypeAndLightID = new Dictionary<int, Dictionary<string, List<MapEvent>>>();
            foreach (MapEvent e in AllLightEvents)
            {
                List<MapEvent> eventTypeList = events.EventsSplitByType.TryGetValue(e.Type, out eventTypeList)
                    ? eventTypeList
                    : new List<MapEvent>();
                eventTypeList.Add(e);
                events.EventsSplitByType[e.Type] = eventTypeList;

                Dictionary<string, List<MapEvent>> eventTypeDict = events.EventsSplitByTypeAndLightID.TryGetValue(e.Type, out eventTypeDict)
                    ? eventTypeDict
                    : new Dictionary<string, List<MapEvent>>();
                string lightID = (e.CustomData != null && e.CustomData["_lightID"] != null)
                    ? string.Join(",", e.LightId)
                    : string.Join(",", new int[] {-1});
                List<MapEvent> eventTypeIDList = eventTypeDict.TryGetValue(lightID, out eventTypeIDList)
                    ? eventTypeIDList
                    : new List<MapEvent>();
                eventTypeIDList.Add(e);
                eventTypeDict[lightID] = eventTypeIDList;
                events.EventsSplitByTypeAndLightID[e.Type] = eventTypeDict;
            }
        }

        collection.RefreshPool(true);
    }

    private void UpdateSlider<T>() where T : BeatmapObject
    {
        PersistentUI.Instance.LevelLoadSliderLabel.text = $"Loading {typeof(T).Name}s... ";
        PersistentUI.Instance.LevelLoadSlider.value = 1;
    }
}
