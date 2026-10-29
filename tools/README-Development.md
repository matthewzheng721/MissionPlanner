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
