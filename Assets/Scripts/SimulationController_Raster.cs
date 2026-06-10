// SimulationController_Raster.cs (V5 - Definitive Fix & Complete)
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class SimulationController_Raster : MonoBehaviour
{
    [Header("核心组件引用")]
    public SceneRecorder sceneRecorder;
    public ScenePreparer scenePreparer;
    public TimeWarpRenderer_Raster timeWarpRenderer;

    [Header("UI元素")]
    public Button recordButton;
    public Button precomputeButton;
    public Slider lightSpeedSlider;
    public Text statusText;
    public Text lightSpeedText;

    private enum SimulationState { Idle, Recording, Recorded, Precomputing, ReadyToRender }
    private SimulationState currentState;

    // <<< 核心修正1：缓存所有可追踪物体的Renderer >>>
    private List<Renderer> trackableRenderers;

    void Start()
    {
        recordButton.onClick.AddListener(OnRecordButtonPressed);
        precomputeButton.onClick.AddListener(OnPrecomputeButtonPressed);
        lightSpeedSlider.onValueChanged.AddListener(OnLightSpeedChanged);

        lightSpeedSlider.minValue = 0f;
        lightSpeedSlider.maxValue = 300f;
        lightSpeedSlider.value = timeWarpRenderer.LightSpeed;

        // 初始时，找到并缓存所有需要控制的Renderer
        trackableRenderers = FindObjectsOfType<TrackableObject>()
            .Select(t => t.GetComponent<Renderer>())
            .Where(r => r != null)
            .ToList();

        SetState(SimulationState.Idle);
    }

    private void OnRecordButtonPressed()
    {
        if (currentState == SimulationState.Recording)
        {
            sceneRecorder.StopRecording();
            SetState(SimulationState.Recorded);
        }
        else
        {
            SetState(SimulationState.Recording);
            sceneRecorder.StartRecording();
        }
    }

    private void OnPrecomputeButtonPressed()
    {
        if (currentState == SimulationState.Recorded)
        {
            SetState(SimulationState.Precomputing);
            StartCoroutine(scenePreparer.PrecomputationCoroutine(
                (progressMessage) => { statusText.text = progressMessage; },
                () => { SetState(SimulationState.ReadyToRender); }
            ));
        }
    }

    private void OnLightSpeedChanged(float value)
    {
        timeWarpRenderer.LightSpeed = value;
        lightSpeedText.text = $"光速 (C): {value:F1} m/s";
    }

    private void SetState(SimulationState newState)
    {
        currentState = newState;
        switch (currentState)
        {
            case SimulationState.Idle:
            case SimulationState.Recording:
            case SimulationState.Recorded:
                recordButton.interactable = true;
                precomputeButton.interactable = (newState == SimulationState.Recorded);
                lightSpeedSlider.interactable = false;
                timeWarpRenderer.DisableRendering();
                // 确保所有物体在录制和等待时都可见
                trackableRenderers.ForEach(r => r.enabled = true);
                statusText.text = (newState == SimulationState.Recording) ? "状态: 正在录制..." :
                                  (newState == SimulationState.Recorded) ? $"状态: 录制完成 ({SceneHistory.History.Count} 帧)。" : "状态: 空闲，等待录制";
                recordButton.GetComponentInChildren<Text>().text = (newState == SimulationState.Recording) ? "停止录制" : "开始/重新录制";
                break;

            case SimulationState.Precomputing:
                recordButton.interactable = false;
                precomputeButton.interactable = false;
                statusText.text = "状态: 正在预计算...";
                // 在预计算时，物体必须是可见的，以便Worker Camera渲染
                trackableRenderers.ForEach(r => r.enabled = true);
                timeWarpRenderer.DisableRendering();
                break;

            case SimulationState.ReadyToRender:
                recordButton.interactable = true;
                recordButton.GetComponentInChildren<Text>().text = "重新录制";
                precomputeButton.interactable = false;
                lightSpeedSlider.interactable = true;
                statusText.text = "状态: 渲染中！请调节光速。";

                // <<< 核心修正2：预计算完成后，立即隐藏所有物体，并启用我们的渲染器 >>>
                trackableRenderers.ForEach(r => r.enabled = false);
                timeWarpRenderer.EnableRendering();
                break;
        }
        OnLightSpeedChanged(lightSpeedSlider.value);
    }
}