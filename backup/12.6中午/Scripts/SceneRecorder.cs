// SceneRecorder.cs
using UnityEngine;

public class SceneRecorder : MonoBehaviour
{
    public bool IsRecording { get; private set; } = false;
    private float recordingTime = 0f;
    private TrackableObject[] trackableObjects;

    public void StartRecording()
    {
        // 初始化历史记录
        SceneHistory.Initialize(Time.fixedDeltaTime);
        trackableObjects = FindObjectsOfType<TrackableObject>();
        recordingTime = 0f;
        IsRecording = true;
        Debug.Log("开始录制...");
    }

    public void StopRecording()
    {
        IsRecording = false;
        Debug.Log($"录制结束。总时长: {recordingTime} 秒, 总帧数: {SceneHistory.History.Count}");
    }

    // 使用FixedUpdate保证采样时间间隔稳定
    void FixedUpdate()
    {
        if (!IsRecording) return;

        FrameSnapshot currentFrame = new FrameSnapshot { Time = recordingTime };

        foreach (var obj in trackableObjects)
        {
            ObjectSnapshot snapshot = new ObjectSnapshot
            {
                Id = obj.UniqueId,
                Position = obj.transform.position,
                Rotation = obj.transform.rotation
            };
            currentFrame.ObjectStates.Add(snapshot);
        }

        SceneHistory.AddFrame(currentFrame);
        recordingTime += Time.fixedDeltaTime;
    }
}