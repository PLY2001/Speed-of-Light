using UnityEngine;
using UnityEngine.UI; // 引入UI命名空间

public class LerpMoveWithRotationAndZLinearAcceleration : MonoBehaviour
{
    // ========== X轴往复移动参数 ==========
    public float moveSpeed = 2f;          // X轴移动速度
    public float moveX = 2f;              // X轴移动距离
    private Vector3 startPos;             // X轴起始点
    private Vector3 endPos;               // X轴终点
    private float t = 0f;                 // X轴插值进度
    private int moveDirection = 1;        // X轴移动方向（1=向终点，-1=向起点）

    // ========== 旋转相关参数 ==========
    public Vector3 rotationAxis = Vector3.up; // 旋转轴（默认Y轴）
    public float minRotateSpeed = 30f;        // 最小旋转速度（度/秒）
    public float maxRotateSpeed = 90f;        // 最大旋转速度（度/秒）
    public bool randomRotateDirection = true; // 是否随机旋转方向
    private float currentRotateSpeed;         // 实时旋转速度（随机值）

    // ========== Z轴匀加速运动参数 ==========
    [Header("Z轴匀加速配置")]
    public float startZ = -5f;             // Z轴起始位置
    public float targetZ = 50f;            // Z轴目标位置
    public float startSpeedZ = 0.1f;       // Z轴初始速度（m/s）
    public float targetSpeedZ = 5f;        // Z轴到达目标时的末速度（m/s）
    private float zTotalDistance;          // Z轴总位移
    private float zCurrentDistance;        // Z轴已移动距离
    private float accelerationZ;           // Z轴恒定加速度（核心修改）
    private float currentSpeedZ;           // Z轴当前速度
    private bool isZMoveFinished = false;  // Z轴是否到达目标
    private bool isZMoveStarted = false;   // 新增：Z轴移动是否已启动（按钮控制）

    // ========== UI按钮配置 ==========
    [Header("UI控制")]
    public Button startZMoveButton;        // 拖拽绑定的启动按钮
    public bool isXMoveActiveBeforeStart = true; // 启动前是否允许X轴移动
    public bool isRotationActiveBeforeStart = true; // 启动前是否允许旋转

    void Start()
    {
        // 1. 初始化X轴往复移动（保持Y轴不变）
        startPos = new Vector3(transform.position.x - moveX, transform.position.y, startZ);
        endPos = new Vector3(transform.position.x + moveX, transform.position.y, startZ);
        // 强制初始Z轴位置为startZ
        transform.position = new Vector3(transform.position.x, transform.position.y, startZ);

        // 2. 初始化旋转速度（随机范围+随机方向）
        currentRotateSpeed = Random.Range(minRotateSpeed, maxRotateSpeed);
        if (randomRotateDirection && Random.value > 0.5f)
        {
            currentRotateSpeed = -currentRotateSpeed; // 50%概率反向旋转
        }

        // 3. 初始化Z轴匀加速运动参数
        zTotalDistance = targetZ - startZ; // Z轴总位移（55）
        zCurrentDistance = 0f;             // 初始已移动距离为0
        currentSpeedZ = startSpeedZ;       // 初始速度

        // 计算恒定加速度：基于匀加速公式 v² - v₀² = 2*a*s → a = (v² - v₀²)/(2*s)
        accelerationZ = (Mathf.Pow(targetSpeedZ, 2) - Mathf.Pow(startSpeedZ, 2)) / (2 * zTotalDistance);
        accelerationZ = Mathf.Max(accelerationZ, 0.001f); // 防止加速度为负

        // 4. 绑定按钮点击事件
        if (startZMoveButton != null)
        {
            startZMoveButton.onClick.AddListener(StartZMovement);
            // 可选：按钮点击后禁用，防止重复触发
            startZMoveButton.onClick.AddListener(() => startZMoveButton.interactable = false);
        }
        else
        {
            Debug.LogWarning("未绑定启动Z轴移动的按钮！请在Inspector中拖拽Button组件到对应字段");
        }
    }

    void Update()
    {
        // ========== 1. X轴平滑往复移动 ==========
        if (isZMoveStarted || isXMoveActiveBeforeStart)
        {
            t += moveDirection * moveSpeed * Time.deltaTime;
            float targetX = Vector3.LerpUnclamped(startPos, endPos, Mathf.SmoothStep(0f, 1f, t)).x;

            // 到达终点/起点时反向
            if (t >= 1f)
            {
                t = 1f;
                moveDirection = -1;
            }
            else if (t <= 0f)
            {
                t = 0f;
                moveDirection = 1;
            }

            // 根据Z轴是否启动，更新位置
            if (isZMoveStarted)
            {
                UpdateZMovement(targetX); // 启动后：更新X+Z位置
            }
            else
            {
                // 未启动时：仅更新X轴，保持Z轴初始位置
                transform.position = new Vector3(targetX, transform.position.y, startZ);
            }
        }

        // ========== 2. 任意轴随机旋转 ==========
        if (isZMoveStarted || isRotationActiveBeforeStart)
        {
            Vector3 randomAxis = new Vector3(Random.value, Random.value, Random.value).normalized;
            transform.Rotate(randomAxis, currentRotateSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// 独立的Z轴移动更新方法（仅在按钮启动后执行）
    /// </summary>
    private void UpdateZMovement(float targetX)
    {
        if (!isZMoveFinished)
        {
            // 1. 计算本次帧的速度增量（v = v₀ + a*t）
            float speedIncrement = accelerationZ * Time.deltaTime;
            currentSpeedZ += speedIncrement;

            // 2. 限制速度不超过目标速度
            currentSpeedZ = Mathf.Min(currentSpeedZ, targetSpeedZ);

            // 3. 计算本次帧移动的Z轴距离
            float deltaZ = currentSpeedZ * Time.deltaTime;

            // 4. 边界检测：防止超过目标Z值
            float remainingDistance = zTotalDistance - zCurrentDistance;
            if (deltaZ > remainingDistance)
            {
                deltaZ = remainingDistance;
                currentSpeedZ = targetSpeedZ;
                isZMoveFinished = true;
                zCurrentDistance = zTotalDistance;
            }
            else
            {
                zCurrentDistance += deltaZ;
            }

            // 5. 计算新的Z轴位置并应用
            float newZ = transform.position.z + deltaZ;
            transform.position = new Vector3(targetX, transform.position.y, newZ);
        }
        else
        {
            // Z轴到达目标后，仅更新X轴位置（保持Z=targetZ）
            transform.position = new Vector3(targetX, transform.position.y, targetZ);
        }
    }

    /// <summary>
    /// 按钮点击触发的Z轴移动启动方法
    /// </summary>
    public void StartZMovement()
    {
        isZMoveStarted = true;
        Debug.Log("Z轴移动已启动！");

        // 可选：启动时重置旋转速度（增加随机性）
        // ResetRotateSpeed();
    }

    /// <summary>
    /// 可选：重置所有运动状态（可绑定到重置按钮）
    /// </summary>
    public void ResetAllMovement()
    {
        // 重置Z轴状态
        isZMoveStarted = false;
        isZMoveFinished = false;
        zCurrentDistance = 0f;
        currentSpeedZ = startSpeedZ;
        transform.position = new Vector3(transform.position.x, transform.position.y, startZ);

        // 重置X轴状态
        t = 0f;
        moveDirection = 1;

        // 重置按钮状态
        if (startZMoveButton != null)
        {
            startZMoveButton.interactable = true;
        }

        // 重置旋转速度
        ResetRotateSpeed();

        Debug.Log("所有运动状态已重置！");
    }

    // 可选方法：重置旋转速度
    private void ResetRotateSpeed()
    {
        currentRotateSpeed = Random.Range(minRotateSpeed, maxRotateSpeed);
        if (randomRotateDirection && Random.value > 0.5f)
        {
            currentRotateSpeed = -currentRotateSpeed;
        }
    }

    // 编辑器Gizmos：绘制Z轴运动轨迹
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        Vector3 gizmoStart = new Vector3(transform.position.x, transform.position.y, startZ);
        Vector3 gizmoEnd = new Vector3(transform.position.x, transform.position.y, targetZ);
        Gizmos.DrawLine(gizmoStart, gizmoEnd);
        Gizmos.DrawSphere(gizmoStart, 0.2f);
        Gizmos.DrawSphere(gizmoEnd, 0.2f);

        // 绘制当前Z轴位置标记
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y, transform.position.z), 0.15f);
    }
}