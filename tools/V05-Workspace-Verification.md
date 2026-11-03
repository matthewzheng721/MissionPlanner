# V05 原生三区工作台：实现与验证

## 已完成

- 沿用 Mission Planner 的 C#／WinForms／net472。左侧常驻原 HUD 和 Quick，中间原地图，右侧动作、航点、参数、工具；没有 WebView 或假遥测。
- HUD 双击打开原 HUD 控件的独立副本，共享 46 项原遥测绑定及自定义显示，原常驻 HUD 不移走。独立窗关闭、重开和 FBWA／AUTO 连续更新已实测。
- 电池 1／2、数传收包质量、GPS 使用真实 CurrentState；EKF／VIBE 使用原 HUD 阈值和详情入口。
- 三区宽度、HUD／Quick 高度及右栏收起状态使用原 Settings。新进程恢复左 335、HUD 270、右 390 和收起状态通过。
- 参数仅 FAV、参数名、值三列。切页保留编辑；收藏即时记录，编辑时不跳行。原四列比较在工作台为非模态，Continue 仅回填，不直接发送；1100×700 最小桌面内初始位置完整位于地图／操作区，不压左 HUD／Quick。原顶部配置路径仍使用原模态生命周期。
- 离线先开参数再连接会自动加载并启用写入。连接或目标改变时清除旧编辑、关闭旧比较并显示说明，重新读取当前目标。F5 显式刷新常驻表；移除了重挂页面导致的重复清表路径。
- 航点页打开原 FlightPlanner 非模态窗口，保持原任务编辑、文件和传输能力；恢复了 MainSwitcher 注册时隐藏的原控件可见性。完整航点表内联迁移留待后续。
- 底栏独立检查入口。两处解锁按钮须八项全部人工核对后启用，跳过不算完成；旧恢复任务、强制解锁、摇杆入口复核同一检查和执行前确认。AUX 解锁／锁定选项由控制窗口补齐同一约束，验证见控制报告。
- 开机、恢复、停车共享原 ConfigProjectMotor 实例；开机／停车执行前确认。局部 ACK 等待、换流取消和脚本行为由控制窗口实现并实测。
- 本地固件、扩展调参、用户自定义参数入口移除。F5 保持快捷键；Google 地图及 TLOG 原逻辑未修改。

## 实际验证

证据根目录：`D:/project/drone-eps/tools/gcs-ui-prototype/v05-acceptance`。

- 最终统一构建：2026-10-30 22:20:54（本地时间），MSBuild 退出码 0，日志 `../v05-final-acceptance-build.log`。主程序生产源码最后编译产物时间 22:14:40，晚于 AUX 22:14:22、航点显示修复 22:13:37、Motor 22:02:58；最终增量构建仅需更新测试 DLL（22:20:54）。
- `unit/v05-final-43.trx`：最终相关组合 43／43 通过、0 失败，耗时 12.265 秒，覆盖控制／AUX 33、检查 2、原电机回读 2、摇杆 4、任务文件 2。新增 AUX 全部 21 例包含其中。
- `unit/v05-acceptance-final.trx`：相关组合 22／22 通过，包括控制 12、检查 2、原电机回读 2、摇杆拒绝／取消 4、任务文件 2。
- `unit/v05-control-status-theme.trx`：最后控制状态和主题修复后再次 12／12 通过。
- `result/result.txt`、`result/status.txt`：专用 ArduPlane SITL 5890／5910 最终 PASS。SIM_SPEEDUP 与未解锁状态先确认；未连真实设备、未解锁、未执行发动机命令。
- 新极性页 FBWA／AUTO 按钮均等待真实 heartbeat 模式反馈；准备两条未完成任务，避免空 AUTO 任务立即转 RTL。
- 三列表实际写 AIRSPEED_CRUISE=23 并回读 23；外部改为 24 后 F5 屏幕更新 24；恢复原值 22。A 待写后断联→B(sysid=2)，旧编辑清空、旧比较关闭、B 表重新读取且无旧待写。
- `transfer/result.txt`、`transfer/status.txt`：180 航点编辑、文件往返、真实上传和下载逐项一致 PASS。上传时主界面可用、仅任务编辑区禁用；模式 STABILIZE 实际回读，AIRSPEED_CRUISE 写入／回读／恢复通过。221 个采样中 UI 定时器间隔平均 67.5 ms、最大 900 ms；最近遥测年龄平均 40.2 ms、最大 468.6 ms。这是本机运行记录，不代表低速数传指标。`transfer/mission-window.png` 为填入 180 行后的航点窗口自身截图；地图为本地缓存模式，缺瓦片区如实显示，未测试 Google 外网。
- `result/layout-restore.txt`：独立新进程从同一 Settings 恢复布局 PASS。
- 向导自身截图：`result/wizard-basic.png`、`wizard-servo.png`、`wizard-polarity.png`、`wizard-summary.png`。脚本页主题修复后已肉眼核对文字可读。`result/hud-copy.png` 为原 HUD 副本；`result/compare-minimum-desktop.png` 为最小桌面对比窗口。

## 本轮发现并修复

- 参数默认值列重现、值列超出右栏、底栏遮挡、首次参数未到达时写入未恢复。
- 原 HUD 双击功能被新容器禁用，已恢复独立副本而不搬走常驻 HUD。
- 参数生命周期未覆盖离线→连接及 A→B；F5 只下载缓存和重复重挂引起清表竞态，均以真实 SITL 复现后修复。
- 航点窗口仅外层可见而原控件仍被 MainSwitcher 隐藏，已补原控件 Show 并增加坐标、地图实际可见性断言。
- AUX 首轮 11／21 通过、10 个确认发送路径超时（`unit/v05-aux.trx`）。实际根因是测试 fixture 用 MAVLink1 生成带扩展字段的 10 字节 ACK，而解析器要求该版本最小 3 字节，假帧被拒绝。改为合法 MAVLink2 帧后，5 个发送成功最小例全部通过（`unit/v05-aux-ack-fixed.trx`），再通过最终 43 项。未放松生产确认或连接检查。
- 控制测试最初裸 STA 测试宿主缺少 WinForms 消息循环，已改真实 Application.Run 宿主；测试换流后被 Dispose 的注入流重开。保留旧失败 TRX，最终通过结果以上述路径为准。

## 范围边界

- 本批没有完成航点完整内联、工具全部配置页重组或仿真入口；不能把原顶部配置页面都称为迁移完成。
- 专用 SDT 脚本、ECU 缺失字段以及真实机硬件反馈没有由通用 SITL 提供；控制使用离线协议捕获证明报文和时序，不能称为硬件验收。
- 原 setParam 内部重试沿用既有可变连接行为，这是既有通信待办，本批没有重写参数协议。新增常驻页的旧编辑／旧比较跨目标问题已修复并实测。
- 未验收低速数传、掉包、长时性能及真实 RTK。本机 TCP 结果不代表这些条件。
- 未改动用户 E:/MP，未关闭用户原有进程，未同步日常运行目录；未覆盖 tools/Retained-Feature-Verification.md；未提交、未推送。


