# V06 独立监视与机上校准模块

## 文件与集成接口

- `Controls/ProjectMonitoringWindows.cs`：`new ProjectMonitoringWindows(owner, statusContent, curveContent, statusVisibility, curveVisibility)`；公开 `ShowMenu()`、`ShowStatus()`、`ShowCurve()`、`ShowEcu()`、`Dispose()`。
- `Controls/ProjectEcuMonitorPanel.cs`：独立 ECU 只读展示，公开 `RefreshValues()` 供刷新与验证。
- `Controls/ProjectOnboardCalibrationWindows.cs`：`new ProjectOnboardCalibrationWindows(owner)`；公开 `ShowMenu()`、`ShowLevel()`、`ShowCompass()`、`Dispose()`。
- `MissionPlannerTests/Utilities/ProjectMonitoringTests.cs`：三个离线生命周期与数据语义测试。

监视入口窗口含全部状态、实时曲线、ECU 信息三个按钮。各窗可并存、再次点击复用现有窗。状态与曲线窗关闭时先移出原控件再销毁宿主，重开继续使用同一控件；关闭“更多监视”入口不关闭已打开的监视窗。

主壳传入含原 `tabStatus` 的轻量 TabControl 和原 `zg1`。回调负责原状态刷新、原曲线采样/绘制定时器；独立宿主不另造数据或开第二采样循环。主壳还须处理 FlightData 被切出时独立窗继续刷新，及关闭独立窗后的正常停止。拥有者结束时应 Dispose 宿主。

## ECU 字段边界

- 油门量明确标为 EFI 节气门位置，只取已收到 `EFI_STATUS.throttle_position`，不取 engine_load。
- 转速取同一 EFI 报文的 rpm；保留接收时间。
- 最高缸温取已确认的 ECU 四缸文本温度最大值，保留接收时间；沿用 `ProjectEcuStatus`，未修改原检查逻辑。
- 润滑油压与 ECU 电压映射未知，显示“—”；不把 fuel_pressure 或 ignition_voltage 冒充这两个数据。
- 报故格式未确认，明确说明并显示 ECU 原始文字，不将没有报文称作无故障。
- 监视只用 `getPacketLast` 和原消息记录，不消费原接收队列、不请求额外遥测、不写参数；断开连接或回放时清空实时数值。

## 机上校准

仅承载现有精简 `ConfigAccelerometerCalibration` 和 `ConfigHWCompass2`，分别提供清楚的“校准水平”“大飞机罗盘校准”入口。仅重排仍可见的原按钮/说明以适配窗口，隐藏的其他校准不开放。首次显示调用 Activate，关闭调用 Deactivate 后释放；关闭重开创建新的原页面，避免沿用已关闭页面反馈。断开或回放状态不启用校准控件；未复制校准命令或替代原飞控保护。

## 验证与交付边界

新增离线测试覆盖窗口共存/复用/关闭重开、原控件不被宿主销毁、监视零发送、EFI 精确字段及不消费队列、断链清空、两个校准各仅一个可见原按钮与打开时零发送。

本模块不自行构建。唯一构建者 gcs_ui_v05 负责统一编译、执行三个测试，并用实际 MP/SITL 验证原全部状态持续更新、原曲线持续采样/绘制/字段选择、切主页面时不中断、三窗共存与重开，以及截图核查。本文暂不将源码测试列为已经通过。

保留所有既有未提交修改；未改 FlightData/MainV2/FlightPlanner，未改既有发动机/检查/AUX规则，未连接实物、未提交或推送。
