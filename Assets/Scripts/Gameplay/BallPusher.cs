using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using SphereRoom.UI;

namespace SphereRoom.Gameplay
{
    /// <summary>
    /// 推球交互（挂在玩家预制体上，与 CharacterController 同物体以接收 OnControllerColliderHit）：
    /// 本地玩家接触共享物理球时，把「推动意图（方向）」通过 ServerRpc 上报主机，
    /// 由主机在自己的物理帧内统一施加力——主机权威、单一数据源，
    /// 两名玩家在同一物理帧内同时推球时，主机依次施加两股力并广播唯一结果，不会出现状态冲突。
    /// </summary>
    public class BallPusher : NetworkBehaviour
    {
        [Header("推动参数")]
        [SerializeField] private float pushForce = 32f;       // 接触期间每物理帧推力
        [SerializeField] private float maxBallSpeed = 18f;    // 球速上限（防止球飞出房间）

        private float _lastPushFixedTime = -1f;  // 上次结算推力的物理帧（主机端限流）
        private PlayerHUD _hud;                  // 缓存本地 HUD（碰球显示 Tapped 用）

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // 仅本地玩家参与推球；只处理共享物理球（带 NetworkRigidbody 的刚体）
            if (!IsOwner || hit.rigidbody == null)
            {
                return;
            }
            NetworkRigidbody networkRigidbody = hit.rigidbody.GetComponent<NetworkRigidbody>();
            NetworkObject networkObject = hit.collider.GetComponent<NetworkObject>();
            if (networkRigidbody == null || networkObject == null)
            {
                return;
            }

            // 推动方向：球心相对玩家中心的水平方向
            Vector3 direction = hit.transform.position - transform.position;
            direction.y = 0f;
            direction.Normalize();

            PushBallServerRpc(direction, new NetworkObjectReference(networkObject));

            // 本地碰球提示（仅本地玩家可见）
            if (_hud == null)
            {
                _hud = Object.FindFirstObjectByType<PlayerHUD>();
            }
            _hud?.ShowTapped();
        }

        /// <summary>
        /// 主机执行：限速 + 统一施加推力（唯一的物理数据源）。
        /// 每物理帧每位玩家只结算一次：客户端推力请求按网络包批量到达，
        /// 若不加限流，一帧内会叠加多份推力，导致后加入玩家推球爆发加速（与主玩家手感不一致）。
        /// </summary>
        [ServerRpc]
        private void PushBallServerRpc(Vector3 direction, NetworkObjectReference ballReference)
        {
            // 同一物理帧只结算一次（Time.fixedTime 每物理帧严格递增，可精确判重）
            if (Time.fixedTime == _lastPushFixedTime)
            {
                return;
            }
            _lastPushFixedTime = Time.fixedTime;

            if (!ballReference.TryGet(out NetworkObject ball))
            {
                return;
            }

            Rigidbody rigidbody = ball.GetComponent<Rigidbody>();
            if (rigidbody == null || rigidbody.linearVelocity.magnitude >= maxBallSpeed)
            {
                return;
            }

            rigidbody.AddForce(direction * pushForce, ForceMode.Force);
        }
    }
}
