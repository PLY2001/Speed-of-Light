// SceneHistory.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// 单个对象在一个时间点的状态快照
[System.Serializable]
public class ObjectSnapshot
{
    public int Id;
    public Vector3 Position;
    public Quaternion Rotation;
    // 未来可扩展：动画状态、材质属性等
}

// 整个场景在一个时间点的状态快照
[System.Serializable]
public class FrameSnapshot
{
    public float Time;
    public List<ObjectSnapshot> ObjectStates = new List<ObjectSnapshot>();
}

public static class SceneHistory
{
    public static List<FrameSnapshot> History = new List<FrameSnapshot>();
    public static float FrameInterval { get; private set; }
    public static float MaxTime => History.Count > 0 ? History.Last().Time : 0f;

    public static void Initialize(float frameInterval)
    {
        History.Clear();
        FrameInterval = frameInterval;
    }

    public static void AddFrame(FrameSnapshot frame)
    {
        History.Add(frame);
    }

    // 核心功能：获取任意过去时刻一个对象的状态（使用线性插值）
    public static ObjectSnapshot GetObjectStateAtTime(int objectId, float time)
    {
        if (History.Count == 0 || time < 0) return null;

        // 处理边界情况
        if (time >= MaxTime) return History.Last().ObjectStates.Find(s => s.Id == objectId);
        if (time <= History.First().Time) return History.First().ObjectStates.Find(s => s.Id == objectId);

        // 查找时间点所在的两个快照帧
        float indexFloat = time / FrameInterval;
        int index1 = Mathf.FloorToInt(indexFloat);
        int index2 = Mathf.CeilToInt(indexFloat);

        if (index1 < 0 || index2 >= History.Count) return null;

        FrameSnapshot frame1 = History[index1];
        FrameSnapshot frame2 = History[index2];

        ObjectSnapshot state1 = frame1.ObjectStates.Find(s => s.Id == objectId);
        ObjectSnapshot state2 = frame2.ObjectStates.Find(s => s.Id == objectId);

        if (state1 == null || state2 == null) return null; // 该时刻物体可能不存在

        // 计算插值系数
        float t = (time - frame1.Time) / (frame2.Time - frame1.Time);
        if (float.IsNaN(t) || float.IsInfinity(t)) t = 0;

        // 返回插值后的状态
        return new ObjectSnapshot
        {
            Id = objectId,
            Position = Vector3.Lerp(state1.Position, state2.Position, t),
            Rotation = Quaternion.Slerp(state1.Rotation, state2.Rotation, t)
        };
    }
}