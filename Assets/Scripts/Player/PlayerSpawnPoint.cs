using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 玩家出生点标记：在场景中摆放位置即可（位置与朝向均可自由编辑，策划可随时调整）。
    /// 运行时按层级顺序依次读取；M2 联机后按玩家加入顺序依次分配，避免重叠。
    /// </summary>
    public class PlayerSpawnPoint : MonoBehaviour
    {
        /// <summary>
        /// 场景视图可视化：画一个小球 + 朝向线，方便在 Scene 面板里直接摆放和旋转出生点。
        /// OnDrawGizmos 只在编辑器里绘制，运行时零开销。
        /// </summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            // 小球 = 出生点位置
            Gizmos.DrawSphere(transform.position, 0.25f);
            // 朝向线 = 玩家出生后的面朝方向（朝线的延长方向看）
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.8f);
        }
    }
}
