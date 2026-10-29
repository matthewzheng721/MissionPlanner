# 开发版独立运行

先编译原项目，再在 PowerShell 执行：

```powershell
.\tools\Start-Development.ps1
```

默认复制构建产物到 `D:\Project\GCS-runtime\app`，启动参数为 `-datadir D:\Project\GCS-runtime\userdata`。可通过 `-RuntimeDirectory` 指定另一个绝对路径，也可通过 `-BuildDirectory` 选择 Release 产物。

数据布局：

- `userdata/config/`：本开发版设置与用户文件；新目录不导入旧版配置。
- `userdata/logs/`：默认遥测日志。
- `userdata/data/`：地图瓦片、高程、参数元数据和诊断日志。

首次准备和重新编译后的更新使用上述 PowerShell 脚本；日常双击 `D:\Project\GCS-runtime\启动开发版.cmd` 即可，勿直接双击 app 中的 exe（否则不带隔离参数）。再次启动沿用这些设置。`-datadir` 必须为绝对路径，不能与 `-config` 混用。无 `-datadir` 时保留原版路径和配置迁移行为。用户通过页面明确选择的任务、参数、日志等文件路径不受此模式限制。

默认启动脚本复制产物但不删除独立目录已有文件，更新前应关闭该目录的开发版进程。原版应用可继续使用原安装路径；数据隔离不等于网络端口隔离，两个实例同时监听相同端口仍会冲突。


## 飞行数据范围与 SITL 回归

本分支构建统一使用已确认的四主页面和飞行数据功能范围。`-datadir` 只控制数据位置，不切换功能版本。

`Test-FlightDataScope.py` 供 MP 内置 IronPython 的 `-script` 参数执行，不能直接使用系统 Python 运行。先启动本机 ArduPlane SITL，再在独立测试实例启动脚本：

```powershell
$env:MP_SCOPE_OUTPUT = 'D:\Project\MP-build-logs\scope-test'
$env:MP_SCOPE_PORT = '5760'
# 以下三项可选；设置后脚本会重启本机模拟器，验证同一地面站实例断线恢复。
$env:MP_SCOPE_SITL_EXE = 'D:\Project\MP-SITL\ArduPlane.exe'
$env:MP_SCOPE_SITL_WORKDIR = 'D:\Project\MP-SITL\plane'
$env:MP_SCOPE_SITL_ARGS = '--model plane --home -35.363261,149.165230,584,353 --speedup 1 --serial0 tcp:0 --defaults ../plane.parm'
$script = (Resolve-Path '.\tools\Test-FlightDataScope.py').Path
& 'D:\Project\GCS-runtime\app\MissionPlanner.exe' -datadir 'D:\Project\GCS-runtime\userdata' -script $script
```

路径均可替换。未指定输出目录时，保存到当前开发版配置目录下的 `scope-test`。仅连接 `127.0.0.1`，操作前还断言存在 SITL 参数 `SIM_SPEEDUP` 且未解锁。测试会切换 STABILIZE，并临时将模拟器巡航空速参数加 1 后恢复；不解锁、不起飞、不读取工程参数文件。不要将测试端口转发到真实飞控。

检查内容：顶部及飞行数据控件清单、HUD 独立窗口打开及回嵌、两个工具窗口打开；F5 完整参数读取、重复触发、同期间模式操作与参数写入回读、遥测数据新鲜度和界面调用延迟；刷新中断线提示；配置重启路径时再检查重新连接和刷新。参数写入使用 MAVLink API，不代表全部参数表页面的点击流程已验证。

输出 `flightdata-scope-result.txt`（最终 `PASS` 或 `FAIL`）、`flightdata-scope-status.txt`（`0` 成功，`1` 未完成或失败）、`flightdata-scope-telemetry.csv`。未提供模拟器重启路径时，结果明确记录 `SKIP reconnect`。查看状态文件应在脚本结束后进行。脚本不终止主应用；结束后关闭专用测试实例及本次启动的模拟器，重启模拟器 PID 写入结果文件。

该脚本记录实际延迟，不将本机 TCP 测量等同于低速数传或真实飞机上的性能结论。AUX 各选项作用、解锁、航点执行、日志下载/回放及 Message Interval 实际设置需要后续专项验证。

## 飞行计划页验证

`Test-FlightPlannerScope.py` 使用 MP 的 IronPython `-script` 入口。先启动本机固定翼 SITL（TCP 5760），设置可选的 `MP_PLANNER_OUTPUT` 输出目录，再把上述启动命令的脚本路径改成此文件。未设置时写入当前配置目录的 `planner-test`。脚本仅连接本机，并检查 SITL 参数和未解锁状态；会覆盖模拟器任务、切换 STABILIZE、临时修改 AIRSPEED_CRUISE 后恢复。

检查实际菜单和底图选项、180 条含不同高度参考系的航点编辑、JSON 保存加载及 SITL 上传下载逐项比对；传输期间执行飞行数据模式按钮与参数 API 写入，记录耗时和独立 UI 定时采样；调用测距和区域预下载入口。文件往返复用按钮的序列化/导入路径，不自动操作系统文件选择窗口。参数 API 测试不代表全部参数表的点击流程。

结果为 `result.txt` 的 PASS/FAIL 和 `status.txt` 的 0/1；`telemetry.csv` 记录脚本采样，`continuous.csv` 的 UI 定时采样覆盖同步参数写入区间（最后一列为 UI 定时器实际间隔，不是请求响应延迟）。脚本不退出应用，结束后关闭专用测试实例。Google 在线请求失败时会记录预下载不完整，不能据此声称地图下载成功；本地受控缓存测试另由 `PlannerPrefetchTests` 验证内存命中后落盘以及 CacheOnly 读取。TCP SITL 的时间结果不能替代低速数传测试。
## 卫星影像验证

`Test-SatelliteMaps.py` 由 MP 的 `-script` 执行，不连接飞控。可设置 `MP_MAP_OUTPUT` 保存结果，`MP_MAP_LAT`、`MP_MAP_LNG`、`MP_MAP_ZOOM` 指定在线对照瓦片的位置与层级（区域预下载样例固定在 Canberra）；先从 Google 选项启动，可测量首次切换必应的耗时。脚本检查两个选项、飞行数据同步，直接请求在线瓦片，验证必应区域预下载、实际菜单及关闭内存缓存后的磁盘读取，并保存必应选择。重启时设置 `MP_MAP_EXPECT_BING=1` 再运行，可断言启动记忆和两页同步。

输出 `result.txt`、`status.txt`（0 为选项/必应/缓存检查通过，1 为未完成或失败）及实际在线图片。Google 在线结果单独记录，Google 请求被拒绝时不会被标为在线成功，也不会自动替用户切换底图；脚本主动切换是测试步骤。测试结束保留必应选项，退出专用实例即可。`PlannerPrefetchTests` 对两种影像分别验证缓存落盘，并断言同一坐标不会读取另一种影像的缓存。

## 初始设置验证

`Test-InitialSetupScope.py` 由 MP 的 `-script` 入口执行。先启动本机固定翼 SITL（TCP 5760），使用独立 `-datadir`，可设置 `MP_INITIAL_OUTPUT` 保存结果。脚本检查固件页仅有本地入口、连接后五项菜单；F5 参数读取期间实际点击“启动电机→恢复”，检查只发送一次1300及一次2000，F5重建页面后再次核对原位反馈。另以受控400ms共享写锁占用验证按钮不会等待传输锁，再检查缺参和断线提示。脚本只连接本机，断言SITL参数及未解锁，结束前恢复模拟器原SERVO5_TRIM。

脚本执行模拟器水平校准、大飞机罗盘校准，并以本机TCP差分源生成RTCM1005，验证原解析及MAVLink转发。此测试不代表实物刷写、串口基站、UDP/NTRIP接入或RTK Fixed已验证。未选择任何固件或连接真实设备。`ProjectMotorReadbackTests` 使用真实编码MAVLink回包，验证较早的1300不能确认较新的2000，并覆盖PARAM_FLOAT编码。

输出 `result.txt`、`status.txt`（0成功、1失败/未完成）及 `telemetry.csv`（UI定时器间隔与遥测包龄）。可选 `MP_INITIAL_CAPTURE=1` 会在2000回读后输出主窗口 `handle.txt` 并等待最多90秒，外部完成截图检查后创建同目录 `capture.done` 继续；普通复跑不要设置此变量。结束后关闭测试实例和本轮模拟器。测试使用本机TCP，不能据此保证低速数传耗时或电机实际动作。

## 配置/调试验证

`Test-ConfigScope.py` 由 MP 的 IronPython `-script` 执行，先启动本机固定翼 SITL（TCP 5760），使用独立 `-datadir`。可设置 `MP_CONFIG_OUTPUT` 输出目录，默认当前配置目录的 `config-test`。脚本断言模拟器未解锁，检查五类配置页、Planner 可见设置和实际原速率；不会把用户曾用的 10/3/2/2/1 自动写成当前值。

脚本使用 MAVFTP 页原上传路径传送 1MB 测试文件并检查远端 CRC，同时打开 Inspector，切回飞行数据用模式按钮切换 STABILIZE，通过全部参数表实际单元格编辑及写入按钮修改 AIRSPEED_CRUISE 并回读。测试自动点击原确认窗的“是”，然后恢复原参数。文件保存在模拟器的根目录 `config-transfer.bin`，不上传真实设备。Message Interval 实际设置 ATTITUDE 为 5Hz，并测量切换 Planner 姿态数据组至3Hz后的消息频率，最后恢复原速率。

`result.txt` 的 PASS/FAIL 与 `status.txt` 的0/1说明完成状态。`health.csv` 记录阶段、FTP是否仍运行、遥测包龄、UI定时器实际间隔和模式；`parameter-events.csv` 记录 PARAM_SET 与 PARAM_VALUE 时间，可区分参数表加载/确认停留与实际链路应答时间。本机TCP结果不代表低速数传、外部机场/ADSB/空域服务已经验收。

可选 `MP_CONFIG_CAPTURE=1` 在同一界面调用内核对当前页面，使用实际控件 DrawToBitmap 保存 Planner、全部参数表及 MAVFTP 截图。须肉眼核对生成图片，不把名称当作页面正确性的证明。`MP_CONFIG_TOOLS_ONLY=1` 仅复测消息频率及恢复，结果会注明 tools_only，不代表重跑完整FTP/写参测试。结束后关闭本轮专用开发版及模拟器。
