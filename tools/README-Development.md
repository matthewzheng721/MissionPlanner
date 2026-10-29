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
