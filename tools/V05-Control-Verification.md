# V05 检查与控制首批交付记录

## 范围

- 平台五步与起飞条件三步逐项人工核对；跳过/未核对均不允许解锁。向导含逐项结果汇总和完成按钮，不自动解锁。
- ECU 只解析已确认的 `ECU Cy1T C1:… C2:… C3:… C4:…` 和 `ECU ACK: throttle valid=0/1`，显示接收时间。未确认的油压、电压等不生成数值。显示数据不会自动勾选检查项。
- SDT v3 参数来自飞控缓存，SDT_STATE 单独请求最新回读；RUN=1运行、2回TRIM保持1秒后释放、3定位起点。RUN命令单次排队发送，不重试旧运行命令。Lua未修改。
- 极性读取 RCMAP 与 RCx_MIN/TRIM/MAX，操作员明确设置幅度；正负按钮表示PWM输入方向，不假设物理抬头/低头。松开回中，失焦/关闭释放；AUTO/FBWA由明确按钮选择，并先完成输入释放。
- 一键开机先发送 Engine Start，获接受应答后写 SERVO5_TRIM=1300。恢复独立写2000，不自动写回。停车发送 Engine Stop。开机/停车/解锁/锁定执行前确认，恢复不确认，无结果弹窗。

## 已修复的控制队列问题

原 MAVLinkInterface.doEngineControl 的内部应答重试使用调用时的当前 BaseStream；在等待期间换流，外层发送前后判断不能阻止内部重试发到新流。本模块改用局部 COMMAND_ACK 订阅，不接管公共接收循环，也不修改 MAVLinkInterface。

仍保留原2秒应答等待间隔和最多3次重试；每次发送和等待期间校验原连接流、目标及局部操作序号。新的恢复/停车使先前等待取消，让队列及时执行新操作；取消后的旧开机不得再写1300。应答订阅在finally解绑。超时、拒绝和取消以控件内文字提示，不称作实际发动机状态。

极性队列保存原流身份，关闭/回中更新局部输入序号，丢弃过时的非零输入；旧连接的释放不得发送到替换后的新连接。

## 验证状态

离线测试使用 CommsInjection 内存流捕获真实MAVLink序列，弹窗应答只枚举测试自身UI线程窗口。没有真实飞控连接，没有关闭用户进程。

测试源码：`MissionPlannerTests/Utilities/ProjectControlBehaviorTests.cs`。覆盖取消零发送、开机组合/停车/恢复、首次发送后缺ACK换流超过原重试周期、确认期间换流、恢复停车取消延迟、停车重试、极性按住/回中/失焦/AUTO/关闭、过时输入取消、脚本命令及订阅解绑、ECU更新、向导汇总。

统一验收报告共 22/22 通过，其中本模块 `ProjectControlBehaviorTests` 12/12 通过，原有检查状态与发动机回读测试亦通过。报告保存于 `D:/project/drone-eps/tools/gcs-ui-prototype/v05-acceptance/unit/`；最终报告具体文件名由总体记录。

实测缺 ACK 时恢复发送延迟 **67 ms**；停车发送延迟（含测试自身原生确认框）**72 ms**；停车等待 ACK 时恢复发送延迟 **55 ms**。测试断言低于 1 秒；这些是本机内存发送端测量，不代表现场无线链路端到端延迟。

测试最初裸 STA 没有完整 WinForms 消息循环，出现异步续体与 Dispose/CreateHandle 并发；改为 Application.Run 的隐形测试宿主，并在进入用例、退出每个模态框后断言 WindowsFormsSynchronizationContext，之后本模块全部通过。没有用增加 sleep 或吞异常绕过失败。异步回调同时核对了真实关闭保护：Motor 使用 active/activation/IsDisposed；脚本写入和极性模式操作补齐关闭后不更新控件、等待释放后已关闭不发送模式。

最后 Motor 文字反馈保护（被新恢复/停车取消的旧操作不覆盖最新状态）与后续主题补修，均已纳入本节末所列最终43项组合构建和回归。四页向导真实截图随后发现动态舵机参数表白底白字，已在动态嵌入后复用现有 ThemeManager 设置行文字/背景；这项可读性修复与上述状态文字保护一起由主壳统一构建，并完成最终控制回归；重新截图与SITL任务验证由主壳记录，不把本模块离线通过等同于实物验证。

## 实物与协议边界

- 没有连接真实飞控或驱动实际舵面、发动机；真实通道极性、RC 覆盖接管行为、飞控 3 秒 TRIM 恢复保护须在后续设备验证。
- Lua 文件仅只读核对，未上传或修改。离线测试覆盖地面站命令和状态订阅，不等同于目标飞控脚本执行通过。
- ECU 解析只覆盖已提供的文本格式，接收时间用于操作员判断数据新旧，未凭未知阈值自动判通过。
- Engine COMMAND_ACK 表示命令应答，不能证明实际发动机启动或停止；同一 MAVLink 命令的 ACK 不携带 Start/Stop 参数身份。本界面始终要求结合飞控反馈和发动机实际状态核对。
- 未提交、未推送，未替换用户运行版本。主壳负责统一构建及最终集成；本模块未修改 MainV2、FlightData、参数表或 MAVLinkInterface。

## AUX 解锁旁路补修

本机选项元数据存在 41、81、153、154、165。依据固件实现而非按钮文字分类实际发送的 `DO_AUX_FUNCTION.param2`：

| 选项 | 低档0 | 中档1 | 高档2 |
| --- | --- | --- | --- |
| 41（旧版）/153 | 锁定确认 | 不改变解锁状态 | 完成检查并解锁确认 |
| 154 | 锁定确认 | 不主动解锁；已解锁时可启用 AirMode | 完成检查并解锁确认 |
| 81 | 不锁定 | 不锁定 | 锁定确认 |
| 165 | 电机急停，不冒充锁定 | 解除急停，不主动解锁 | 完成检查并解锁确认，固件同时解除急停 |

下拉选项保留；自定义低/高档值及原MP中档计算保持不变，禁用和handler复核均以实际发包值为准。确认前捕获目标与流，确认后和应答等待期间核对；取消或目标变化不向新流发送，finally恢复公共接收状态。其余AUX沿用原命令及参数。

官方固定版本来源：
- [Plane-4.7.2 通用 RC_Channel 实现](https://github.com/ArduPilot/ardupilot/blob/Plane-4.7.2/libraries/RC_Channel/RC_Channel.cpp)：`do_aux_function_armdisarm`、`DISARM`、`ARM_EMERGENCY_STOP`。
- [Plane-4.7.2 固定翼 RC_Channel 实现](https://github.com/ArduPilot/ardupilot/blob/Plane-4.7.2/ArduPlane/RC_Channel_Plane.cpp)：`ARMDISARM_AIRMODE`先调用通用arm/disarm，再判断已解锁状态设置AirMode。
- [ArduPlane-4.1.0 旧41实现](https://github.com/ArduPilot/ardupilot/blob/ArduPlane-4.1.0/libraries/RC_Channel/RC_Channel.cpp)：41对应ArmDisarm，HIGH解锁、MIDDLE不动作、LOW锁定。
- [Plane-4.7.2 Mission_Start处理](https://github.com/ArduPilot/ardupilot/blob/Plane-4.7.2/ArduPlane/GCS_MAVLink_Plane.cpp)：`MAV_CMD_MISSION_START`检查param1/2后只设置AUTO，没有主动arm调用，不扩大其处理。

新增AUX实际控件测试21个执行例：未核对禁用且直接调用handler零发送、改按钮实际档位仍受约束、取消零发送、确认后捕获正确option/position、154中档及165低中和无关AUX不误拦、确认中换流、首次发送后缺ACK换流超过原2秒重试周期。首轮11/21通过，10个应答成功路径超时。定位到测试帧生成器把COMMAND_ACK扩展字段写入MAVLink1帧，真实解析器因payload长度不等于v1最小长度而拒绝；订阅直接事件的旧测试未覆盖此解析。已仅将fixture应答改为合法MAVLink2并同时注入接收缓冲，生产AUX协议保持不变。修复后最小成功路径5/5通过，最终组合43/43通过（0失败，12.265秒），其中本模块控制/AUX共33个执行例全部通过。最终报告：`D:/project/drone-eps/tools/gcs-ui-prototype/v05-acceptance/unit/v05-final-43.trx`；最小复跑：同目录`v05-aux-ack-fixed.trx`。
