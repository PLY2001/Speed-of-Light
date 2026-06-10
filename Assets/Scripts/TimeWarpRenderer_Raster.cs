using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections.Generic;

[RequireComponent(typeof(Camera))]
public class TimeWarpRenderer_Raster : MonoBehaviour
{
    public Shader compositionShader;
    private Material compositionMaterial;
    private ScenePreparer scenePreparer;

    [Header("物理参数")]
    public float LightSpeed = 20.0f;
    public float MaxDistance = 1000f;

    private float renderTime = 0f;
    private ComputeBuffer cameraMatricesBuffer;
    private ComputeBuffer idToIndexMapBuffer;
    private bool isReadyToRender = false;

    public void EnableRendering() { renderTime = 0f; isReadyToRender = true; }
    public void DisableRendering() { isReadyToRender = false; }

    void Start()
    {
        if (compositionShader == null) return;
        compositionMaterial = new Material(compositionShader);
        scenePreparer = FindObjectOfType<ScenePreparer>();
    }

    void Update()
    {
        if (isReadyToRender)
        {
            renderTime += Time.deltaTime;
            // 循环播放逻辑
            if (SceneHistory.MaxTime > 0 && renderTime > SceneHistory.MaxTime)
            {
                renderTime %= SceneHistory.MaxTime;
            }
        }
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (!isReadyToRender || scenePreparer == null || scenePreparer.ColorMapsArray == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        // 1. 懒加载相机矩阵 Buffer
        if (cameraMatricesBuffer == null)
        {
            cameraMatricesBuffer = new ComputeBuffer(scenePreparer.CameraVPMatricesArray.Length, Marshal.SizeOf(typeof(Matrix4x4)));
            cameraMatricesBuffer.SetData(scenePreparer.CameraVPMatricesArray);
        }

        // 2. 懒加载 ID 映射 Buffer
        if (idToIndexMapBuffer == null)
        {
            int maxId = 0;
            foreach (var key in scenePreparer.ObjectIdToIndexMap.Keys) if (key > maxId) maxId = key;
            int[] mapData = new int[maxId + 1];
            for (int i = 0; i < mapData.Length; i++) mapData[i] = -1;
            foreach (var kvp in scenePreparer.ObjectIdToIndexMap) mapData[kvp.Key] = kvp.Value;

            idToIndexMapBuffer = new ComputeBuffer(mapData.Length, sizeof(int));
            idToIndexMapBuffer.SetData(mapData);
            compositionMaterial.SetBuffer("_IdToIndexMap", idToIndexMapBuffer);
        }

        // 3. 设置 Shader 参数
        compositionMaterial.SetTexture("_SourceTex", source);

        int currentFrame = Mathf.FloorToInt(renderTime / Time.fixedDeltaTime);
        currentFrame = Mathf.Clamp(currentFrame, 0, scenePreparer.ColorMapsArray.depth - 1);

        compositionMaterial.SetTexture("_IdMapCurrent", scenePreparer.IdMaps[currentFrame]);
        compositionMaterial.SetTexture("_PosMapCurrent", scenePreparer.PosMaps[currentFrame]);

        compositionMaterial.SetTexture("_ColorMapsArray", scenePreparer.ColorMapsArray);

        // >>> 修改 1: 传递 PosMapsArray <<<
        compositionMaterial.SetTexture("_PosMapsArray", scenePreparer.PosMapsArray);

        compositionMaterial.SetBuffer("_CameraVPMatricesBuffer", cameraMatricesBuffer);
        compositionMaterial.SetBuffer("_ObjectHistoryBuffer", scenePreparer.ObjectHistoryBuffer);
        compositionMaterial.SetInt("_MaxTrackableCount", scenePreparer.MaxTrackableCount);

        compositionMaterial.SetFloat("_LightSpeed", LightSpeed);
        compositionMaterial.SetFloat("_CurrentTime", renderTime);
        compositionMaterial.SetFloat("_FrameInterval", Time.fixedDeltaTime);
        compositionMaterial.SetInt("_FrameCount", scenePreparer.ColorMapsArray.depth);
        compositionMaterial.SetFloat("_MaxDistance", MaxDistance);

        var camState = SceneHistory.GetObjectStateAtTime(0, renderTime);
        if (camState != null) compositionMaterial.SetVector("_CameraPosCurrent", camState.Position);

        Graphics.Blit(source, destination, compositionMaterial);
    }

    void OnDestroy()
    {
        if (cameraMatricesBuffer != null) cameraMatricesBuffer.Release();
        if (idToIndexMapBuffer != null) idToIndexMapBuffer.Release();
    }
}