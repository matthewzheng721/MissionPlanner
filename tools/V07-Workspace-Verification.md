# V07 右栏功能迁移与验证

## 本轮完成

- 顶部移除飞行数据、飞行计划、初始设置、配置调试四个分页入口，保留连接和诊断工具。旧分页注册退出；原内部航点导航仍转右栏同一任务页，快捷键仅保留原 F5。
- 原 MAVFTP 控件内嵌右侧工具，目录树与文件列表上下排列；返回工具保留原校准、BIN日志、脚本入口。在线、参数齐、FTP能力条件沿用原规则；同连接切页保持原实例与原传输串行；断连、换流或换目标时取消原操作 token、清旧树并在原操作退出后销毁客户端，再按新目标建立。
- 新增并列“设置”页，重排原 ConfigPlanner 控件以适配窄栏；原即时保存、Activate/startup初始化规则保留。保留至家的距离及单位用途说明，不增加保存按钮。
- 原 RTK/GPS 注入在右工具可达，保留全部原控件，窄栏可横向滚动。工作台与 --rtk 命令行共用唯一原实例；切页只停 UI timer，不停止静态 RTK 后台连接。本轮没有连接 RTK 设备。
- 迁入设置、MAVFTP、RTK的入口复用原 password_protect/Password 校验；默认关闭不增加密码弹窗。原解锁、AUX、预飞检查及发动机确认逻辑未改。

## 构建产物

- 完整独立输出：D:/project/drone-eps/tools/gcs-ui-prototype/v07-workspace/build。
- 最终 MissionPlanner.exe：2026-10-31 09:42:44，本地时间；构建日志 build.log，退出码0。
- 使用该目录实际启动测试，运行库、资源和配置可用；未覆盖 V06、日常运行目录或用户原版 MP，未提交/推送。
- MainV2/MavFTPUI及工作台改动的 git diff --check 通过。

## 实际验证

证据目录：D:/project/drone-eps/tools/gcs-ui-prototype/v07-workspace/result。

- result.txt/status.txt：PASS。顶部四入口消失，原设置即时保存并切页恢复；RTK为同一实例，进入/离开只切换原UI timer；离线FTP显示连接条件。设置、RTK、FTP共六次实际原密码弹窗验证：取消阻止进入，正确密码进入；独立测试数据里的密码设置已恢复。
- 专用本机 ArduPlane SITL（5890，SIM_SPEEDUP存在，保持未解锁）读取原FTP根目录，同连接切页实例保持；原 UploadFile 上传31字节测试文件，原 GetFile 回读逐字一致，临时远端文件随后删除。
- 断连后旧目录/实例退出，重启专用SITL后同一MP重连建立新客户端。首轮重连因该SITL断开后退出而拒绝连接，第二轮明确重启自建SITL后通过；未修改生产通信规则掩盖该前置条件。
- directory-result.txt/directory-status.txt：最终产物 PASS。实际点击原目录节点后列表读出3项；树与列表可见且边界完全位于右栏；HUD/Quick/唯一地图同时可见；连接和断连均保持当前工具页。截图 ftp-directory.png。
- 其他截图：settings.png、tools.png、rtk.png、ftp-offline.png、ftp-online.png、ftp-disconnected.png。
- 首轮截图暴露的旧FTP占位叠显与动态返回按钮主题问题已修，最终截图显示原目录树/文件表和可读返回按钮。
- 第二轮PASS之后最终仅整理改动行空白/换行并重编，随后在最终产物补跑目录点击与边界检查；本轮未重复与改动无关的V06全46项测试。

## 边界与清理

- 没有连接真机，没有发送解锁、发动机、校准或真实RTK设备命令。FTP小文件只写专用本机SITL。
- RTK原控件较宽，完整功能通过右栏横向滚动可达；本轮没有改造其内部基站布局。地图测试使用本地缓存，缺失瓦片会显示原缓存提示，与本次入口迁移无关。
- 本轮创建的MP/SITL进程均已退出；未关闭用户进程（包括此前的30840）。
