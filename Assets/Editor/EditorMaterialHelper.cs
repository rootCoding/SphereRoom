using UnityEditor;
using UnityEngine;

namespace SphereRoom.EditorTools
{
    /// <summary>
    /// 【模块】编辑器工具共用：按颜色获取/创建 URP Lit 材质（保存到 Assets/Materials，幂等）。
    ///
    /// 项目没有美术资源，所有颜色（房间墙体、球体、障碍物、玩家胶囊）都靠这里生成的纯色材质表现。
    /// 代码是材质的唯一数据源：每次调用都会重写颜色，改代码里的颜色值后重跑工具即全局生效。
    /// </summary>
    public static class EditorMaterialHelper
    {
        /// <summary>
        /// 按名字与颜色获取材质：Assets/Materials/M_{name}.mat 存在则复用并刷新颜色（幂等，可反复执行），
        /// 不存在则创建（优先 URP Lit 着色器，找不到时回退内置 Standard）。
        /// </summary>
        /// <param name="color">材质颜色（每次调用都会覆盖写入，保证与代码中的颜色定义一致）</param>
        /// <param name="name">材质名（生成 M_{name}.mat 文件）</param>
        public static Material GetOrCreateMaterial(Color color, string name)
        {
            const string dir = "Assets/Materials";
            // 目录不存在时先创建（AssetDatabase 操作的是工程资产库，而非磁盘路径）
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            string path = $"{dir}/M_{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                // 材质不存在 → 新建：优先 URP 的 Lit 着色器（本项目是 URP 管线）
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    // URP 着色器找不到（管线未配置好等异常情况）→ 回退内置 Standard 保证不报错
                    shader = Shader.Find("Standard");
                }
                material = new Material(shader) { name = $"M_{name}" };
                AssetDatabase.CreateAsset(material, path);
            }

            // 无论新建还是复用，都写入本次要求的颜色（幂等覆盖），并标记脏让编辑器落盘
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
