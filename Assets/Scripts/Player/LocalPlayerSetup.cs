using Unity.Netcode;
using UnityEngine;
using SphereRoom.Game;

namespace SphereRoom.Player
{
    /// <summary>
    /// 本地玩家初始化（挂在玩家网络预制体上）：
    /// 当玩家对象生成且归属本客户端时，把场景主相机挂到该玩家身上（保证只有本地玩家是第一人称视角，
    /// 其他玩家的实例只是可见的胶囊人）；生成完成后通知 GameBootstrap 构建 HUD。
    /// 销毁（断线/退出）时把相机摘回场景根，避免随玩家对象一起销毁。
    /// </summary>
    public class LocalPlayerSetup : NetworkBehaviour
    {
        public override void OnNetworkSpawn()
        {
            Debug.Log($"[LocalPlayerSetup] OnNetworkSpawn IsOwner={IsOwner} IsServer={IsServer}");
            if (!IsOwner)
            {
                return;
            }

            // 把场景主相机挂到本地玩家（保留其 URP 相机数据与 AudioListener）
            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener))
                    .GetComponent<Camera>();
                camera.tag = "MainCamera";
            }
            camera.transform.SetParent(transform);
            camera.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            camera.transform.localRotation = Quaternion.identity;
            camera.gameObject.name = "Camera";

            // 通知启动器：本地玩家就绪，构建 HUD
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.NotifyLocalPlayerReady(GetComponent<StaminaSystem>());
            }
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner)
            {
                return;
            }

            // 相机摘回场景根，等待下次生成时重新挂载
            Camera camera = GetComponentInChildren<Camera>();
            if (camera != null)
            {
                camera.transform.SetParent(null);
                camera.transform.position = new Vector3(0f, 2f, 6f);
                camera.transform.rotation = Quaternion.identity;
            }
        }
    }
}
