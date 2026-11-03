# V09 HUD连接信息隐藏验证

按用户最终纠正，主HUD和双击独立HUD都隐藏右上角链路质量（条形、百分比、零质量红叉）以及时钟。

- 本轮最终只在 FlightData.ProjectWorkspace.cs 的主HUD和副本各设置 displayconninfo=false。临时新增的独立质量开关已完整撤销，HUD.cs与原逻辑无差异；原HUD控件默认行为保留。
- 中间数传状态栏保留，地图WindDir/DistanceBar及其他地图逻辑未改。
- 独立完整输出：D:/project/drone-eps/tools/gcs-ui-prototype/v09-workspace/build。
- 最终构建成功：MissionPlanner.exe 时间2026-10-31 10:00:54，MissionPlanner.Controls.dll 时间10:00:46（本地时间）；build.log退出码0，改动文件diff --check通过。
- 实际从最终输出离线启动验证。result/result.txt和result/status.txt为PASS：原HUD默认displayconninfo=true，项目主/副HUD均false；本地绘图赋99%与12:34:56后，两项均不显示；主/副窗口截图已检查；中间数传状态标签仍可见，独立窗关闭后主HUD/Quick/地图保持。
- 最终截图：result/main-hud.png、result/independent-hud.png。截图时间10:01:46及之后，覆盖此前仅隐藏质量的图片。
- 修改需求后旧测试残留displaylinkquality引用曾导致测试FAIL，已删除该旧分支；最终同一产物复跑PASS，未为测试增加生产功能。
- 没有连接真机或SITL，不发送设备命令，不重复全套测试。本轮自建测试进程已清理，用户V07进程31332保持运行；未覆盖日常版本，未提交推送。
