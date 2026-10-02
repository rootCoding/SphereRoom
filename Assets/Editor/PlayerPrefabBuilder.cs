using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using SphereRoom.Gameplay;
using SphereRoom.Player;

namespace SphereRoom.EditorTools
{
    /// <summary>
    /// 生成玩家网络预制体到 Assets/Resources/Player.prefab（运行时由 NetworkBootstrap 加载并注册到 NGO）。
    /// 结构：Player（CharacterController + StaminaSystem + FirstPersonController + NetworkObject
    ///            + NetworkTransform(Owner 权威) + LocalPlayerSetup）
    ///        └─ Body 可视胶囊（无碰撞体，碰撞交给 CharacterController）
    /// 相机不放在预制体里：由 LocalPlayerSetup 在「本地玩家」生成后把场景主相机挂上，
    /// 保证只有本地玩家是第一人称视角，其他玩家实例只是可见的胶囊人。
    /// 菜单：Tools/球体房间/生成玩家网络预制体（幂等覆盖；玩家结构变化时重跑一次即可）。
    /// </summary>
    public static class PlayerPrefabBuilder
    {
        [MenuItem("Tools/球体房间/生成玩家网络预制体")]
        public static void Build()
        {
            // 注意：预制体必须以「激活状态」保存。
            // 若保存为未激活，NGO 克隆出的实例会跳过所有 NetworkBehaviour 的 OnNetworkSpawn 回调
            // （见 NGO 源码 NetworkObject.InvokeBehaviourNetworkSpawn 中 activeInHierarchy 检查）。
            // 编辑器里 AddComponent 不会触发 Awake，保持激活构建是安全的。
            GameObject player = new GameObject("Player");

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            player.AddComponent<StaminaSystem>();
            player.AddComponent<FirstPersonController>();
            player.AddComponent<NetworkObject>();

            // Owner 权威：本地操控本地计算，位置/旋转同步给其他客户端（第一人称手感好）
            NetworkTransform networkTransform = player.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;

            player.AddComponent<LocalPlayerSetup>();
            player.AddComponent<BallPusher>();

            // 可视胶囊（略小于控制器，避免视觉穿墙）
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(player.transform);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(1f, 0.9f, 1f);
            body.GetComponent<Renderer>().sharedMaterial =
                EditorMaterialHelper.GetOrCreateMaterial(new Color(0.3f, 0.55f, 0.9f), "Player");
            Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            PrefabUtility.SaveAsPrefabAsset(player, "Assets/Resources/Player.prefab");
            Object.DestroyImmediate(player);
            AssetDatabase.SaveAssets();
            Debug.Log("[PlayerPrefabBuilder] 玩家网络预制体已生成：Assets/Resources/Player.prefab");
        }
    }
}
