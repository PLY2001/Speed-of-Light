// TrackableObject.cs
using UnityEngine;

public class TrackableObject : MonoBehaviour
{
    // 在Inspector中为每个可追踪对象分配一个唯一的ID
    [Tooltip("为场景中每个可追踪对象设置一个唯一的ID")]
    public int UniqueId;

    private void OnValidate()
    {
        // 自动查找场景中的其他对象，防止ID冲突
        TrackableObject[] allTrackables = FindObjectsOfType<TrackableObject>();
        foreach (var other in allTrackables)
        {
            if (other != this && other.UniqueId == this.UniqueId)
            {
                Debug.LogError($"ID冲突: 对象 '{this.name}' 和 '{other.name}' 拥有相同的唯一ID {this.UniqueId}。请修改。", this);
            }
        }
    }
}