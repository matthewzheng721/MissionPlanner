# 只连接本机 SITL，仅读取电机参数，不校准、不写参、不刷固件。
import clr
from System import Action,Array,Object,EventArgs,Environment,Activator,Int32
from System.IO import File,Directory,Path
from System.Net.Sockets import TcpClient
from System.Threading import Thread
from System.Reflection import BindingFlags
from System.Windows.Forms import Button,FormWindowState
from System.Drawing import Size
from MissionPlanner.Comms import TcpSerial
from MissionPlanner.Utilities import Settings
output=Environment.GetEnvironmentVariable('MP_VISIBLE_OUTPUT') or Path.Combine(Settings.GetUserDataDirectory(),'initial-visible-test')
Directory.CreateDirectory(output)
report=[];flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
File.WriteAllText(Path.Combine(output,'status.txt'),'1')
def log(s):report.append(str(s));File.WriteAllLines(Path.Combine(output,'result.txt'),report)
def field(o,n):return o.GetType().GetField(n,flags).GetValue(o)
def ui(f):MainV2.Invoke(Action(f))
def pages():
    MainV2.MenuSetup_Click(None,EventArgs.Empty)
    return field(field(MainV2,'MyView').current.Control,'backstageView')
def unavailable(stage):
    back=pages();names=[p.LinkText for p in back.Pages]
    log(stage+'_names='+','.join(names))
    assert names==['本地固件安装','校准水平','大飞机罗盘校准','电机启动与恢复','RTK/GPS 注入']
    for p in list(back.Pages)[1:4]:
        assert p.PageType.Name=='InitialSetupUnavailable'
        back.ActivatePage(p)
        log(stage+'_'+p.LinkText+'='+field(p.Page,'status').Text)
        assert all(not c.Enabled for c in p.Page.Controls if isinstance(c,Button))
        assert ('正在读取' if stage=='loading' else '未连接') in field(p.Page,'status').Text
    return back

def capture():
    MainV2.Show();MainV2.WindowState=FormWindowState.Normal;MainV2.Size=Size(1100,760)
    back=unavailable('offline')
    MainV2.PerformLayout();MainV2.Refresh()
    method=MainV2.GetType().GetMethod('DrawToBitmap');types=method.GetParameters()
    bmp=Activator.CreateInstance(types[0].ParameterType,Array[Object]([Int32(MainV2.Width),Int32(MainV2.Height)]))
    rect=Activator.CreateInstance(types[1].ParameterType,Array[Object]([Int32(0),Int32(0),Int32(MainV2.Width),Int32(MainV2.Height)]))
    try:method.Invoke(MainV2,Array[Object]([bmp,rect]));bmp.Save(Path.Combine(output,'offline-five-pages.png'))
    finally:bmp.Dispose()
def connect():
    stream=TcpSerial();stream.client=TcpClient('127.0.0.1',5760);MAV.BaseStream=stream
    MainV2.doConnect(MAV,'preset','5760',True,True)
def motor_page():
    global motor
    back=pages();types=[p.PageType.Name for p in back.Pages]
    log('connected_types='+','.join(types))
    assert types==['ConfigFirmwareDisabled','ConfigAccelerometerCalibration','ConfigHWCompass2','ConfigProjectMotor','ConfigSerialInjectGPS']
    p=list(back.Pages)[0];back.ActivatePage(p)
    assert not field(p.Page,'but_bootloaderupdate').Visible
    p=list(back.Pages)[3];back.ActivatePage(p);motor=p.Page
try:
    ui(capture);ui(connect)
    assert not MAV.MAV.cs.armed
    def auto_connected():
        back=field(field(MainV2,'MyView').current.Control,'backstageView')
        types=[p.PageType.Name for p in back.Pages]
        log('automatic_after_connect='+','.join(types))
        assert types[1:4]==['ConfigAccelerometerCalibration','ConfigHWCompass2','ConfigProjectMotor']
    ui(auto_connected)
    # 只暂改本机缓存的“已报告参数数”模拟加载中，不向飞控写任何参数。
    reported=MAV.MAV.param.TotalReported
    try:
        MAV.MAV.param.TotalReported=MAV.MAV.param.TotalReceived+1
        ui(lambda:unavailable('loading'))
    finally:MAV.MAV.param.TotalReported=reported
    ui(motor_page)
    for i in range(120):
        if '回读' in field(motor,'status').Text:break
        Thread.Sleep(50)
    log('motor_read='+field(motor,'status').Text+' '+field(motor,'currentValue').Text)
    assert '回读' in field(motor,'status').Text
    ui(lambda:MainV2.doDisconnect(MAV))
    def auto_disconnected():
        back=field(field(MainV2,'MyView').current.Control,'backstageView')
        types=[p.PageType.Name for p in back.Pages]
        log('automatic_after_disconnect='+','.join(types))
        assert types[1:4]==['InitialSetupUnavailable']*3
    ui(auto_disconnected)
    ui(lambda:unavailable('disconnected'))
    log('PASS');File.WriteAllText(Path.Combine(output,'status.txt'),'0')
except Exception as ex:
    import traceback
    log('FAIL '+str(ex));log(traceback.format_exc())