using UnityEditor;
using UnityEngine;

namespace SphereRoom.EditorTools
{
    /// <summary>
    /// 【模块】一键生成「球体房间」灰盒场景：地板 + 四面墙 + 障碍物。
    ///
    /// 用法：菜单 Tools/球体房间/生成灰盒房间，生成后 Ctrl+S 保存场景。
    /// 重复点击会先删除旧的房间再重建（幂等）。
    ///
    /// 设计说明：
    /// - 房间是纯静态碰撞盒（Cube + BoxCollider），球的弹回靠「球的弹力材质 + 盒碰撞体」，无需任何场景美术
    /// - 所有尺寸是代码常量：房间 20×20 米、墙高 4 米、墙厚 1 米，改常量即可整体调整
    /// - 颜色材质由 EditorMaterialHelper 生成到 Assets/Materials（与球/玩家材质统一管理）
    /// - 全部对象挂在 Room_灰盒 根节点下，方便整体删除/移动
    /// </summary>
    public static class RoomBuilder
    {
        // 房间尺寸常量（米）
        private const float RoomSize = 20f;        // 房间边长（米）
        private const float WallHeight = 4f;        // 墙高
        private const float WallThickness = 1f;     // 墙厚

        /// <summary>
        /// 菜单入口：Tools/球体房间/生成灰盒房间。
        /// 执行流程：删除旧房间（幂等）→ 建地板 → 建四面墙 → 建 3 个障碍物 → 保存并选中。
        /// </summary>
        [MenuItem("Tools/球体房间/生成灰盒房间")]
        public static void BuildRoom()
        {
            // 幂等：先删除旧房间（Undo 记录，误删可撤销）
            GameObject old = GameObject.Find("Room_灰盒");
            if (old != null)
            {
                Undo.DestroyObjectImmediate(old);
            }

            // 所有房间物体挂在同一根节点下
            GameObject root = new GameObject("Room_灰盒");

            // 地板（顶面在 y=0：中心放 -0.5 让 1 米厚的盒子顶面正好与地面平齐）
            CreateBox(root, "Floor",
                new Vector3(0f, -0.5f, 0f),
                new Vector3(RoomSize, 1f, RoomSize),
                new Color(0.25f, 0.25f, 0.28f));

            // 四面墙：位置从房间边线向外偏移半个墙厚（墙内表面正好贴房间边线，内部空间正好 20×20）
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

            // 障碍物（供球体弹回测试）：大小/位置错落摆放，颜色区分。
            // 3 个障碍物分别位于房间的不同象限，测试球撞障碍弹回的多种角度
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

            // 收尾三件事：
            // 1) 记录撤销（Undo 一次退回生成前）
            Undo.RegisterCreatedObjectUndo(root, "生成灰盒房间");
            // 2) 保存资产库（新建的材质落盘）
            AssetDatabase.SaveAssets();
            // 3) 自动选中根节点，方便在 Inspector 直接查看/调整
            Selection.activeGameObject = root;   // 生成后自动选中，方便直接查看
            Debug.Log("[RoomBuilder] 灰盒房间已生成（Room_灰盒），请 Ctrl+S 保存场景。");
        }

        /// <summary>
        /// 创建静态碰撞盒，并指定颜色材质（材质保存到 Assets/Materials）。
        /// </summary>
        /// <param name="parent">父节点（房间根）</param>
        /// <param name="name">物体名（Hierarchy 可读）</param>
        /// <param name="position">世界坐标位置</param>
        /// <param name="scale">三个方向的尺寸（米）</param>
        /// <param name="color">材质颜色</param>
        private static GameObject CreateBox(GameObject parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.isStatic = true;   // 房间不移动：标记静态让 Unity 做碰撞合并等优化

            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = EditorMaterialHelper.GetOrCreateMaterial(color, name);
            return go;
        }

    }
}
