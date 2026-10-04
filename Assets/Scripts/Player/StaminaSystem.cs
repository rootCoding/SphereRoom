using System;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 体力系统：按住 Shift 冲刺时持续消耗（满→空 3 秒），松开后持续恢复（空→满 6 秒）。
    /// 体力耗尽后必须松开 Shift 才开始恢复（Tick 按「是否按住 Shift」而非「是否冲刺」结算）。
    /// UI 通过 Changed 事件监听（参数为 0~1 归一化值）。
    /// </summary>
    public class StaminaSystem : MonoBehaviour
    {
        [SerializeField] private float drainDuration = 3f;   // 满体力 → 空：持续按住 Shift 的秒数
        [SerializeField] private float regenDuration = 6f;   // 空体力 → 满：松开 Shift 后的恢复秒数

        /// <summary>体力变化（参数为 0~1 归一化值），用于 UI 更新。</summary>
        public event Action<float> Changed;

        /// <summary>当前体力（0~1 归一化值）。</summary>
        public float Normalized { get; private set; } = 1f;

        /// <summary>体力是否耗尽（耗尽时无法冲刺）。</summary>
        public bool IsDepleted => Normalized <= 0f;

        /// <summary>
        /// 按帧结算体力：按住 Shift 消耗，松开恢复。
        /// 用「时长等分」计算——deltaTime / 总时长 就是本帧应变化的比例，
        /// 帧率波动时体力速度依然恒定（不会因为帧率高消耗快）。
        /// </summary>
        /// <param name="shiftHeld">本帧是否按住 Shift 键（由控制器传入按键状态）</param>
        /// <param name="deltaTime">本帧间隔秒数</param>
        public void Tick(bool shiftHeld, float deltaTime)
        {
            float previous = Normalized;
            // 消耗与恢复都夹在 0~1 区间内，不会出现负数或溢出
            Normalized = shiftHeld
                ? Mathf.Max(0f, Normalized - deltaTime / drainDuration)
                : Mathf.Min(1f, Normalized + deltaTime / regenDuration);

            // 只有数值真正变化才发事件（Approximately 容忍浮点误差），避免 UI 每帧空刷
            if (!Mathf.Approximately(previous, Normalized))
            {
                Changed?.Invoke(Normalized);
            }
        }
    }
}
