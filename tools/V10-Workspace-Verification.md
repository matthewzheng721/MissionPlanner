# V10 原生舵机检查交付与验证

日期：2026-10-31。最终程序 `build/MissionPlanner.exe` 本地时间 11:16:56。

## 本轮实现

- 舵机极性页分为「FBWA 稳定纠偏」「MANUAL 操纵方向」两标签，每次只展示一组四舵面图。S1/S2 反向安装、S3/S4 正向安装；S1/S4 混控，S2/S3 俯仰。
- FBWA 读取实际姿态；有新鲜飞控目标时按目标与实际姿态之差演示。无目标回报时，明确显示按水平 0° 示意。机体被抬头与主动发出抬头指令的含义分开。
- MANUAL 按住发送，松开回 RC 中位；按 RC 映射、范围、中位与 REVERSED 解释操纵方向。另一操纵轴保持中位，未操作油门通道。关闭、失焦、切标签、外部模式变化和连接变化释放覆盖；已排队的过时操纵不补发。
- 只有当前标签、对应实际模式可勾选本组核对。两子组都核对才完成原第 5 步；旧父勾选不能绕过，重连/换目标沿用清除规则。
- 舵机测试按原 `sd_pwm_test.lua` v3 的往返、阶跃、正弦，逐路 MIN/MAX、FRAC 与 TRIM 规则展示预期 PWM 和方向；到时 TRIM 保持 1 秒再释放。预览播放/暂停/复位均不发飞控命令。
- 编辑显示待写入预览；整组写入收到本次 PARAM_VALUE 后显示已确认参数。逐项串行确认，不接管 MP 读取线程；超时或部分失败不标整组成功，重连后不向新连接补发旧参数。
- 动画不代表位置测量；真实 SERVO_OUTPUT_RAW 回报独立展示。姿态、目标、输出分别按报文时间检查 3 秒时效，停更显示待数据/缺失值。
- 检查窗口采用局部深色/灰蓝样式和中文字体，初始尺寸限制在工作区内。900×720 窗口中检查/导航可达；参数表滚动至最后一项，不覆盖写入或脚本按钮。

## 验证结果

1. 最终构建成功。`build.log`、`test-build.log` 保留日志；存在上游依赖/旧代码警告，未借此扩大本轮修改。
2. 本轮相关离线测试 **50/50 通过**。覆盖原有解锁/发动机/辅助动作保护、双极性记录、动作回中/释放、断连取消、三种波形、反向安装、RC 反向、参数整组回报、部分写入超时、重连禁止旧重试等。见 `tests.log`、`test-results/v10-controls.trx`。
3. 自建 localhost:5900 的 ArduPlane SITL，实际读取 1450 项参数并加载 SDT v3；通过页面切 FBWA、MANUAL，按住抬头及右滚均收到 RC 对应通道 1575，松开收到中位，外部切 FBWA 后覆盖释放。
4. 实际页面将 SDT_MODE 改为 2，收到整组写入回报，预览更新并播放/暂停，随后恢复原值并确认。没有启动脚本真实输出，也没有解锁或发动机操作。
5. AUTO 按钮正确发出模式请求。本次空任务 SITL 回报 `Mission complete, changing mode to RTL`，页面按实际回报显示 RTL；**没有把请求发送当作切入 AUTO 成功**。
6. 最后一次 UI 验证对参数表与按钮矩形无重叠、最后一行可滚动显示、导航在窗口内作了断言；断联后两子核对均清除。最终 `result/result.txt` 和 `result/status.txt` 为 PASS/0。

## 入口及范围

- 启动：`D:/project/drone-eps/tools/gcs-ui-prototype/启动V10开发版.cmd`。
- 独立程序：同目录 `v10-workspace/build/MissionPlanner.exe`。
- 用户设置：`D:/project/drone-eps/data/gcs-v10`，测试数据独立位于 `v10-workspace/userdata`。
- 最终图片：`result/fbwa-check.png`、`result/manual-check-held.png`、`result/manual-check-centered.png`、`result/servo-test.png`、`result/servo-preview.png`。
- 未改 B2 固件或原 Lua，没有真机测试、没有实际舵角标定，也未提交或推送 Git。V09 及更早独立包保留。

关键源码：`Controls/ProjectPolarityPanel.cs`、`Controls/ProjectServoTestPanel.cs`、`Controls/ProjectSurfaceView.cs`、`Controls/ProjectPreflightWizard.cs`、`Utilities/ProjectServoPreview.cs`、`Utilities/ProjectPreflight.cs`，以及对应测试。
