using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace SphereRoom.Player
{
    /// <summary>
    /// 第一人称控制器（M1 单机版）：WASD 移动 + 鼠标视角 + Shift 冲刺（消耗体力）。
    /// 鼠标锁定 / Esc 菜单由 PlayerHUD 统一管理；
    /// 本组件仅在鼠标锁定时处理视角与移动（菜单打开时输入不生效）。
    /// 注：直接读取设备状态（Keyboard/Mouse），避免动作资产与 Inspector 手工接线；
    /// M2 接入联机后复用本脚本作为本地输入源，届时如需手柄/触屏再迁移到 InputAction 资产。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(StaminaSystem))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("移动")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float gravity = -20f;

        [Header("冲刺")]
        [SerializeField] private float sprintSpeedMultiplier = 1.5f;

        [Header("视角")]
        [SerializeField] private float lookSensitivity = 0.1f;
        [SerializeField] private float pitchClamp = 80f;

        private CharacterController _controller;
        private Camera _camera;
        private StaminaSystem _stamina;
        private NetworkObject _networkObject;
        private float _pitch;              // 相机俯仰角（度）
        private float _verticalVelocity;   // 垂直速度（贴地保持小负值，保证 isGrounded）

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
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

        private void Update()
        {
            // 网络模式：仅本地玩家（owner）处理输入，其他玩家实例的位置由网络同步
            if (_networkObject != null && !_networkObject.IsOwner)
            {
                return;
            }

            // 菜单打开（鼠标解锁）时不处理视角与移动
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

            HandleLook();
            HandleMove();
        }

        /// <summary>鼠标视角：水平转身体，垂直转相机（带俯仰限制）。</summary>
        private void HandleLook()
        {
            float yaw = Mouse.current.delta.x.ReadValue() * lookSensitivity;
            float pitch = Mouse.current.delta.y.ReadValue() * lookSensitivity;

            _pitch = Mathf.Clamp(_pitch - pitch, -pitchClamp, pitchClamp);
            _camera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            transform.Rotate(0f, yaw, 0f);
        }

        /// <summary>WASD 相对朝向移动（Shift 冲刺）+ 重力贴地。</summary>
        private void HandleMove()
        {
            Vector2 input = ReadMoveInput();

            // 体力耗尽时无法冲刺；松开 Shift 才开始恢复（StaminaSystem.Tick 按「是否按住」结算）
            bool shiftHeld = Keyboard.current.shiftKey.isPressed;
            bool sprinting = shiftHeld && !_stamina.IsDepleted;
            float speed = moveSpeed * (sprinting ? sprintSpeedMultiplier : 1f);
            _stamina.Tick(shiftHeld, Time.deltaTime);

            Vector3 move = transform.TransformDirection(new Vector3(input.x, 0f, input.y)) * speed;

            _verticalVelocity = _controller.isGrounded
                ? -2f
                : _verticalVelocity + gravity * Time.deltaTime;
            move.y = _verticalVelocity;

            _controller.Move(move * Time.deltaTime);
        }

        private static Vector2 ReadMoveInput()
        {
            Keyboard keyboard = Keyboard.current;
            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
        }
    }
}
