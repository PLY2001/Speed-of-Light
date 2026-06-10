using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class TimeDilationController : MonoBehaviour
{
    [Header("Debugging")]
    public bool enableShaderDebugging = false;

    [Header("Simulation Parameters")]
    public float speedOfLight = 20.0f;
    [Tooltip("录制时长。注意：网格烘焙会消耗内存，建议保持在 3-5秒内。")]
    public float recordingDuration = 4.0f;
    public int frameRate = 30;
    public Vector2Int renderResolution = new Vector2Int(480, 270);

    [Header("Scene References")]
    public Camera sceneCamera;
    public RawImage playbackDisplay;
    public Shader depthShader;
    public Text progressText;
    public ComputeShader compositionShader;

    // --- 数据结构 ---
    private class GeometrySnapshot
    {
        public Mesh meshReference;  // 网格引用
        public bool isInstanceMesh; // 标记是否是运行时实例（需要Destroy）
        public Matrix4x4 localToWorld;
        public bool isActive;
    }

    private class ObjectTrack
    {
        // 支持两种渲染器类型
        public SkinnedMeshRenderer originalSMR;
        public MeshRenderer originalMR;
        public MeshFilter originalMF;

        public GameObject proxyGO;
        public MeshFilter proxyMF;
        public MeshRenderer proxyMR;

        public Dictionary<int, GeometrySnapshot> snapshots = new Dictionary<int, GeometrySnapshot>();
    }

    private class RecordingSession
    {
        public Dictionary<int, ObjectTrack> tracks = new Dictionary<int, ObjectTrack>();
        public Dictionary<int, Matrix4x4> cameraTrack = new Dictionary<int, Matrix4x4>();
        public List<Texture2D> finalFrames = new List<Texture2D>();
    }

    private RecordingSession _session;
    private Camera _offscreenCam;
    private Coroutine _simulationCoroutine;

    public void StartSimulation()
    {
        if (_simulationCoroutine != null) StopCoroutine(_simulationCoroutine);
        _simulationCoroutine = StartCoroutine(RunSimulationFlow());
    }

    private IEnumerator RunSimulationFlow()
    {
        // 0. 初始清理与重置
        Time.timeScale = 1.0f; // 确保开始前时间是流动的
        _session = new RecordingSession();
        SetupOffscreenCamera();
        IdentifyDynamicObjects();

        // 1. 录制 (Baking)
        UpdateProgress("Phase 1/3: Baking geometry...");
        // 这一步现在包含了【双重锁定】，既保证物理准确，又保证操作时间
        yield return StartCoroutine(RecordPhase());
        UpdateProgress("Baking Complete.");

        // 【关键修复】冻结世界
        // 录制一结束，立即停止所有 Update 和物理模拟
        // 防止在合成阶段真身继续移动干扰渲染
        Time.timeScale = 0f;

        // 2. 准备合成环境
        SetupProxies(true);

        // 3. 合成
        UpdateProgress("Phase 2/3: Composing...");
        yield return StartCoroutine(ComposePhase());

        // 4. 清理与回放
        SetupProxies(false);
        UpdateProgress("Phase 3/3: Playback.");

        // 此时依然保持 timeScale = 0，仅让 UI 播放视频
        PlayPhase();
    }

    // --- 步骤 1: 识别物体 (升级版) ---
    private void IdentifyDynamicObjects()
    {
        _session.tracks.Clear();

        // 1. 查找 SkinnedMeshRenderer (带动画的角色)
        var smrs = FindObjectsOfType<SkinnedMeshRenderer>();
        foreach (var smr in smrs)
        {
            if (smr.gameObject.layer == LayerMask.NameToLayer("Dynamic"))
            {
                int id = smr.gameObject.GetInstanceID();
                _session.tracks[id] = new ObjectTrack { originalSMR = smr };
            }
        }

        // 2. 【新增】查找 MeshRenderer (普通物体，如 Cube)
        var mrs = FindObjectsOfType<MeshRenderer>();
        foreach (var mr in mrs)
        {
            if (mr.gameObject.layer == LayerMask.NameToLayer("Dynamic"))
            {
                int id = mr.gameObject.GetInstanceID();
                var mf = mr.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    _session.tracks[id] = new ObjectTrack { originalMR = mr, originalMF = mf };
                }
            }
        }
    }

    // --- 步骤 2: 录制 (双重锁定版) ---
    private IEnumerator RecordPhase()
    {
        int totalFrames = (int)(recordingDuration * frameRate);
        float timeStep = 1.0f / frameRate;

        // --- 环境快照与锁定 ---
        float originalCaptureDeltaTime = Time.captureDeltaTime;
        int originalTargetFrameRate = Application.targetFrameRate;
        int originalVSync = QualitySettings.vSyncCount;

        // 【锁定 1】物理步长：确保物理模拟严格按照 1/30秒 步进，消除“逐渐加速”Bug
        Time.captureDeltaTime = timeStep;

        // 【锁定 2】渲染帧率：强制 Unity 等待现实时间，确保录制不会瞬间完成，让你有时间操作
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = frameRate;

        try
        {
            for (int i = 0; i < totalFrames; i++)
            {
                // 等待帧结束，此时 Unity 会自动计算物理并休眠以匹配 targetFrameRate
                yield return null;
                yield return new WaitForEndOfFrame();

                // 记录相机
                _session.cameraTrack[i] = sceneCamera.transform.localToWorldMatrix;

                // 记录所有物体
                foreach (var kvp in _session.tracks)
                {
                    var track = kvp.Value;
                    Mesh currentMesh = null;
                    bool isInstance = false;
                    Transform tr = null;
                    bool active = false;

                    // 分类处理 SMR 和 MR
                    if (track.originalSMR != null)
                    {
                        if (track.originalSMR.isVisible)
                        {
                            currentMesh = new Mesh();
                            track.originalSMR.BakeMesh(currentMesh); // SMR 必须 Bake
                            isInstance = true; // 这是一个新实例，以后要销毁
                            tr = track.originalSMR.transform;
                            active = track.originalSMR.gameObject.activeInHierarchy;
                        }
                    }
                    else if (track.originalMR != null)
                    {
                        if (track.originalMR.isVisible)
                        {
                            currentMesh = track.originalMF.sharedMesh; // 普通物体直接引用，不用 Bake
                            isInstance = false; // 这是资源引用，千万别销毁
                            tr = track.originalMR.transform;
                            active = track.originalMR.gameObject.activeInHierarchy;
                        }
                    }

                    if (tr != null && currentMesh != null)
                    {
                        var snapshot = new GeometrySnapshot
                        {
                            meshReference = currentMesh,
                            isInstanceMesh = isInstance,
                            localToWorld = tr.localToWorldMatrix,
                            isActive = active
                        };
                        track.snapshots[i] = snapshot;
                    }
                }

                if (i % 10 == 0) UpdateProgress($"Recording: {i}/{totalFrames}");
            }
        }
        finally
        {
            // 还原设置，防止编辑器卡顿
            Time.captureDeltaTime = originalCaptureDeltaTime;
            Application.targetFrameRate = originalTargetFrameRate;
            QualitySettings.vSyncCount = originalVSync;
        }
    }

    // --- 步骤 3: 替身系统 (已修复光照问题) ---
    private void SetupProxies(bool enableProxies)
    {
        foreach (var kvp in _session.tracks)
        {
            var track = kvp.Value;

            // 获取材质引用
            Material[] mats = null;
            // 【修改点】新增一个变量来暂存原始渲染器，以便后续复制光照设置
            Renderer originalRenderer = null;

            if (track.originalSMR != null)
            {
                mats = track.originalSMR.sharedMaterials;
                originalRenderer = track.originalSMR;
            }
            else if (track.originalMR != null)
            {
                mats = track.originalMR.sharedMaterials;
                originalRenderer = track.originalMR;
            }

            if (mats == null) continue;

            if (enableProxies)
            {
                // 创建替身
                GameObject proxy = new GameObject($"Proxy_{kvp.Key}");
                proxy.layer = LayerMask.NameToLayer("Dynamic");

                var mf = proxy.AddComponent<MeshFilter>();
                var mr = proxy.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;

                // -----------------------------------------------------------
                // 【修复核心】复制光照探针和反射设置
                // -----------------------------------------------------------
                if (originalRenderer != null)
                {
                    // 开启光照探针混合
                    mr.lightProbeUsage = originalRenderer.lightProbeUsage;
                    // 开启反射探针混合
                    mr.reflectionProbeUsage = originalRenderer.reflectionProbeUsage;
                    // 保持阴影投射模式一致
                    mr.shadowCastingMode = originalRenderer.shadowCastingMode;
                    mr.receiveShadows = originalRenderer.receiveShadows;
                    // 关键：复制探针锚点，确保替身在与真身相同的位置采样光照
                    mr.probeAnchor = originalRenderer.probeAnchor;
                }
                // -----------------------------------------------------------

                track.proxyGO = proxy;
                track.proxyMF = mf;
                track.proxyMR = mr;

                // 【关键】隐藏真身，防止它们被相机拍到
                if (track.originalSMR != null) track.originalSMR.enabled = false;
                if (track.originalMR != null) track.originalMR.enabled = false;
            }
            else
            {
                // 销毁替身
                if (track.proxyGO != null) Destroy(track.proxyGO);

                // 恢复真身
                if (track.originalSMR != null) track.originalSMR.enabled = true;
                if (track.originalMR != null) track.originalMR.enabled = true;

                // 清理内存：只销毁 Bake 出来的实例 Mesh
                foreach (var frame in track.snapshots.Values)
                {
                    if (frame.isInstanceMesh && frame.meshReference != null)
                        Destroy(frame.meshReference);
                }
                track.snapshots.Clear();
            }
        }
    }

    // --- 步骤 4: 合成 (保持逻辑不变，只做微调) ---
    private IEnumerator ComposePhase()
    {
        int totalFrames = (int)(recordingDuration * frameRate);
        float timeStep = 1.0f / frameRate;

        // Shader Init
        int kernel = compositionShader.FindKernel("CSMain");
        uint tx, ty, tz;
        compositionShader.GetKernelThreadGroupSizes(kernel, out tx, out ty, out tz);
        int tgX = Mathf.CeilToInt((float)renderResolution.x / tx);
        int tgY = Mathf.CeilToInt((float)renderResolution.y / ty);

        RenderTexture rtColorA = CreateRT(false); RenderTexture rtDepthA = CreateDepthRT();
        RenderTexture rtColorB = CreateRT(false); RenderTexture rtDepthB = CreateDepthRT();
        RenderTexture staticColor = CreateRT(false); RenderTexture staticDepth = CreateDepthRT();

        void SetProxiesToTime(int frameIndex)
        {
            foreach (var kvp in _session.tracks)
            {
                var track = kvp.Value;
                if (track.proxyGO == null) continue;

                if (track.snapshots.TryGetValue(frameIndex, out var snapshot))
                {
                    track.proxyGO.SetActive(snapshot.isActive);
                    if (snapshot.isActive)
                    {
                        track.proxyMF.mesh = snapshot.meshReference; // Assign Mesh
                        track.proxyGO.transform.position = snapshot.localToWorld.GetColumn(3);
                        track.proxyGO.transform.rotation = snapshot.localToWorld.rotation;
                        track.proxyGO.transform.localScale = snapshot.localToWorld.lossyScale;
                    }
                }
                else
                {
                    track.proxyGO.SetActive(false);
                }
            }
        }

        // 在 TimeDilationController.cs 中
        void RenderScene(int frameIndex, RenderTexture targetColor, RenderTexture targetDepth)
        {
            SetProxiesToTime(frameIndex);

            // 此时 Time.timeScale 为 0, 真身不会动, 只有 Proxy 被我们强行瞬移
            _offscreenCam.targetTexture = targetColor;

            // --- 修改开始 ---
            // [原有逻辑] 只渲染 Dynamic，导致静态物体无法在回溯阶段接收阴影
            // _offscreenCam.cullingMask = 1 << LayerMask.NameToLayer("Dynamic");

            // [新逻辑] 同时渲染 Dynamic 和 Static
            // 这样静态物体会参与光速计算，并且能接住动态物体的阴影
            int dynamicMask = 1 << LayerMask.NameToLayer("Dynamic");
            int staticMask = 1 << LayerMask.NameToLayer("Static");
            _offscreenCam.cullingMask = dynamicMask | staticMask;
            // --- 修改结束 ---

            _offscreenCam.Render();

            _offscreenCam.targetTexture = targetDepth;
            _offscreenCam.RenderWithShader(depthShader, "RenderType");
        }

        for (int ti_idx = 0; ti_idx < totalFrames; ti_idx++)
        {
            float ti = ti_idx * timeStep;
            RenderTexture finalFrameRT = CreateRT(true);
            ComputeBuffer finalDepthBuffer = new ComputeBuffer(renderResolution.x * renderResolution.y, sizeof(int));

            // 渲染静态背景
            if (_session.cameraTrack.ContainsKey(ti_idx))
            {
                Matrix4x4 camMat = _session.cameraTrack[ti_idx];
                _offscreenCam.transform.position = camMat.GetColumn(3);
                _offscreenCam.transform.rotation = camMat.rotation;
            }

            _offscreenCam.targetTexture = staticColor;
            _offscreenCam.cullingMask = 1 << LayerMask.NameToLayer("Static");
            _offscreenCam.Render();
            _offscreenCam.targetTexture = staticDepth;
            _offscreenCam.backgroundColor = Color.white;
            _offscreenCam.RenderWithShader(depthShader, "RenderType");

            Graphics.Blit(staticColor, finalFrameRT);

            // 读取静态深度
            var reqStatic = AsyncGPUReadback.Request(staticDepth);
            yield return new WaitUntil(() => reqStatic.done);
            var rawDepth = reqStatic.GetData<float>();
            int[] depthInts = new int[rawDepth.Length];
            for (int k = 0; k < depthInts.Length; k++) depthInts[k] = System.BitConverter.ToInt32(System.BitConverter.GetBytes(rawDepth[k]), 0);
            finalDepthBuffer.SetData(depthInts);
            rawDepth.Dispose();

            // 动态回溯
            RenderScene(ti_idx, rtColorA, rtDepthA);

            for (int tb_idx = ti_idx; tb_idx > 0; tb_idx--)
            {
                RenderScene(tb_idx - 1, rtColorB, rtDepthB);

                float startTime = (tb_idx - 1) * timeStep;

                compositionShader.SetTexture(kernel, "ColorStart", rtColorB);
                compositionShader.SetTexture(kernel, "DepthStart", rtDepthB);
                compositionShader.SetTexture(kernel, "ColorEnd", rtColorA);
                compositionShader.SetTexture(kernel, "DepthEnd", rtDepthA);

                compositionShader.SetFloat("EmissionTimeStart", startTime);
                compositionShader.SetFloat("TimeStep", timeStep);
                compositionShader.SetFloat("ImpactTime", ti);
                compositionShader.SetFloat("SpeedOfLight", speedOfLight);
                compositionShader.SetFloat("FarPlane", sceneCamera.farClipPlane);
                compositionShader.SetInt("BufferWidth", renderResolution.x);

                compositionShader.SetTexture(kernel, "FinalFrame", finalFrameRT);
                compositionShader.SetBuffer(kernel, "FinalDepthBuffer", finalDepthBuffer);

                compositionShader.Dispatch(kernel, tgX, tgY, 1);

                // Swap Ping-Pong
                var tC = rtColorA; rtColorA = rtColorB; rtColorB = tC;
                var tD = rtDepthA; rtDepthA = rtDepthB; rtDepthB = tD;
            }

            // 保存结果
            var reqFinal = AsyncGPUReadback.Request(finalFrameRT);
            yield return new WaitUntil(() => reqFinal.done);
            if (!reqFinal.hasError)
            {
                var data = reqFinal.GetData<Color32>();
                Texture2D tex = new Texture2D(renderResolution.x, renderResolution.y, TextureFormat.RGBA32, false);
                tex.LoadRawTextureData(data);
                tex.Apply();
                _session.finalFrames.Add(tex);
                data.Dispose();
            }

            finalFrameRT.Release();
            finalDepthBuffer.Dispose();

            UpdateProgress($"Composing {ti_idx}/{totalFrames}");
            yield return null;
        }

        rtColorA.Release(); rtDepthA.Release(); rtColorB.Release(); rtDepthB.Release();
        staticColor.Release(); staticDepth.Release();
        _offscreenCam.targetTexture = null;
    }

    // --- 辅助方法 ---
    private RenderTexture CreateRT(bool enableRandomWrite)
    {
        var rt = new RenderTexture(renderResolution.x, renderResolution.y, 24, RenderTextureFormat.ARGB32);
        rt.enableRandomWrite = enableRandomWrite;
        // 建议开启自动SRGB转换，以防在Linear空间下颜色变暗
        // rt.sRGB = true; 
        rt.Create();
        return rt;
    }

    private RenderTexture CreateDepthRT()
    {
        var rt = new RenderTexture(renderResolution.x, renderResolution.y, 24, RenderTextureFormat.RFloat);
        rt.Create();
        return rt;
    }

    private void SetupOffscreenCamera()
    {
        if (_offscreenCam != null) Destroy(_offscreenCam.gameObject);
        GameObject camObj = new GameObject("OffscreenCamera");
        _offscreenCam = camObj.AddComponent<Camera>();
        _offscreenCam.CopyFrom(sceneCamera);
        _offscreenCam.clearFlags = CameraClearFlags.Skybox;
        _offscreenCam.enabled = false;
    }

    private int _playbackFrameIndex = 0;
    private void PlayPhase()
    {
        if (_session.finalFrames.Count == 0) return;
        playbackDisplay.gameObject.SetActive(true);
        // 因为时间被冻结了，我们不能用 InvokeRepeating，改用协程
        StartCoroutine(PlaybackRoutine());
    }

    private IEnumerator PlaybackRoutine()
    {
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(1.0f / frameRate);
        while (true)
        {
            playbackDisplay.texture = _session.finalFrames[_playbackFrameIndex];
            UpdateProgress($"Playing {_playbackFrameIndex}");
            _playbackFrameIndex = (_playbackFrameIndex + 1) % _session.finalFrames.Count;
            yield return wait;
        }
    }

    private void UpdateProgress(string message)
    {
        if (progressText != null) progressText.text = message;
    }
}