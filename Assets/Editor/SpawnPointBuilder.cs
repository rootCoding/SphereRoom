using System.Linq;
using UnityEditor;
using UnityEngine;
using SphereRoom.Game;
using SphereRoom.Player;

namespace SphereRoom.EditorTools
{
    /// <summary>
    /// 一键生成「游戏启动器 + 一排出生点」。玩家与 HUD 已改为运行时生成，场景中不再预置。
    /// 幂等：启动器与出生点已存在时不重建——出生点位置可自由拖动编辑，重复点击不会覆盖你的改动。
    /// 同时清理旧版场景中的单机玩家与 HUD（相机先摘回场景根再销毁）。
    /// 菜单：Tools/球体房间/生成出生点与启动器
    /// </summary>
    public static class SpawnPointBuilder
    {
        /// <summary>
        /// 出生点常驻数量 = 4（位置自由编辑）。
        /// 运行时按实际联机人数启用前 N 个，其余由 NetworkBootstrap 禁用（用户要求：2 人联机只留前 2 个）。
        /// </summary>
        private const int SpawnPointCount = 4;

        [MenuItem("Tools/球体房间/生成出生点与启动器")]
        public static void Build()
        {
            CleanupLegacyObjects();

            if (Object.FindFirstObjectByType<GameBootstrap>() == null)
            {
                new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
            }

            SyncSpawnPoints();

            Debug.Log($"[SpawnPointBuilder] 启动器与 {SpawnPointCount} 个出生点已就绪，Ctrl+S 保存场景。");
        }

        /// <summary>
        /// 按 SpawnPointCount 同步出生点数量：多了删除（保留层级靠前的，已编辑位置不受影响），少了创建。
        /// </summary>
        private static void SyncSpawnPoints()
        {
            PlayerSpawnPoint[] points = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None)
                .OrderBy(p => p.transform.GetSiblingIndex()).ToArray();

            // 删除多余的出生点
            for (int i = SpawnPointCount; i < points.Length; i++)
            {
                Undo.DestroyObjectImmediate(points[i].gameObject);
            }

            // 创建缺少的出生点
            int existing = Mathf.Min(points.Length, SpawnPointCount);
            for (int i = existing; i < SpawnPointCount; i++)
            {
                GameObject point = new GameObject($"SpawnPoint_{i + 1}");
                point.transform.position = DefaultPosition(i);
                point.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                point.AddComponent<PlayerSpawnPoint>();
                Undo.RegisterCreatedObjectUndo(point, "生成出生点");
            }
        }

        /// <summary>默认出生点：一排均匀分布且居中（1 个时在正中央），面朝房间中心。</summary>
        private static Vector3 DefaultPosition(int index)
        {
            float x = (index - (SpawnPointCount - 1) * 0.5f) * 2f;
            return new Vector3(x, 0.05f, 6f);
        }

        /// <summary>清理旧版编辑器生成的玩家/HUD/EventSystem（新架构下均由运行时创建）。</summary>
        private static void CleanupLegacyObjects()
        {
            GameObject oldPlayer = GameObject.Find("Player_单机");
            if (oldPlayer != null)
            {
                Camera camera = oldPlayer.GetComponentInChildren<Camera>();
                if (camera != null)
                {
                    camera.transform.SetParent(null);
                }
                Undo.DestroyObjectImmediate(oldPlayer);
            }
            GameObject oldHud = GameObject.Find("PlayerHUD");
            if (oldHud != null)
            {
                Undo.DestroyObjectImmediate(oldHud);
            }
            GameObject oldEventSystem = GameObject.Find("EventSystem");
            if (oldEventSystem != null)
            {
                Undo.DestroyObjectImmediate(oldEventSystem);
            }
        }
    }
}
