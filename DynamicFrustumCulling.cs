using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Helpers
{
    public class DynamicFrustumCulling : MonoBehaviour
    {
        [Header("Object References")]
        [SerializeField] private Transform playerTransform;
        [SerializeField] private string referenceObjectTag = "Blackhole";

        [Header("Culling Settings")]
        [SerializeField] private float checkInterval = 0.2f;
        [SerializeField] private float cullingDistance = 1000f;
        [SerializeField] private float initializationDelay;
        [SerializeField] private float groundCheckRadius = 5f;
        [SerializeField] private float groundCheckHeight = 5f;
        [SerializeField] private int batchSize = 100;
        [SerializeField] private float frustumPadding = 1.5f;
        [SerializeField] private bool showHierarchyLogs;

        private Camera mainCamera;
        private float timer;
        private ConcurrentBag<CullableObject> cullableObjects;
        private CancellationTokenSource cancellationSource;
        private volatile bool isProcessing;
        private int highestId = 0;

        private Dictionary<int, Transform> transformCache;
        private Dictionary<int, Bounds> boundsCache;

        private class CullingData
        {
            public Vector3 Position;
            public Vector3 BoundsCenter;
            public Vector3 BoundsExtents;
            public bool ShouldBeEnabled;
            public int ObjectId;
        }
        
        private class FrameData
        {
            public Vector3 PlayerPosition;
            public Vector3 PlayerDownPosition;
            public Vector3 CameraPosition;
            public PlaneData[] FrustumPlanes;
        }
    
        private struct PlaneData
        {
            public readonly Vector3 Normal;
            public readonly float Distance;

            public PlaneData(Plane plane)
            {
                Normal = plane.normal;
                Distance = plane.distance;
            }
        }

        [System.Serializable]
        public class CullableObject
        {
            public MeshRenderer meshRenderer;
            public bool shouldBeEnabled;
            public int objectId;
            public bool isStatic;

            public CullableObject(MeshRenderer renderer, int id, bool isStatic)
            {
                meshRenderer = renderer;
                objectId = id;
                shouldBeEnabled = false;
                this.isStatic = isStatic;
            }
        }

        private void Start()
        {
            transformCache = new Dictionary<int, Transform>();
            boundsCache = new Dictionary<int, Bounds>();
            
            var playerObj = GameObject.FindGameObjectWithTag(referenceObjectTag);
            if (playerTransform == null && playerObj != null)
            {
                playerTransform = playerObj.transform;
            }

            cancellationSource = new CancellationTokenSource();
            StartCoroutine(InitializeWithDelay());
        }

        private bool IsBoundsInFrustum(Vector3 center, Vector3 extents, PlaneData[] planes)
        {
            for (var i = 0; i < planes.Length; i++)
            {
                var r = extents.x * Mathf.Abs(planes[i].Normal.x) +
                        extents.y * Mathf.Abs(planes[i].Normal.y) +
                        extents.z * Mathf.Abs(planes[i].Normal.z);

                var paddedRadius = r * frustumPadding;

                var s = Vector3.Dot(planes[i].Normal, center) + planes[i].Distance;

                if (s + paddedRadius < 0)
                    return false;
            }
            return true;
        }

        private bool IsNearPlayer(Vector3 objectPosition, Vector3 boundsMin, Vector3 boundsMax, FrameData frameData)
        {
            var objectDistanceFromPlayerXZ = Vector2.Distance(
                new Vector2(frameData.PlayerPosition.x, frameData.PlayerPosition.z),
                new Vector2(objectPosition.x, objectPosition.z)
            );

            var effectiveRadius = groundCheckRadius + Mathf.Max(
                (boundsMax.x - boundsMin.x) * 0.5f,
                (boundsMax.z - boundsMin.z) * 0.5f
            );

            return objectDistanceFromPlayerXZ < effectiveRadius &&
                   boundsMax.y >= frameData.PlayerDownPosition.y &&
                   boundsMin.y <= frameData.PlayerPosition.y;
        }

        private IEnumerator InitializeWithDelay()
        {
            yield return new WaitForSeconds(0.05f);

            if (playerTransform == null)
            {
                Debug.LogError("Player Transform not assigned!");
                yield break;
            }

            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("Main Camera not found!");
                yield break;
            }

            cullableObjects = new ConcurrentBag<CullableObject>();
            int idCounter = 0;

            var childRenderers = transform.GetComponentsInChildren<MeshRenderer>(true);
            
            foreach (var mr in childRenderers)
            {
                if (mr.gameObject == this.gameObject) continue;
                
                // Add bot static and non-static objects from the hierarchy
                cullableObjects.Add(new CullableObject(mr, idCounter, mr.gameObject.isStatic));
                
                transformCache[idCounter] = mr.transform;
                boundsCache[idCounter] = mr.bounds;
                
                mr.enabled = false;
                idCounter++;
            }

            //Debug.Log($"FrustumCullingManager initialized with {cullableObjects.Count} MeshRenderers in hierarchy.");

            if (showHierarchyLogs)
            {
                LogHierarchyStructure();
            }
            
        }

        private void ProcessObjectData(CullingData data, FrameData frameData)
        {
            if (IsNearPlayer(data.Position, 
                    data.BoundsCenter - data.BoundsExtents,
                    data.BoundsCenter + data.BoundsExtents,
                    frameData))
            {
                data.ShouldBeEnabled = true;
                return;
            }

            var distanceToCamera = Vector3.Distance(data.Position, frameData.CameraPosition);
            if (distanceToCamera > cullingDistance)
            {
                data.ShouldBeEnabled = false;
                return;
            }

            data.ShouldBeEnabled = IsBoundsInFrustum(data.BoundsCenter, data.BoundsExtents, frameData.FrustumPlanes);
        }

        private void LogHierarchyStructure(int depth = 0, Transform current = null)
        {
            if (current == null) current = transform;
            
            string indent = new string('-', depth * 2);
            string staticStatus = current.gameObject.isStatic ? "[Static]" : "[Dynamic]";
            bool hasMeshRenderer = current.GetComponent<MeshRenderer>() != null;
            
            Debug.Log($"{indent}{current.name} {staticStatus} {(hasMeshRenderer ? "[MeshRenderer]" : "")}");
            
            foreach (Transform child in current)
            {
                LogHierarchyStructure(depth + 1, child);
            }
        }

        private async void UpdateCullingAsync()
        {
            if (mainCamera == null || playerTransform == null || isProcessing) return;

            isProcessing = true;
        
            try
            {
                var frameData = new FrameData
                {
                    PlayerPosition = playerTransform.position,
                    PlayerDownPosition = playerTransform.position + Vector3.down * groundCheckHeight,
                    CameraPosition = mainCamera.transform.position,
                    FrustumPlanes = GetFrustumPlanes()
                };

                var objects = cullableObjects.ToArray();
                var cullingDataList = new ConcurrentBag<CullingData>();

                // Update cached bounds for non-static objects
                foreach (var obj in objects)
                {
                    if (!obj.isStatic && obj.meshRenderer != null)
                    {
                        boundsCache[obj.objectId] = obj.meshRenderer.bounds;
                    }
                }

                foreach (var obj in objects)
                {
                    if (obj.meshRenderer != null)
                    {
                        var bounds = boundsCache[obj.objectId];
                        cullingDataList.Add(new CullingData
                        {
                            Position = transformCache[obj.objectId].position,
                            BoundsCenter = bounds.center,
                            BoundsExtents = bounds.extents,
                            ObjectId = obj.objectId
                        });
                    }
                }

                var cullingData = cullingDataList.ToArray();
                var tasks = new List<Task>();

                for (int i = 0; i < cullingData.Length; i += batchSize)
                {
                    int start = i;
                    int count = Mathf.Min(batchSize, cullingData.Length - start);
                
                    tasks.Add(Task.Run(() =>
                    {
                        for (int j = start; j < start + count; j++)
                        {
                            ProcessObjectData(cullingData[j], frameData);
                        }
                    }, cancellationSource.Token));
                }

                await Task.WhenAll(tasks);

                foreach (var data in cullingData)
                {
                    foreach (var obj in objects)
                    {
                        if (obj.objectId == data.ObjectId && obj.meshRenderer != null)
                        {
                            obj.meshRenderer.enabled = data.ShouldBeEnabled;
                            break;
                        }
                    }
                }
            }
            finally
            {
                isProcessing = false;
            }
        }

        private PlaneData[] GetFrustumPlanes()
        {
            var unityPlanes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
            var planeData = new PlaneData[unityPlanes.Length];
            for (var i = 0; i < unityPlanes.Length; i++)
            {
                planeData[i] = new PlaneData(unityPlanes[i]);
            }
            return planeData;
        }

        public void AddDynamicObject(GameObject obj)
        {
            if (obj == null) return;

            // Verify the object is a child of this transform
            if (!IsChildOf(obj.transform, transform))
            {
                Debug.LogWarning($"Attempted to add object '{obj.name}' which is not a child of this FrustumCullingManager.");
                return;
            }

            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                Debug.LogWarning($"Attempted to add object '{obj.name}' without a MeshRenderer.");
                return;
            }

            int newId = System.Threading.Interlocked.Increment(ref highestId);
            
            transformCache[newId] = renderer.transform;
            boundsCache[newId] = renderer.bounds;
            
            cullableObjects.Add(new CullableObject(renderer, newId, obj.isStatic));
            renderer.enabled = false;
        }

        private bool IsChildOf(Transform child, Transform parent)
        {
            Transform current = child;
            while (current != null)
            {
                if (current == parent) return true;
                current = current.parent;
            }
            return false;
        }

        public void RemoveObject(GameObject obj)
        {
            if (obj == null) return;

            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            var currentObjects = cullableObjects.ToArray();
            var newObjects = new ConcurrentBag<CullableObject>();
        
            foreach (var cullable in currentObjects)
            {
                if (cullable.meshRenderer != renderer)
                {
                    newObjects.Add(cullable);
                }
                else
                {
                    transformCache.Remove(cullable.objectId);
                    boundsCache.Remove(cullable.objectId);
                }
            }
        
            cullableObjects = newObjects;
        }

        private void OnDestroy()
        {
            cancellationSource?.Cancel();
            cancellationSource?.Dispose();
            
            transformCache?.Clear();
            boundsCache?.Clear();
        }

        private void Update()
        {
            if (cullableObjects == null) return;

            timer += Time.deltaTime;
            if (timer >= checkInterval)
            {
                timer = 0f;
                UpdateCullingAsync();
            }
        }
    }
}