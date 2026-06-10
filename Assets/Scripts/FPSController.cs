using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FPSController : MonoBehaviour
{
    [Header("基础设置")]
    public Camera playerCamera;
    public float walkSpeed = 6f;
    public float runSpeed = 10f;
    public float crouchSpeed = 3f;
    public float jumpHeight = 1.5f;
    public float gravity = -9.81f;

    [Header("视角设置")]
    public float mouseSensitivity = 100f;
    public float lookXLimit = 90f;

    [Header("交互与UI")]
    public KeyCode uiModeKey = KeyCode.Q; // 按下此键呼出鼠标
    public bool stopMoveInUIMode = true;       // 呼出鼠标时是否禁止WASD移动？

    [Header("下蹲设置")]
    public float crouchHeight = 0.5f;
    public float standHeight = 2f;
    public KeyCode crouchKey = KeyCode.LeftControl;

    [Header("按键设置")]
    public KeyCode runKey = KeyCode.LeftShift;
    public KeyCode jumpKey = KeyCode.Space;

    [Header("物理交互")]
    public float pushPower = 2.0f;

    // 内部变量
    private CharacterController characterController;
    private Vector3 moveDirection = Vector3.zero;
    private float rotationX = 0;

    // 核心开关：是否处于UI模式（鼠标可见模式）
    private bool isUIMode = true;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        // 游戏开始默认锁定鼠标
        SetCursorState(false);
    }

    void Update()
    {
        // --- 1. 检测UI模式切换 ---
        if (Input.GetKeyDown(uiModeKey))
        {
            isUIMode = !isUIMode; // 切换状态
            SetCursorState(!isUIMode); // 如果是UI模式，就不锁定鼠标
        }

        // --- 2. 如果在UI模式下，且开启了"停止移动"，则跳过后续逻辑 ---
        if (isUIMode && stopMoveInUIMode) return;

        // --- 3. 处理移动 (WASD) ---
        bool isRunning = Input.GetKey(runKey);
        bool isCrouching = Input.GetKey(crouchKey);

        // 计算当前是否在地面
        bool isGrounded = characterController.isGrounded;

        float curSpeedX = 0;
        float curSpeedY = 0;

        // 如果在UI模式下，这里可以根据需求决定是否允许移动
        // 目前逻辑：即使呼出鼠标，只要 stopMoveInUIMode 为 false，依然可以WASD走位
        float targetSpeed = walkSpeed;
        if (isCrouching) targetSpeed = crouchSpeed;
        else if (isRunning) targetSpeed = runSpeed;

        curSpeedX = targetSpeed * Input.GetAxis("Vertical");
        curSpeedY = targetSpeed * Input.GetAxis("Horizontal");

        float movementDirectionY = moveDirection.y;
        moveDirection = (transform.forward * curSpeedX) + (transform.right * curSpeedY);

        if (Input.GetButton("Jump") && isGrounded && !isCrouching)
        {
            moveDirection.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else
        {
            moveDirection.y = movementDirectionY;
        }

        if (!isGrounded)
        {
            moveDirection.y += gravity * Time.deltaTime;
        }

        characterController.Move(moveDirection * Time.deltaTime);

        // --- 4. 处理视角 (核心修改：鼠标敏感度不受帧率影响) ---
        // 只有当【不是】UI模式时，才允许鼠标控制视角
        if (!isUIMode)
        {
            // 关键修改1：使用 Time.unscaledDeltaTime 替代 Time.deltaTime
            // unscaledDeltaTime 不受时间缩放（如慢动作）和帧率波动影响
            float mouseDeltaTime = Time.unscaledDeltaTime;

            // 垂直视角（上下）
            rotationX += -Input.GetAxis("Mouse Y") * mouseSensitivity * mouseDeltaTime;
            rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);

            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);

            // 水平视角（左右）
            // 关键修改2：同样使用 unscaledDeltaTime 保证旋转速度稳定
            transform.rotation *= Quaternion.Euler(0, Input.GetAxis("Mouse X") * mouseSensitivity * mouseDeltaTime, 0);
        }

        // --- 5. 处理下蹲形状 ---
        if (isCrouching)
        {
            characterController.height = crouchHeight;
            characterController.center = new Vector3(0, crouchHeight / 2, 0);
        }
        else
        {
            characterController.height = standHeight;
            characterController.center = new Vector3(0, standHeight / 2, 0);
        }
    }

    // 辅助函数：设置鼠标锁定状态
    // locked = true : 隐藏并锁定（FPS模式）
    // locked = false: 显示并自由（UI模式）
    void SetCursorState(bool locked)
    {
        if (locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;
        if (body == null || body.isKinematic) return;
        if (hit.moveDirection.y < -0.3) return;
        Vector3 pushDir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);
        body.velocity = pushDir * pushPower;
    }
}