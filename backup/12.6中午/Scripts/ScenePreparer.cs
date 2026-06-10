// ScenePreparer.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class ScenePreparer : MonoBehaviour
{
    public Shader gBufferShader;

    public Texture2DArray ColorMapsArray { get; private set; }
    public List<RenderTexture> IdMaps { get; private set; }
    public List<RenderTexture> PosMaps { get; private set; }
    public Matrix4x4[] CameraVPMatricesArray { get; private set; }

    public ComputeBuffer ObjectHistoryBuffer { get; private set; }
    public int MaxTrackableCount { get; private set; }
    public Dictionary<int, int> ObjectIdToIndexMap { get; private set; }

    public struct ObjectTransformData
    {
        public Vector3 position;
        public float padding;
        public Quaternion rotation;
    }

    private Camera mainCamera;
    private TrackableObject[] trackableObjects;
    private MaterialPropertyBlock propBlock;

    void Awake()
    {
        mainCamera = Camera.main;
        if (gBufferShader == null) Debug.LogError("ScenePreparer: 未指定 G-Buffer Shader！");

        IdMaps = new List<RenderTexture>();
        PosMaps = new List<RenderTexture>();
        propBlock = new MaterialPropertyBlock();
    }

    public IEnumerator PrecomputationCoroutine(System.Action<string> onProgress, System.Action onComplete)
    {
        Debug.Log("开始预计算...");
        trackableObjects = FindObjectsOfType<TrackableObject>();
        int objCount = trackableObjects.Length;
        int frameCount = SceneHistory.History.Count;
        MaxTrackableCount = objCount;

        if (frameCount == 0) { onComplete?.Invoke(); yield break; }

        ObjectIdToIndexMap = new Dictionary<int, int>();
        for (int i = 0; i < objCount; i++) ObjectIdToIndexMap[trackableObjects[i].UniqueId] = i;

        IdMaps.ForEach(rt => rt.Release()); IdMaps.Clear();
        PosMaps.ForEach(rt => rt.Release()); PosMaps.Clear();
        if (ColorMapsArray != null) Destroy(ColorMapsArray);
        if (ObjectHistoryBuffer != null) ObjectHistoryBuffer.Release();

        // >>> 核心修正 1: 禁用 MipChain (false)，防止模糊 <<<
        ColorMapsArray = new Texture2DArray(Screen.width, Screen.height, frameCount, TextureFormat.ARGB32, false);
        // >>> 核心修正 2: 强制 Point 采样，杜绝颜色溢出 <<<
        ColorMapsArray.filterMode = FilterMode.Point;
        ColorMapsArray.wrapMode = TextureWrapMode.Clamp;

        CameraVPMatricesArray = new Matrix4x4[frameCount];

        ObjectTransformData[] allHistoryData = new ObjectTransformData[frameCount * objCount];
        for (int f = 0; f < frameCount; f++)
        {
            var frameSnapshot = SceneHistory.History[f];
            foreach (var objState in frameSnapshot.ObjectStates)
            {
                if (ObjectIdToIndexMap.TryGetValue(objState.Id, out int objIndex))
                {
                    int flatIndex = f * objCount + objIndex;
                    allHistoryData[flatIndex] = new ObjectTransformData
                    {
                        position = objState.Position,
                        padding = 0f,
                        rotation = objState.Rotation
                    };
                }
            }
        }
        ObjectHistoryBuffer = new ComputeBuffer(allHistoryData.Length, Marshal.SizeOf(typeof(ObjectTransformData)));
        ObjectHistoryBuffer.SetData(allHistoryData);

        GameObject workerCamObj = new GameObject("Worker Precomputation Camera");
        Camera workerCam = workerCamObj.AddComponent<Camera>();
        workerCam.CopyFrom(mainCamera);
        workerCam.enabled = false;
        workerCam.clearFlags = CameraClearFlags.SolidColor;
        workerCam.backgroundColor = Color.clear;

        Dictionary<int, Transform> objTransforms = new Dictionary<int, Transform>();
        foreach (var t in trackableObjects) objTransforms[t.UniqueId] = t.transform;

        RenderTexture tempColorRT = RenderTexture.GetTemporary(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);

        try
        {
            for (int i = 0; i < frameCount; i++)
            {
                var frame = SceneHistory.History[i];
                var camState = frame.ObjectStates.Find(s => s.Id == 0);
                if (camState != null)
                {
                    workerCam.transform.position = camState.Position;
                    workerCam.transform.rotation = camState.Rotation;
                }

                foreach (var state in frame.ObjectStates)
                {
                    if (objTransforms.TryGetValue(state.Id, out Transform t))
                    {
                        t.position = state.Position;
                        t.rotation = state.Rotation;
                    }
                }

                // >>> 核心修正 3: 这里的 G-Buffer 必须也是 Point 模式 <<<
                RenderTexture idMap = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.RFloat);
                idMap.filterMode = FilterMode.Point; // <--- 关键！

                RenderTexture posMap = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGBFloat);
                posMap.filterMode = FilterMode.Point; // <--- 关键！

                foreach (var obj in trackableObjects)
                {
                    var renderer = obj.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        propBlock.SetFloat("_ObjectID", obj.UniqueId);
                        renderer.SetPropertyBlock(propBlock);
                    }
                }

                workerCam.SetTargetBuffers(new RenderBuffer[] { idMap.colorBuffer, posMap.colorBuffer }, posMap.depthBuffer);
                workerCam.RenderWithShader(gBufferShader, "RenderType");

                IdMaps.Add(idMap);
                PosMaps.Add(posMap);

                workerCam.targetTexture = tempColorRT;
                workerCam.Render();
                Graphics.CopyTexture(tempColorRT, 0, 0, ColorMapsArray, i, 0);

                // 记录矩阵 (使用 GL 版本以确保兼容性)
                var gpuProj = GL.GetGPUProjectionMatrix(workerCam.projectionMatrix, false);
                CameraVPMatricesArray[i] = gpuProj * workerCam.worldToCameraMatrix;

                if (i % 10 == 0)
                {
                    onProgress?.Invoke($"预计算中... {i + 1} / {frameCount}");
                    yield return null;
                }
            }
        }
        finally
        {
            RenderTexture.ReleaseTemporary(tempColorRT);
            Destroy(workerCamObj);
            onProgress?.Invoke($"预计算完成，共 {frameCount} 帧。");
            onComplete?.Invoke();
        }
    }

    void OnDestroy()
    {
        if (ObjectHistoryBuffer != null) ObjectHistoryBuffer.Release();
    }
}