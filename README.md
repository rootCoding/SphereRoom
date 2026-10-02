# SphereRoom（球体房间）

Unity 6 多人联机技术测试 Demo：支持 1-4 名玩家联机，第一人称视角在房间内推动共享物理球（可撞障碍弹回，多人同帧推球无状态冲突）。

## 运行方式

- 环境：Unity 6000.3.14f1（URP），打开项目后进入 `SampleScene`
- 单机测试：直接 Play → 大厅点「创建房间」（主机，默认端口 12305）即进入游戏
- 多人联机（局域网）：主机点「创建房间」；其他玩家输入主机 IP 与端口点「加入房间」
- 编辑器多实例测试：安装 Multiplayer Play Mode 包（Window → Multiplayer Play Mode），开启 Virtual Players（2 个）后 Play
- Steam 联机：大厅切换「联机方式 → Steam 联机」，主机点「创建房间」获得大厅 ID（Esc 菜单可一键复制）；加入方输入大厅 ID 即可加入；主机也可在 Esc 菜单邀请 Steam 好友，好友接受邀请后自动进入房间（Steam 客户端需登录，测试用 AppID 480，无需开发者账号）
- Windows 可运行包：见 [GitHub Release v1.0.0](https://github.com/rootCoding/SphereRoom/releases/tag/v1.0.0) 附件 `SphereRoom-Windows.zip`（解压运行 `SphereRoom.exe`）

## 操作

WASD 移动 · 鼠标视角 · Shift 冲刺（消耗体力，3 秒耗尽 / 6 秒回满）· Esc 菜单 · 身体碰撞推球

## 网络方案与理由

采用 **Unity Netcode for GameObjects（NGO 2.x）+ Unity Transport**，主机权威（Host-Authoritative）模型，理由如下：

1. **题目要求「共享状态以主机为唯一数据源」**——NGO 的 ServerRpc / 网络变量体系与该模型一一对应，代码结构即架构说明
2. **共享物理球**：Rigidbody 仅在主机模拟，`NetworkRigidbody` 自动广播位置/速度/角速度，客户端只做插值渲染；推球由客户端上报「推动意图」（ServerRpc），主机在物理帧内统一施力并**每物理帧限流一次**——两名玩家同帧同时推球时，主机依次结算两股力并广播唯一结果，不会出现状态冲突
3. **玩家**：owner 权威 `NetworkTransform`（本地操控本地计算、同步给他人），第一人称手感与网络负担兼得
4. 主机断开时客户端弹窗提示并可确认返回大厅；中途加入自动同步球的状态；官方包与 Unity 6 深度集成，1-4 人小房间正是其典型场景

### Steam 联机（超级加分项）

社区现成的 Steam 传输层均停留在 NGO 1.x 时代（内置 2020 年旧版 Steam 库，有已知 bug），故**自写 NGO 2.x 传输层**（`SteamTransport.cs`）：基于 Facepunch.Steamworks 2.5.2 的 Steam P2P 网络实现 `NetworkTransport` 接口——客户端 SteamId ↔ NGO clientId 映射、优雅退出 BYE 报文、1 秒心跳 + 5 秒超时断连检测；配合 Steam 大厅（创建 / 大厅 ID 加入 / 好友邀请 / 接受邀请自动加入，Overlay 不可用时自动回退游戏内好友列表邀请）。局域网直连模式完整保留。

> **验证状态（如实说明）**：局域网双人全流程实测通过；Steam 单人侧全流程验证通过（初始化 / 大厅创建 / 邀请送达好友），游戏内双账号实测需两台电脑暂未完成。

（详细技术思路见《技术测试思路.docx》）
