# V08 诊断入口迁移验证

本轮仅修改 MainV2.cs 与 FlightData.ProjectWorkspace.cs：移除顶部“工具”菜单；右侧工具增加“诊断”分组及 MAVLink Inspector、Message Interval 两按钮。Inspector 原打开处理提取后复用，Message Interval 直接调用原 ShowMessageInterval；没有新传输逻辑、协议或窗口实现，顶部连接保留。

- 独立完整输出：D:/project/drone-eps/tools/gcs-ui-prototype/v08-workspace/build。
- 最终 MissionPlanner.exe 时间：2026-10-31 09:54:37（本地时间）。构建日志 build.log，退出码0；git diff --check 通过。
- 从此输出实际启动并离线验证，result/result.txt 与 result/status.txt 为 PASS。
- 顶部无工具菜单、连接仍可见；右栏两诊断按钮均可见、可点且边界位于右栏内；点击分别打开原 MAVLinkInspector 和 Message Interval 窗口，关闭后正常销毁。
- HUD、Quick、唯一地图全程可见。未连接真机或SITL，未点击消息频率 Set / Set All，不发送设备命令；未重复无关整套测试。
- 截图：result/tools-diagnostics.png、result/mavlink-inspector.png、result/message-interval.png。
- 本轮测试进程35200已清理。用户V07进程31332保持运行，未覆盖V07或日常版，未提交推送。
