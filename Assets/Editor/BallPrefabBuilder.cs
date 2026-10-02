using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

namespace SphereRoom.EditorTools
{
    /// <summary>
    /// 生成共享物理球网络预制体到 Assets/Resources/Ball.prefab（运行时由 NetworkBootstrap 加载注册）。
    /// 结构：Ball（SphereCollider(弹力物理材质) + Rigidbody + NetworkObject + NetworkRigidbody）
    /// 主机权威：刚体仅在主机端模拟（NetworkRigidbody 默认 Server 权威），
    /// 位置/速度/角速度自动广播给所有客户端，客户端只做插值渲染。
    /// 注意：预制体必须以「激活状态」保存（未激活会导致 NGO 跳过 OnNetworkSpawn 回调）。
    /// 菜单：Tools/球体房间/生成物理球预制体（幂等覆盖）。
    /// </summary>
    public static class BallPrefabBuilder
    {
        [MenuItem("Tools/球体房间/生成物理球预制体")]
        public static void Build()
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";

            // 弹力物理材质：撞障碍真实弹回
            SphereCollider collider = ball.GetComponent<SphereCollider>();
            collider.material = GetOrCreateBouncyMaterial();

            Rigidbody rigidbody = ball.AddComponent<Rigidbody>();
            rigidbody.mass = 1f;
            rigidbody.linearDamping = 0.1f;          // 空气阻力低：滚动更持久
            rigidbody.angularDamping = 0.05f;

            ball.AddComponent<NetworkObject>();
            ball.AddComponent<NetworkRigidbody>(); // 主机权威，自动同步位置/速度/角速度

            ball.GetComponent<Renderer>().sharedMaterial =
                EditorMaterialHelper.GetOrCreateMaterial(new Color(0.95f, 0.45f, 0.2f), "Ball");

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            PrefabUtility.SaveAsPrefabAsset(ball, "Assets/Resources/Ball.prefab");
            Object.DestroyImmediate(ball);
            AssetDatabase.SaveAssets();
            Debug.Log("[BallPrefabBuilder] 物理球网络预制体已生成：Assets/Resources/Ball.prefab");
        }

        private static PhysicsMaterial GetOrCreateBouncyMaterial()
        {
            const string dir = "Assets/Materials";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            const string path = "Assets/Materials/M_Bouncy.physicMaterial";
            PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material == null)
            {
                material = new PhysicsMaterial("M_Bouncy");
                AssetDatabase.CreateAsset(material, path);
            }

            material.bounciness = 1.15f;  // 反弹更强（>1 为超弹性，球会越弹越高）
            material.bounceCombine = PhysicsMaterialCombine.Maximum;
            material.dynamicFriction = 0.1f;  // 摩擦力低：滚动更顺滑
            material.staticFriction = 0.1f;
            material.frictionCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
