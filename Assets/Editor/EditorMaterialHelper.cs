using UnityEditor;
using UnityEngine;

namespace SphereRoom.EditorTools
{
    /// <summary>编辑器工具共用：按颜色获取/创建 URP Lit 材质（保存到 Assets/Materials，幂等）。</summary>
    public static class EditorMaterialHelper
    {
        public static Material GetOrCreateMaterial(Color color, string name)
        {
            const string dir = "Assets/Materials";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            string path = $"{dir}/M_{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }
                material = new Material(shader) { name = $"M_{name}" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
