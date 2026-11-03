# V06 原生工作台整合与验证

## 本轮完成

1. 原 HUD 隐藏电池、GPS、EKF、VIBE，读数留在 HUD 与 Quick 之间状态带；保留姿态、空速、高度、航向、模式。独立 HUD 副本继承同样显示设置。
2. 动作页直接显示三个命令行、AUTO／解锁和原 AUX 控件，不再套第二层标签。AUX 原检查、确认和换流保护保留。发动机按钮仅保留底栏一组，原控制实例继续提供操作反馈。
3. FlightData.gMapControl1 是唯一中央地图。原 FlightPlanner.MainMap 引用接入该地图；原 Commands、任务模型、文件、校验、上传下载直接嵌入右栏。旧独立航点窗口入口和实现退出；顶部“飞行计划”重定向同一内联页。
4. 地图加点采用用户确认的 B：航点页明确开启后可连续单击；关闭只浏览，拖拽／滚轮不误加点，切离规划关闭开关。每次按下重新命中已有点，避免不离开标记连续点击产生重复点。原飞行地图右键菜单及飞至／引导鼠标事件解绑。
5. 规划任务 WPOverlay 与飞控当前任务 ProjectVehicleMission 分开，实时刷新不清除未上传编辑。原规划器重复绘飞机的定时器退出，共图实时飞机由 FlightData 维护。
6. 右工具去掉简单动作、状态、TLOG回放入口，增加水平／大飞机罗盘两个原校准页面入口。BIN日志下载、BIN/LOG查看、原脚本工具入口保留；原脚本窗口关闭归还控件，可重复打开，不自动运行脚本。
7. 消息位于左 Quick 下，默认收起可展开。左侧有始终可见“更多监视”：全部状态、原实时曲线、ECU三独立窗口。主页面切换后状态和曲线仍刷新；关闭曲线窗口停止采样。Quick 沿原控件自动字号机制按实际列宽显示。
8. 保留顶部工具中的 MAVLink Inspector／Message Interval；MAVFTP 和 Planner 设置继续位于顶部“配置调试”（MAVFTP沿原连接能力条件显示）。自动 TLOG 记录代码未改，移除的是用户回放加载界面。

## 构建与运行位置

- 完整隔离输出：`D:/project/drone-eps/tools/gcs-ui-prototype/v05-inline/build`。
- 最终 `MissionPlanner.exe` 时间：2026-10-31 **09:30:49**；构建日志完成时间 **09:30:52**（本地时间）。
- 最终构建日志：`D:/project/drone-eps/tools/gcs-ui-prototype/v06-final-build.log`，退出码 0。
- 此输出包含主程序、运行库、配置和资源；已直接从该目录启动并连接专用本机 SITL，未从旧 bin 启动代替验证。
- 原开发 bin 的运行库被已有 MP 30840 占用，首次构建复制失败后改用独立 OutDir；未关闭该用户进程，未覆盖日常运行目录。用户启动入口及独立日常数据目录由总体窗口维护。

## 实际验证

证据根目录：`D:/project/drone-eps/tools/gcs-ui-prototype/v05-inline`。

- `unit/v06-related.trx`：**46／46 通过，0 失败**，13.7542 秒。包含原 43 项控制／AUX／检查／电机回读／摇杆／任务文件回归，加 3 项监视与校准宿主测试。
- 46 项运行后只做了左入口位置、任务页主题／显示列顺序、恢复旧日志／脚本入口和换行整理，未改变协议或检查逻辑；这些最后改动按以下局部 UI 用例复验，没有无差别重跑 46 项。
- `result/result.txt`、`result/status.txt`：完整 SITL 验收 PASS。实际唯一地图和原表可见；关闭加点不增行、开启连续3点、拖拽与滚轮不加点、切离关闭、已有点连续点击不重复；30项任务上传／下载逐项一致；等遥测刷新后规划和飞控任务图层同时保留。原曲线采样实测从 3 增至 26。
- `result/final-ui-result.txt`、`result/final-ui-status.txt`：局部复验 PASS。左“更多监视”可见可点；顶部“飞行计划”共图；切到其他主页面后曲线定时器继续运行且实际采样继续增加；返回后30项任务只读回显；水平和罗盘窗口只打开／关闭，无校准命令。
- `result/tools-result.txt`、`result/tools-status.txt`：原日志窗打开关闭，脚本选择入口实际可见、同一原控件关闭重开不销毁。未选择或运行用户脚本。
- 截图：`actions-inline.png`、`mission-readback.png`、`tools-inline.png`、`monitor-menu.png`、`status-window.png`、`curve-window.png`、`ecu-window.png`、`level-window.png`、`compass-window.png`、`bin-log-window.png`、`script-window.png`，均位于 result；独立窗口截取自身。

## 复现中修正

- 动态内联任务容器未套主题造成白底白字，已应用原 ThemeManager；显示列优先命令／高度／纬度／经度，仅改变 DisplayIndex，不改变原列索引与校验。
- 状态带容器缺百分比列导致原 Quick 超出实际宽度，已修布局，保留原 Quick 自动字号。
- 首轮地图测试未设默认高度触发原输入提示；用例明确选择80，不改变原默认高度校验。
- 脚本工具用例首次误要求“运行”按钮在未选择脚本时可见。原资源本就隐藏该按钮，选择文件后才显示；测试改核实际“选择脚本”入口与重开生命周期，没有放松生产显隐规则。
- 本轮误隐藏的日志与脚本入口已恢复；未增加 CSV 等额外功能。

## 范围边界

- 任务参数较多时保留原表水平滚动和可调右栏，未另建第二套任务数据或替换原飞控协议。
- 仿真启动入口此前即未完成，本轮未新增；验证使用独立本机 SITL。地图使用本地缓存，Google外网／高精地图、低速数传、掉包和长期性能未验收。
- 校准未对实物执行；专用ECU与脚本硬件行为没有由通用SITL冒充验证。
- 未提交、未推送，未覆盖 Retained-Feature-Verification.md、用户 E:/MP 或日常 runtime。
