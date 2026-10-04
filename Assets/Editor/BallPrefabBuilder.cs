using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

namespace SphereRoom.EditorTools
{
    /// <summary>
    /// 【模块】生成共享物理球网络预制体到 Assets/Resources/Ball.prefab（运行时由 NetworkBootstrap 加载注册）。
    ///
    /// 结构：Ball（SphereCollider(弹力物理材质) + Rigidbody + NetworkObject + NetworkRigidbody）
    /// 主机权威：刚体仅在主机端模拟（NetworkRigidbody 默认 Server 权威），
    /// 位置/速度/角速度自动广播给所有客户端，客户端只做插值渲染。
    ///
    /// 注意：预制体必须以「激活状态」保存（未激活会导致 NGO 跳过 OnNetworkSpawn 回调）。
    /// 菜单：Tools/球体房间/生成物理球预制体（幂等覆盖）。
    /// </summary>
    public static class BallPrefabBuilder
    {
        /// <summary>
        /// 菜单入口：生成/覆盖物理球预制体。
        /// 执行流程：建球体 → 挂弹力材质 → 挂刚体 → 挂网络组件 → 存预制体。
        /// </summary>
        [MenuItem("Tools/球体房间/生成物理球预制体")]
        public static void Build()
        {
            // Unity 原生球体自带 SphereCollider（半径 0.5 米），无需手动添加碰撞体
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";

            // 弹力物理材质：撞障碍真实弹回（bounciness > 1 为超弹性，球会越弹越高）
            SphereCollider collider = ball.GetComponent<SphereCollider>();
            collider.material = GetOrCreateBouncyMaterial();

            // 刚体：质量 1，低阻力（linearDamping 0.1 让球滚动更持久）
            Rigidbody rigidbody = ball.AddComponent<Rigidbody>();
            rigidbody.mass = 1f;
            rigidbody.linearDamping = 0.1f;          // 空气阻力低：滚动更持久
            rigidbody.angularDamping = 0.05f;

            // 网络组件：NetworkRigidbody 主机权威，自动同步位置/速度/角速度
            //（球的所有物理只在主机模拟，客户端做插值渲染——这正是「主机权威」模型的体现）
            ball.AddComponent<NetworkObject>();
            ball.AddComponent<NetworkRigidbody>();

            // 橙色材质便于识别（与玩家胶囊/障碍物颜色区分；材质生成到 Assets/Materials）
            ball.GetComponent<Renderer>().sharedMaterial =
                EditorMaterialHelper.GetOrCreateMaterial(new Color(0.95f, 0.45f, 0.2f), "Ball");

            // Resources 目录不存在则创建（Resources.Load 依赖该目录）
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            // 保存预制体后销毁临时场景物体（幂等覆盖）
            PrefabUtility.SaveAsPrefabAsset(ball, "Assets/Resources/Ball.prefab");
            Object.DestroyImmediate(ball);
            AssetDatabase.SaveAssets();
            Debug.Log("[BallPrefabBuilder] 物理球网络预制体已生成：Assets/Resources/Ball.prefab");
        }

        /// <summary>
        /// 获取/创建弹力物理材质（Assets/Materials/M_Bouncy.physicMaterial，幂等）。
        /// 参数说明：弹力 1.15（超弹性）、摩擦 0.1（顺滑滚动）。
        /// </summary>
        private static PhysicsMaterial GetOrCreateBouncyMaterial()
        {
            const string dir = "Assets/Materials";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            // 存在则加载复用，不存在则创建（幂等覆盖参数）
            const string path = "Assets/Materials/M_Bouncy.physicMaterial";
            PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material == null)
            {
                material = new PhysicsMaterial("M_Bouncy");
                AssetDatabase.CreateAsset(material, path);
            }

            // 每次执行都重写参数，保证代码是材质的唯一数据源（改这里下次生成即生效）
            material.bounciness = 1.15f;  // 反弹更强（>1 为超弹性，球会越弹越高）
            material.bounceCombine = PhysicsMaterialCombine.Maximum;
            material.dynamicFriction = 0.1f;  // 摩擦力低：滚动更顺滑
            material.staticFriction = 0.1f;
            material.frictionCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(material);   // 参数改动落盘
            return material;
        }
    }
}
