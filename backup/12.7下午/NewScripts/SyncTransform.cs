using UnityEngine;

/// <summary>
/// 同步当前物体与目标物体的位置和旋转角度
/// </summary>
public class SyncTransform : MonoBehaviour
{
    [Header("同步目标")]
    [Tooltip("需要跟随的目标物体")]
    public Transform targetTransform; // 拖拽目标物体到此处

    [Header("同步设置")]
    [Tooltip("是否同步位置（默认开启）")]
    public bool syncPosition = true;
    [Tooltip("是否同步旋转（默认开启）")]
    public bool syncRotation = true;
    [Tooltip("是否使用固定步长同步（适合有刚体的物体）")]
    public bool useFixedUpdate = false;

    [Header("刚体适配")]
    [Tooltip("若当前物体有刚体，是否使用刚体移动（避免物理冲突）")]
    public bool useRigidbody = false;
    private Rigidbody _rigidbody;

    // 初始化
    private void Start()
    {
        // 自动获取当前物体的刚体组件（若开启刚体适配）
        if (useRigidbody)
        {
            _rigidbody = GetComponent<Rigidbody>();
            // 若没有刚体但勾选了useRigidbody，自动添加
            if (_rigidbody == null)
            {
                _rigidbody = gameObject.AddComponent<Rigidbody>();
                // 禁用刚体重力（避免同步时受重力影响）
                _rigidbody.useGravity = false;
                // 设为运动学（避免物理碰撞干扰同步）
                _rigidbody.isKinematic = true;
            }
        }

        // 安全校验：若未指定目标物体，给出警告
        if (targetTransform == null)
        {
            Debug.LogWarning($"[{gameObject.name}] 未指定同步目标！请在Inspector面板中拖拽目标物体到Target Transform字段", this);
        }
    }

    // 每帧同步（适合非刚体物体）
    private void Update()
    {
        if (!useFixedUpdate)
        {
            SyncTargetTransform();
        }
    }

    // 固定步长同步（适合有刚体的物理物体）
    private void FixedUpdate()
    {
        if (useFixedUpdate)
        {
            SyncTargetTransform();
        }
    }

    /// <summary>
    /// 核心同步逻辑
    /// </summary>
    private void SyncTargetTransform()
    {
        // 目标为空时直接返回
        if (targetTransform == null) return;

        // 同步位置
        if (syncPosition)
        {
            if (useRigidbody && _rigidbody != null)
            {
                // 刚体模式：使用MovePosition避免物理冲突
                _rigidbody.MovePosition(targetTransform.position);
            }
            else
            {
                // 普通模式：直接赋值位置
                transform.position = targetTransform.position;
            }
        }

        // 同步旋转
        if (syncRotation)
        {
            if (useRigidbody && _rigidbody != null)
            {
                // 刚体模式：使用MoveRotation同步旋转
                _rigidbody.MoveRotation(targetTransform.rotation);
            }
            else
            {
                // 普通模式：直接赋值旋转
                transform.rotation = targetTransform.rotation;
            }
        }
    }

    // 编辑器下实时预览（可选）
    private void OnDrawGizmos()
    {
        if (targetTransform != null)
        {
            // 绘制当前物体到目标物体的连线（方便调试）
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, targetTransform.position);
        }
    }
}