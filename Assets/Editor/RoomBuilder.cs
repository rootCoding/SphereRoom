using UnityEditor;
using UnityEngine;

namespace SphereRoom.EditorTools
{
    /// <summary>
    /// 一键生成「球体房间」灰盒场景：地板 + 四面墙 + 障碍物。
    /// 用法：菜单 Tools/球体房间/生成灰盒房间，生成后 Ctrl+S 保存场景。
    /// 重复点击会先删除旧的房间再重建（幂等）。
    /// </summary>
    public static class RoomBuilder
    {
        private const float RoomSize = 20f;        // 房间边长（米）
        private const float WallHeight = 4f;        // 墙高
        private const float WallThickness = 1f;     // 墙厚

        [MenuItem("Tools/球体房间/生成灰盒房间")]
        public static void BuildRoom()
        {
            // 幂等：先删除旧房间
            GameObject old = GameObject.Find("Room_灰盒");
            if (old != null)
            {
                Undo.DestroyObjectImmediate(old);
            }

            GameObject root = new GameObject("Room_灰盒");

            // 地板（顶面在 y=0）
            CreateBox(root, "Floor",
                new Vector3(0f, -0.5f, 0f),
                new Vector3(RoomSize, 1f, RoomSize),
                new Color(0.25f, 0.25f, 0.28f));

            // 四面墙
            float half = RoomSize / 2f;
            float wallOffset = half + WallThickness / 2f;
            CreateBox(root, "Wall_North",
                new Vector3(0f, WallHeight / 2f, wallOffset),
                new Vector3(RoomSize + 2f * WallThickness, WallHeight, WallThickness),
                new Color(0.45f, 0.45f, 0.5f));
            CreateBox(root, "Wall_South",
                new Vector3(0f, WallHeight / 2f, -wallOffset),
                new Vector3(RoomSize + 2f * WallThickness, WallHeight, WallThickness),
                new Color(0.45f, 0.45f, 0.5f));
            CreateBox(root, "Wall_East",
                new Vector3(wallOffset, WallHeight / 2f, 0f),
                new Vector3(WallThickness, WallHeight, RoomSize + 2f * WallThickness),
                new Color(0.45f, 0.45f, 0.5f));
            CreateBox(root, "Wall_West",
                new Vector3(-wallOffset, WallHeight / 2f, 0f),
                new Vector3(WallThickness, WallHeight, RoomSize + 2f * WallThickness),
                new Color(0.45f, 0.45f, 0.5f));

            // 障碍物（供球体弹回测试）
            CreateBox(root, "Obstacle_A",
                new Vector3(4f, 1f, 3f),
                new Vector3(3f, 2f, 2f),
                new Color(0.6f, 0.42f, 0.25f));
            CreateBox(root, "Obstacle_B",
                new Vector3(-5f, 0.75f, -4f),
                new Vector3(2f, 1.5f, 2f),
                new Color(0.25f, 0.45f, 0.6f));
            CreateBox(root, "Obstacle_C",
                new Vector3(1f, 0.5f, -6f),
                new Vector3(2f, 1f, 4f),
                new Color(0.35f, 0.55f, 0.35f));

            Undo.RegisterCreatedObjectUndo(root, "生成灰盒房间");
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            Debug.Log("[RoomBuilder] 灰盒房间已生成（Room_灰盒），请 Ctrl+S 保存场景。");
        }

        /// <summary>创建静态碰撞盒，并指定颜色材质（材质保存到 Assets/Materials）。</summary>
        private static GameObject CreateBox(GameObject parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.isStatic = true;

            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = GetOrCreateMaterial(color, name);
            return go;
        }

        private static Material GetOrCreateMaterial(Color color, string name)
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
