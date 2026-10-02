using System.Linq;
using UnityEngine;
using SphereRoom.Player;
using SphereRoom.UI;

namespace SphereRoom.Game
{
    /// <summary>
    /// 游戏启动器（单机）：Play 时在出生点生成玩家并构建 HUD，场景中不预置玩家与 UI。
    /// M2 联机后：玩家生成改由网络管理器按出生点依次进行（避免重叠），本组件保留 HUD 构建职责。
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            GameObject player = CreatePlayer(GetSpawnPose());
            HudBuilder.Build(player.GetComponent<StaminaSystem>());
        }

        /// <summary>取第一个出生点（按层级顺序）；没有出生点时使用默认位置。</summary>
        private static Pose GetSpawnPose()
        {
            PlayerSpawnPoint[] points = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
            if (points.Length > 0)
            {
                PlayerSpawnPoint first = points.OrderBy(p => p.transform.GetSiblingIndex()).First();
                return new Pose(first.transform.position, first.transform.rotation);
            }

            Debug.LogWarning("[GameBootstrap] 场景中没有出生点，使用默认位置 (0, 0.05, 6)。");
            return new Pose(new Vector3(0f, 0.05f, 6f), Quaternion.Euler(0f, 180f, 0f));
        }

        /// <summary>
        /// 创建玩家（先整体禁用再构建，避免 AddComponent 在子物体挂载前触发 Awake；
        /// 构建完成后激活，保证相机已就位）。
        /// </summary>
        private static GameObject CreatePlayer(Pose pose)
        {
            GameObject player = new GameObject("Player_单机");
            player.SetActive(false);
            player.transform.SetPositionAndRotation(pose.position, pose.rotation);

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            player.AddComponent<StaminaSystem>();
            player.AddComponent<FirstPersonController>();

            // 可视胶囊（略小于控制器，避免视觉穿墙）
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(player.transform);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(1f, 0.9f, 1f);
            body.GetComponent<Renderer>().sharedMaterial = CreateBodyMaterial();
            Destroy(body.GetComponent<CapsuleCollider>());

            // 沿用场景里的 Main Camera（保留 URP 相机数据与 AudioListener）
            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener))
                    .GetComponent<Camera>();
                camera.tag = "MainCamera";
            }
            camera.transform.SetParent(player.transform);
            camera.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            camera.transform.localRotation = Quaternion.identity;
            camera.gameObject.name = "Camera";

            player.SetActive(true);
            return player;
        }

        /// <summary>运行时创建玩家胶囊材质（不落盘，随场景实例存在）。</summary>
        private static Material CreateBodyMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            Material material = new Material(shader) { name = "M_Player_Runtime" };
            material.color = new Color(0.3f, 0.55f, 0.9f);
            return material;
        }
    }
}
