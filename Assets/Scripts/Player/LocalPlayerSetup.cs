using Unity.Netcode;
using UnityEngine;
using SphereRoom.Game;

namespace SphereRoom.Player
{
    /// <summary>
    /// 【模块】本地玩家初始化（挂在玩家网络预制体上）。
    ///
    /// 职责：当玩家对象生成且归属本客户端时，把场景主相机挂到该玩家身上（保证只有本地玩家是第一人称视角，
    /// 其他玩家的实例只是可见的胶囊人）；生成完成后通知 GameBootstrap 构建 HUD。
    /// 销毁（断线/退出）时把相机摘回场景根，避免随玩家对象一起销毁。
    ///
    /// 关键理解：玩家预制体在「每台机器」上都会生成实例（1 个自己 + 3 个其他玩家），
    /// 但场景里只有一个主相机——所以相机只能挂在 IsOwner 的实例上，否则 4 个实例抢 1 个相机。
    /// </summary>
    public class LocalPlayerSetup : NetworkBehaviour
    {
        /// <summary>
        /// 网络生成回调（每个玩家实例在每台机器上都会触发一次）：
        /// 只有「属于本客户端的实例」（IsOwner）才执行相机挂载——
        /// 否则 4 个玩家实例会把唯一的场景相机抢来抢去。
        /// </summary>
        public override void OnNetworkSpawn()
        {
            Debug.Log($"[LocalPlayerSetup] OnNetworkSpawn IsOwner={IsOwner} IsServer={IsServer}");
            if (!IsOwner)
            {
                return;
            }

            // 把场景主相机挂到本地玩家（保留其 URP 相机数据与 AudioListener）；
            // 相机是运行时由 LocalPlayerSetup 挂载的，晚于控制器 Awake，故控制器里做延迟解析
            Camera camera = Camera.main;
            if (camera == null)
            {
                // 兜底：场景里没有主相机（异常情况）→ 现场创建一个
                camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener))
                    .GetComponent<Camera>();
                camera.tag = "MainCamera";
            }
            camera.transform.SetParent(transform);
            // 相机放到角色头部高度（胶囊体高 1.8m，眼睛约 1.6m）
            camera.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            camera.transform.localRotation = Quaternion.identity;
            camera.gameObject.name = "Camera";

            // 通知启动器：本地玩家就绪，构建 HUD（体力条/准星/菜单）
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.NotifyLocalPlayerReady(GetComponent<StaminaSystem>());
            }
        }

        /// <summary>网络销毁回调（断线/退出房间）：把相机摘回场景根，等待下次生成时重新挂载。</summary>
        public override void OnNetworkDespawn()
        {
            if (!IsOwner)
            {
                return;
            }

            // 相机摘回场景根（不销毁），否则会跟着玩家对象一起被销毁、回到大厅后没有相机渲染
            Camera camera = GetComponentInChildren<Camera>();
            if (camera != null)
            {
                camera.transform.SetParent(null);
                // 放回大厅视角的默认位置（稍高处俯视房间）
                camera.transform.position = new Vector3(0f, 2f, 6f);
                camera.transform.rotation = Quaternion.identity;
            }
        }
    }
}
