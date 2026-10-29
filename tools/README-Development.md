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
