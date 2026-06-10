using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class TimeDilationController : MonoBehaviour
{
    [Header("Debugging")]
    [Tooltip("启用调试模式，将输出中间计算值而不是最终图像。")]
    public bool enableShaderDebugging = false;

    // ... (All the fields and other methods are exactly the same as before) ...
    [Header("Simulation Parameters")]
    [Tooltip("模拟光速 (米/秒)。值越小，相对论效应越明显。")]
    public float speedOfLight = 20.0f;
    [Tooltip("录制和模拟的总时长（秒）。")]
    public float recordingDuration = 5.0f;
    [Tooltip("模拟的帧率。")]
    public int frameRate = 30;
    [Tooltip("最终输出视频的分辨率。")]
    public Vector2Int renderResolution = new Vector2Int(480, 270);

    [Header("Scene & UI References")]
    [Tooltip("场景中用于观察的主相机。")]
    public Camera sceneCamera;
    [Tooltip("用于播放最终结果的UI RawImage。")]
    public RawImage playbackDisplay;
    [Tooltip("用于渲染深度的自定义着色器。")]
    public Shader depthShader;
    [Tooltip("显示当前进度的UI Text。")]
    public Text progressText;

    [Header("Compute Resources")]
    [Tooltip("用于合成的Compute Shader。")]
    public ComputeShader compositionShader;

    private struct TransformSnapshot { public Vector3 position; public Quaternion rotation; }
    private class RecordingSession
    {
        public Dictionary<int, Dictionary<int, TransformSnapshot>> dynamicObjectTracks = new Dictionary<int, Dictionary<int, TransformSnapshot>>();
        public Dictionary<int, TransformSnapshot> cameraTrack = new Dictionary<int, TransformSnapshot>();
        public List<Texture2D> finalFrames = new List<Texture2D>();
    }

    private RecordingSession _session;
    private Camera _offscreenCam;
    private Coroutine _simulationCoroutine;

    // ... (StartSimulation, RunSimulationFlow, RecordPhase are unchanged) ...
    public void StartSimulation()
    {
        if (_simulationCoroutine != null)
        {
            StopCoroutine(_simulationCoroutine);
        }
        _simulationCoroutine = StartCoroutine(RunSimulationFlow());
    }

    private IEnumerator RunSimulationFlow()
    {
        _session = new RecordingSession();
        SetupOffscreenCamera();

        UpdateProgress("Phase 1/3: Recording scene motion...");
        yield return StartCoroutine(RecordPhase());
        UpdateProgress("Recording Complete.");

        UpdateProgress("Phase 2/3: Composing final frames... (This will take a long time)");
        yield return StartCoroutine(ComposePhase());
        UpdateProgress("Composition Complete.");

        UpdateProgress("Phase 3/3: Starting playback.");
        PlayPhase();
    }
    private IEnumerator RecordPhase()
    {
        var recordableObjects = FindObjectsOfType<Recordable>();
        int totalFrames = (int)(recordingDuration * frameRate);
        float timeStep = 1.0f / frameRate;

        for (int i = 0; i < totalFrames; i++)
        {
            _session.cameraTrack[i] = new TransformSnapshot { position = sceneCamera.transform.position, rotation = sceneCamera.transform.rotation };

            foreach (var obj in recordableObjects)
            {
                int id = obj.gameObject.GetInstanceID();
                if (!_session.dynamicObjectTracks.ContainsKey(id))
                    _session.dynamicObjectTracks[id] = new Dictionary<int, TransformSnapshot>();
                _session.dynamicObjectTracks[id][i] = new TransformSnapshot { position = obj.transform.position, rotation = obj.transform.rotation };
            }
            yield return new WaitForSeconds(timeStep);
        }
    }

    // ================================================================================
    // Phase 2: Composition (Analytical Intersection / Anti-Ghosting Version)
    // ================================================================================

    private IEnumerator ComposePhase()
    {
        int totalFrames = (int)(recordingDuration * frameRate);
        float timeStep = 1.0f / frameRate;

        int kernel = compositionShader.FindKernel("CSMain");

        uint threadGroupSizeX, threadGroupSizeY, threadGroupSizeZ;
        compositionShader.GetKernelThreadGroupSizes(kernel, out threadGroupSizeX, out threadGroupSizeY, out threadGroupSizeZ);
        int threadGroupsX = Mathf.CeilToInt((float)renderResolution.x / threadGroupSizeX);
        int threadGroupsY = Mathf.CeilToInt((float)renderResolution.y / threadGroupSizeY);

        int dynamicLayerMask = 1 << LayerMask.NameToLayer("Dynamic");
        int staticLayerMask = 1 << LayerMask.NameToLayer("Static");

        // 1. 准备 Ping-Pong 双缓冲纹理
        // Set A ("Current/End" state)
        RenderTexture rtColorA = new RenderTexture(renderResolution.x, renderResolution.y, 24, RenderTextureFormat.ARGB32);
        RenderTexture rtDepthA = new RenderTexture(renderResolution.x, renderResolution.y, 24, RenderTextureFormat.RFloat);
        // Set B ("Previous/Start" state)
        RenderTexture rtColorB = new RenderTexture(renderResolution.x, renderResolution.y, 24, RenderTextureFormat.ARGB32);
        RenderTexture rtDepthB = new RenderTexture(renderResolution.x, renderResolution.y, 24, RenderTextureFormat.RFloat);

        rtColorA.Create(); rtDepthA.Create();
        rtColorB.Create(); rtDepthB.Create();

        // 临时辅助纹理（用于静态背景读取）
        RenderTexture staticBaseColorRT = new RenderTexture(renderResolution.x, renderResolution.y, 24, RenderTextureFormat.ARGB32);
        RenderTexture staticBaseDepthRT = new RenderTexture(renderResolution.x, renderResolution.y, 24, RenderTextureFormat.RFloat);

        // ---------------------------------------------------------
        // 本地辅助函数：渲染指定时刻的动态物体到目标纹理
        // ---------------------------------------------------------
        void RenderDynamicScene(int frameIndex, RenderTexture targetColor, RenderTexture targetDepth)
        {
            // 使用整数索引即可，因为 Shader 会负责两帧之间的平滑插值
            SetSceneToTime(frameIndex);

            _offscreenCam.cullingMask = dynamicLayerMask;
            _offscreenCam.targetTexture = targetColor;
            _offscreenCam.Render();
            _offscreenCam.targetTexture = targetDepth;
            _offscreenCam.RenderWithShader(depthShader, "RenderType");
        }

        // ======================== 主循环：生成每一帧 ========================
        for (int ti_idx = 0; ti_idx < totalFrames; ti_idx++)
        {
            float ti = ti_idx * timeStep; // 观察者当前时刻 (Impact Time)

            // 2. 初始化最终输出纹理
            RenderTexture finalFrameRT = new RenderTexture(renderResolution.x, renderResolution.y, 24, RenderTextureFormat.ARGB32);
            finalFrameRT.enableRandomWrite = true;
            finalFrameRT.Create();

            ComputeBuffer finalDepthBuffer = new ComputeBuffer(renderResolution.x * renderResolution.y, sizeof(int));

            // 3. 渲染静态背景 (这部分逻辑保持不变，确保背景遮挡正确)
            // -------------------------------------------------------
            SetObjectTransform(sceneCamera.gameObject, _session.cameraTrack[ti_idx]);
            _offscreenCam.transform.position = sceneCamera.transform.position;
            _offscreenCam.transform.rotation = sceneCamera.transform.rotation;

            _offscreenCam.cullingMask = staticLayerMask;
            _offscreenCam.targetTexture = staticBaseColorRT;
            _offscreenCam.Render();
            _offscreenCam.targetTexture = staticBaseDepthRT;
            // 显式清除背景为白色(最远)，防止天空盒残留问题
            _offscreenCam.backgroundColor = Color.white;
            _offscreenCam.RenderWithShader(depthShader, "RenderType");

            Graphics.Blit(staticBaseColorRT, finalFrameRT);

            // CPU Readback for Atomic Min initialization (性能瓶颈点，但逻辑必须)
            var requestStaticDepth = AsyncGPUReadback.Request(staticBaseDepthRT);
            yield return new WaitUntil(() => requestStaticDepth.done);
            var staticDepths = requestStaticDepth.GetData<float>();
            int[] initialDepths = new int[renderResolution.x * renderResolution.y];
            for (int i = 0; i < initialDepths.Length; i++)
            {
                // 注意：现在使用的是 DepthShader 修正后的 0-1 线性深度
                initialDepths[i] = System.BitConverter.ToInt32(System.BitConverter.GetBytes(staticDepths[i]), 0);
            }
            finalDepthBuffer.SetData(initialDepths);
            staticDepths.Dispose();
            // -------------------------------------------------------


            // 4. 历史回溯 (History Trace Loop)
            // -------------------------------------------------------

            // 【预热】：先渲染当前时刻 (ti) 到缓冲组 A
            // 这将作为第一次迭代的 "End" 状态
            RenderDynamicScene(ti_idx, rtColorA, rtDepthA);

            // 从当前帧向回遍历到第1帧
            // 区间是 [tb_idx - 1, tb_idx]
            for (int tb_idx = ti_idx; tb_idx > 0; tb_idx--)
            {
                // A 组目前持有 tb_idx 的数据 (End State)
                // 我们需要渲染 tb_idx - 1 的数据到 B 组 (Start State)
                RenderDynamicScene(tb_idx - 1, rtColorB, rtDepthB);

                // 计算该区间的起始时间
                float startTime = (tb_idx - 1) * timeStep;

                // 设置 Shader 参数
                compositionShader.SetTexture(kernel, "ColorStart", rtColorB); // Start = Past
                compositionShader.SetTexture(kernel, "DepthStart", rtDepthB);
                compositionShader.SetTexture(kernel, "ColorEnd", rtColorA);   // End = Future (relative to start)
                compositionShader.SetTexture(kernel, "DepthEnd", rtDepthA);

                compositionShader.SetFloat("EmissionTimeStart", startTime);
                compositionShader.SetFloat("TimeStep", timeStep); // 这里的 Step 就是一帧的长度

                compositionShader.SetFloat("ImpactTime", ti);
                compositionShader.SetFloat("SpeedOfLight", speedOfLight);
                compositionShader.SetFloat("FarPlane", sceneCamera.farClipPlane);
                compositionShader.SetInt("BufferWidth", renderResolution.x);

                // 输出目标
                compositionShader.SetTexture(kernel, "FinalFrame", finalFrameRT);
                compositionShader.SetBuffer(kernel, "FinalDepthBuffer", finalDepthBuffer);

                // 执行计算
                compositionShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

                // 【Ping-Pong 交换】
                // 下一次循环，tb_idx 会减 1。
                // 我们需要的 "End" 状态正是现在的 "Start" (tb_idx - 1)。
                // 所以，我们将 B 组变成 A 组，下一轮循环 B 组就可以被复写用于存更早的一帧。
                // 这样就避免了 Graphics.Blit 的开销。
                var tempC = rtColorA; rtColorA = rtColorB; rtColorB = tempC;
                var tempD = rtDepthA; rtDepthA = rtDepthB; rtDepthB = tempD;
            }

            // 5. 保存结果
            var request = AsyncGPUReadback.Request(finalFrameRT);
            yield return new WaitUntil(() => request.done);

            if (!request.hasError)
            {
                var data = request.GetData<Color32>();
                Texture2D finalFrameTex = new Texture2D(renderResolution.x, renderResolution.y, TextureFormat.RGBA32, false);
                finalFrameTex.LoadRawTextureData(data);
                finalFrameTex.Apply();
                _session.finalFrames.Add(finalFrameTex);
                data.Dispose();
            }

            finalFrameRT.Release();
            finalDepthBuffer.Dispose();

            UpdateProgress($"Composing frame {ti_idx + 1} / {totalFrames}");
            yield return null;
        }

        // 恢复相机设置
        _offscreenCam.cullingMask = sceneCamera.cullingMask;
        _offscreenCam.targetTexture = null;

        // 清理资源
        rtColorA.Release(); rtDepthA.Release();
        rtColorB.Release(); rtDepthB.Release();
        staticBaseColorRT.Release(); staticBaseDepthRT.Release();
    }

    // ... (The rest of the script: PlayPhase, Helper Methods, etc. are unchanged) ...
    private int _playbackFrameIndex = 0;
    private void PlayPhase()
    {
        if (_session.finalFrames.Count == 0)
        {
            UpdateProgress("Error: No frames were composed.");
            return;
        }
        playbackDisplay.gameObject.SetActive(true);
        InvokeRepeating(nameof(UpdatePlaybackFrame), 0, 1.0f / frameRate);
    }

    private void UpdatePlaybackFrame()
    {
        playbackDisplay.texture = _session.finalFrames[_playbackFrameIndex];
        UpdateProgress($"{_playbackFrameIndex}/{_session.finalFrames.Count}");
        _playbackFrameIndex = (_playbackFrameIndex + 1) % _session.finalFrames.Count;
    }
    private void SetSceneToTime(int timeIndex)
    {
        foreach (var track in _session.dynamicObjectTracks)
        {
            GameObject obj = FindObjectFromInstanceID(track.Key);
            if (obj != null && track.Value.ContainsKey(timeIndex))
            {
                SetObjectTransform(obj, track.Value[timeIndex]);
            }
        }
    }

    private void SetObjectTransform(GameObject obj, TransformSnapshot snapshot)
    {
        obj.transform.position = snapshot.position;
        obj.transform.rotation = snapshot.rotation;
    }

    private void SetupOffscreenCamera()
    {
        GameObject camObj = new GameObject("OffscreenCamera");
        _offscreenCam = camObj.AddComponent<Camera>();
        _offscreenCam.CopyFrom(sceneCamera);

        // ============================ 确保相机清除背景 ============================
        // 如果你的主相机使用天空盒，这里也应该使用Skybox
        _offscreenCam.clearFlags = CameraClearFlags.Skybox;
        // 如果你只想要一个纯色背景，可以用这个
        // _offscreenCam.clearFlags = CameraClearFlags.SolidColor;
        // _offscreenCam.backgroundColor = Color.black; // 或者其他颜色
        // ========================================================================

        _offscreenCam.enabled = false;
    }

    private Dictionary<int, GameObject> _instanceIdCache = new Dictionary<int, GameObject>();
    private GameObject FindObjectFromInstanceID(int id)
    {
        if (_instanceIdCache.ContainsKey(id)) return _instanceIdCache[id];
        var recordables = FindObjectsOfType<Recordable>(true);
        foreach (var r in recordables)
        {
            if (r.gameObject.GetInstanceID() == id)
            {
                _instanceIdCache[id] = r.gameObject;
                return r.gameObject;
            }
        }
        return null;
    }

    private void UpdateProgress(string message)
    {
        if (progressText != null)
        {
            progressText.text = message;
        }
        //Debug.Log(message);
    }
}