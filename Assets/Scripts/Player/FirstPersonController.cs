using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace SphereRoom.Player
{
    /// <summary>
    /// 【模块】第一人称控制器（M1 单机版）：WASD 移动 + 鼠标视角 + Shift 冲刺（消耗体力）。
    ///
    /// 鼠标锁定 / Esc 菜单由 PlayerHUD 统一管理；
    /// 本组件仅在鼠标锁定时处理视角与移动（菜单打开时输入不生效）。
    ///
    /// 注：直接读取设备状态（Keyboard/Mouse），避免动作资产与 Inspector 手工接线；
    /// M2 接入联机后复用本脚本作为本地输入源，届时如需手柄/触屏再迁移到 InputAction 资产。
    ///
    /// 联机说明：组件挂在玩家网络预制体上（每台机器都有所有玩家的实例），
    /// Update 里用 NetworkObject.IsOwner 门控——只有本地玩家响应输入，其他实例位置由网络同步。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(StaminaSystem))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("移动")]
        [SerializeField] private float moveSpeed = 5f;      // 基础移动速度（米/秒）
        [SerializeField] private float gravity = -20f;      // 重力加速度（CharacterController 不自带重力）

        [Header("冲刺")]
        [SerializeField] private float sprintSpeedMultiplier = 1.5f;   // 冲刺速度倍率（5 × 1.5 = 7.5 米/秒）

        [Header("视角")]
        [SerializeField] private float lookSensitivity = 0.1f;   // 鼠标灵敏度（每像素转 0.1°）
        [SerializeField] private float pitchClamp = 80f;   // 上下看的角度限制（±80°，防止翻过头）

        private CharacterController _controller;
        private Camera _camera;
        private StaminaSystem _stamina;
        private NetworkObject _networkObject;
        private float _pitch;              // 相机俯仰角（度）
        private float _verticalVelocity;   // 垂直速度（贴地保持小负值，保证 isGrounded）

        /// <summary>缓存组件引用（相机此时可能还没挂载，Update 里做延迟解析）。</summary>
        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            // 注意：相机此时还没挂载（LocalPlayerSetup 在生成后才挂），所以用 GetComponentInChildren 可能拿不到
            _camera = GetComponentInChildren<Camera>();
            _stamina = GetComponent<StaminaSystem>();
            _networkObject = GetComponent<NetworkObject>();
        }

        private void Start()
        {
            // 兜底：若场景中 HUD 不存在（玩家未重新生成），也保证鼠标锁定、输入可用。
            // 有 HUD 时由 PlayerHUD 统一管理解锁/锁定，这里只做启动时的兜底。
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>
        /// 每帧输入处理：两道门控后分别处理视角与移动。
        /// 门控 1：网络 owner 检查（只控制本地玩家）；
        /// 门控 2：鼠标锁定检查（菜单打开时不响应游戏输入）。
        /// </summary>
        private void Update()
        {
            // 网络模式：仅本地玩家（owner）处理输入，其他玩家实例的位置由网络同步
            if (_networkObject != null && !_networkObject.IsOwner)
            {
                return;
            }

            // 菜单打开（鼠标解锁）时不处理视角与移动，防止菜单操作时角色乱转乱走
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            // 相机由 LocalPlayerSetup 在生成后挂载（晚于本组件 Awake），按需延迟解析
            if (_camera == null)
            {
                _camera = GetComponentInChildren<Camera>();
            }
            if (_camera == null)
            {
                return;
            }

            // 先视角后移动（顺序无强依赖，习惯上先转身再走）
            HandleLook();
            HandleMove();
        }

        /// <summary>
        /// 鼠标视角：水平移动量转身体（绕世界 Y 轴旋转），垂直移动量转相机俯仰（绕本地 X 轴），
        /// 俯仰用 _pitch 累计并夹在 ±pitchClamp，避免旋转翻转（万向节）问题。
        /// 拆开旋转的原因：身体转水平、相机转垂直，俯仰限制只约束相机不影响身体朝向。
        /// </summary>
        private void HandleLook()
        {
            // Mouse.current.delta = 本帧鼠标移动量（像素），乘灵敏度转成角度
            float yaw = Mouse.current.delta.x.ReadValue() * lookSensitivity;
            float pitch = Mouse.current.delta.y.ReadValue() * lookSensitivity;

            // 屏幕 Y 向上 = 鼠标上移 → 视觉上「抬头」，故俯仰角取负
            _pitch = Mathf.Clamp(_pitch - pitch, -pitchClamp, pitchClamp);
            _camera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            transform.Rotate(0f, yaw, 0f);
        }

        /// <summary>
        /// WASD 相对朝向移动（Shift 冲刺）+ 重力贴地。
        /// 移动向量 = 输入方向 × 角色朝向 × 速度（冲刺时 ×1.5）；
        /// 垂直分量由「贴地压紧 + 重力累加」计算（CharacterController 不自动算重力）。
        /// </summary>
        private void HandleMove()
        {
            Vector2 input = ReadMoveInput();

            // 体力耗尽时无法冲刺；松开 Shift 才开始恢复（StaminaSystem.Tick 按「是否按住」结算）
            bool shiftHeld = Keyboard.current.shiftKey.isPressed;
            bool sprinting = shiftHeld && !_stamina.IsDepleted;
            float speed = moveSpeed * (sprinting ? sprintSpeedMultiplier : 1f);
            _stamina.Tick(shiftHeld, Time.deltaTime);

            // 把 WASD 输入从「本地坐标系」转到「世界朝向」：W 永远是角色面朝的前方
            Vector3 move = transform.TransformDirection(new Vector3(input.x, 0f, input.y)) * speed;

            // 重力：贴地时给一个小的向下速度压住地面（isGrounded 才能持续为 true）；
            // 空中则按重力加速度累加（CharacterController 不自动算重力）
            _verticalVelocity = _controller.isGrounded
                ? -2f
                : _verticalVelocity + gravity * Time.deltaTime;
            move.y = _verticalVelocity;

            _controller.Move(move * Time.deltaTime);
        }

        /// <summary>
        /// 读取 WASD 按键状态并合成移动向量（对角同时按下时长度会被后续 Normalize/按速缩放）。
        /// 直接读 Input System 的键盘设备状态（isPressed），不依赖动作资产接线。
        /// </summary>
        /// <returns>归一化方向的原始输入（x=左右，y=前后，各分量 -1/0/1）</returns>
        private static Vector2 ReadMoveInput()
        {
            Keyboard keyboard = Keyboard.current;
            // 反向键相减：同时按 A+D 时 x=0（原地不动），符合直觉
            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
        }
    }
}
