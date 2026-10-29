# 本机 SITL 验证：不解锁、不起飞，仅切换地面模式并读写模拟器参数。
import clr
from System import Action, DateTime, Array, Object, EventArgs, Environment
from System.IO import File, Directory, Path
from System.Net.Sockets import TcpClient
from System.Threading import Thread
from System.Diagnostics import Stopwatch, Process, ProcessStartInfo, ProcessWindowStyle
from System.Reflection import BindingFlags
from System.Windows.Forms import Application, Keys, Message, FormWindowState
from MissionPlanner.Comms import TcpSerial
# 由 MissionPlanner.exe -script 执行；仅连接本机 SITL。
# 输出目录与重启模拟器路径通过环境变量配置，详见 README-Development.md。
from MissionPlanner.Utilities import Settings
output_dir = Environment.GetEnvironmentVariable('MP_SCOPE_OUTPUT') or Path.Combine(Settings.GetUserDataDirectory(), 'scope-test')
Directory.CreateDirectory(output_dir)
out = Path.Combine(output_dir, 'flightdata-scope')
# 启动即重置退出状态，避免上次 PASS 被误读为本次结果。
File.WriteAllText(out+'-status.txt','1')
port = int(Environment.GetEnvironmentVariable('MP_SCOPE_PORT') or '5760')
original = None
param = None
report=[]
flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic

def log(s):
    report.append(str(s)); File.WriteAllLines(out+'-result.txt',report)
def field(obj,name): return obj.GetType().GetField(name,flags).GetValue(obj)
def call(obj,name,args): return obj.GetType().GetMethod(name,flags).Invoke(obj,Array[Object](args))
def ui(fn): MainV2.Invoke(Action(fn))
def pv(name):
    return next(float(p.Value) for p in MAV.MAV.param if p.Name==name)

def connect():
    stream=TcpSerial();stream.client=TcpClient('127.0.0.1',port);MAV.BaseStream=stream
    MainV2.doConnect(MAV,'preset',str(port),True,True)
def structure():
    MainV2.Show();MainV2.WindowState=FormWindowState.Normal;MainV2.BringToFront()
    menu=field(MainV2,'MainMenu');tabs=field(FlightData,'tabControlactions')
    log('top='+','.join([x.Name+':'+x.Text for x in menu.Items if x.Visible]))
    actual_tabs=[x.Name for x in tabs.TabPages]
    log('tabs='+','.join(actual_tabs))
    assert actual_tabs==['tabQuick','tabActions','tabPagemessages','tabActionsSimple','tabStatus','tabAuxFunction','tabScripts','tabTLogs','tablogbrowse']
    assert not menu.Items.ContainsKey('MenuSimulation') and not menu.Items.ContainsKey('MenuHelp')
    for name in ['tabActions','tabActionsSimple','tabTLogs','tablogbrowse']:
        tab=field(FlightData,name);tabs.SelectedTab=tab
        def visible(c):
            names=[]
            for child in c.Controls:
                if child.Visible:
                    if child.Controls.Count>0:names.extend(visible(child))
                    else:names.append(child.Name)
            return names
        actual=visible(tab)
        log(name+'='+','.join(actual))
        if name=='tabActions':
            assert set(actual)==set(['CMB_action','BUTactiondo','CMB_setwp','BUT_ARM','BUT_setwp','CMB_modes','BUT_quickauto','BUT_setmode'])
        elif name=='tabActionsSimple':assert actual==['myButton3']
        elif name=='tablogbrowse':assert set(actual)==set(['BUT_DFMavlink','BUT_logbrowse','but_bintolog'])
        elif name=='tabTLogs':assert 'BUT_log2kml' not in actual
    tabs.SelectedTab=field(FlightData,'tabQuick')
    log('hudmenu='+','.join([x.Name for x in field(FlightData,'contextMenuStripHud').Items]))
    log('mapcontext='+str(field(FlightData,'gMapControl1').ContextMenuStrip))
    assert field(FlightData,'gMapControl1').ContextMenuStrip is None
    # 截图另在窗口绘制完成后进行。
    call(FlightData,'hud1_DoubleClick',[None,EventArgs.Empty])
    forms=[f for f in Application.OpenForms if f.Text=='HUD Dropout'];log('hud_detached='+str(len(forms)))
    for f in forms:f.Close()
    assert len(forms)==1
    log('hud_restored='+str(field(FlightData,'hud1').Parent==field(FlightData,'SubMainLeft').Panel1))
    assert field(FlightData,'hud1').Parent==field(FlightData,'SubMainLeft').Panel1
    tools=menu.Items['ProjectTools']
    for item in tools.DropDownItems:
        before=set([f.Handle.ToInt64() for f in Application.OpenForms])
        item.PerformClick()
        forms=[f for f in Application.OpenForms if f.Handle.ToInt64() not in before]
        log('tool '+item.Text+'='+','.join([f.Text for f in forms]))
        assert len(forms)==1
        for f in forms:f.Close()

def refresh():
    call(MainV2,'ProcessCmdKey',[Message(),Keys.F5])
def mode():
    tabs=field(FlightData,'tabControlactions');tabs.SelectedTab=field(FlightData,'tabActions')
    combo=field(FlightData,'CMB_modes');combo.Text='STABILIZE';field(FlightData,'BUT_setmode').PerformClick()
try:
    ui(connect);Thread.Sleep(3000);ui(structure)
    log('connected='+str(MAV.BaseStream.IsOpen)+' params='+str(MAV.MAV.param.Count)+' mode='+MAV.MAV.cs.mode+' armed='+str(MAV.MAV.cs.armed))
    assert MAV.MAV.param.ContainsKey('SIM_SPEEDUP'), 'Only local SITL is allowed'
    assert not MAV.MAV.cs.armed
    original=pv('TRIM_ARSPD_CM') if MAV.MAV.param.ContainsKey('TRIM_ARSPD_CM') else pv('AIRSPEED_CRUISE')
    param='TRIM_ARSPD_CM' if MAV.MAV.param.ContainsKey('TRIM_ARSPD_CM') else 'AIRSPEED_CRUISE'
    rows=['utc,packet_age_ms,ui_latency_ms,refreshing,mode,param']
    refresh_watch=Stopwatch.StartNew()
    ui(refresh);ui(refresh)
    completed_ms=None
    for i in range(700):
        watch=Stopwatch.StartNew();ui(lambda:None);watch.Stop()
        rows.append('{},{},{},{},{},{}'.format(DateTime.UtcNow.ToString('o'),(DateTime.UtcNow-MAV.MAV.lastvalidpacket).TotalMilliseconds,watch.ElapsedMilliseconds,field(MainV2,'refreshingParameters'),MAV.MAV.cs.mode,pv(param)))
        if completed_ms is None and not field(MainV2,'refreshingParameters'):completed_ms=refresh_watch.ElapsedMilliseconds
        if i==1:
            watch=Stopwatch.StartNew();ui(mode);watch.Stop();log('mode_control_ms='+str(watch.ElapsedMilliseconds))
        if i==2:
            watch=Stopwatch.StartNew();ok=MAV.setParam(param,original+1);watch.Stop();log('write_during_f5='+str(ok)+' ms='+str(watch.ElapsedMilliseconds));assert ok
        if i%10==0:File.WriteAllLines(out+'-telemetry.csv',rows)
        if i>120 and not field(MainV2,'refreshingParameters'):break
        Thread.Sleep(100)
    File.WriteAllLines(out+'-telemetry.csv',rows)
    log('refresh_status='+field(MainV2,'parameterRefreshStatus').Text)
    log('refresh_complete_ms='+str(completed_ms))
    assert field(MainV2,'parameterRefreshStatus').Text==u'参数读取完成'
    assert MAV.MAV.cs.mode.upper()=='STABILIZE' and not MAV.MAV.cs.armed
    assert abs(pv(param)-(original+1))<0.01
    log('final_mode='+MAV.MAV.cs.mode+' armed='+str(MAV.MAV.cs.armed)+' param='+str(pv(param)))
    readback=MAV.GetParam(param)
    log('explicit_param_read='+str(readback))
    assert abs(readback-(original+1))<0.01
    MAV.setParam(param,original)
    ui(refresh);Thread.Sleep(150);MAV.BaseStream.Close()
    for i in range(30):
        if not field(MainV2,'refreshingParameters'):break
        Thread.Sleep(100)
    log('disconnect_status='+field(MainV2,'parameterRefreshStatus').Text)
    assert not field(MainV2,'refreshingParameters')
    assert u'失败' in field(MainV2,'parameterRefreshStatus').Text
    # 可选：指定同一本机模拟器后，在当前地面站实例内验证重新连接及完整刷新。
    sitl_exe=Environment.GetEnvironmentVariable('MP_SCOPE_SITL_EXE')
    if sitl_exe:
        Thread.Sleep(1000)
        info=ProcessStartInfo(sitl_exe, Environment.GetEnvironmentVariable('MP_SCOPE_SITL_ARGS') or '')
        info.WorkingDirectory=Environment.GetEnvironmentVariable('MP_SCOPE_SITL_WORKDIR') or Path.GetDirectoryName(sitl_exe)
        info.UseShellExecute=False;info.CreateNoWindow=True;info.WindowStyle=ProcessWindowStyle.Hidden
        restarted=Process.Start(info)
        log('restarted_sitl_pid='+str(restarted.Id))
        Thread.Sleep(3000);ui(connect);ui(refresh)
        for i in range(650):
            if not field(MainV2,'refreshingParameters'):break
            Thread.Sleep(100)
        log('reconnect_refresh='+field(MainV2,'parameterRefreshStatus').Text)
        assert field(MainV2,'parameterRefreshStatus').Text==u'参数读取完成'
    else:
        log('SKIP reconnect: MP_SCOPE_SITL_EXE not set')
    log('PASS')
    File.WriteAllText(out+'-status.txt','0')
except Exception as ex:
    import traceback
    log('FAIL '+traceback.format_exc())
    File.WriteAllText(out+'-status.txt','1')



finally:
    # 仅恢复本脚本在模拟器中改变的参数，不改真实飞控或工程参数文件。
    if original is not None and param is not None and MAV.BaseStream.IsOpen:
        try:
            MAV.setParam(param,original)
        except Exception as ex:
            log('FAIL restore: '+str(ex))
            File.WriteAllText(out+'-status.txt','1')
