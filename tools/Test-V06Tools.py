# 本轮仅连接隔离的本机 ArduPlane SITL；不解锁，不发送发动机或校准命令。
import clr
from System import Action,Array,Object,EventArgs,Environment,Activator,Int32,UInt16,Byte
from System.IO import File,Directory,Path
from System.Reflection import BindingFlags
from System.Net.Sockets import TcpClient
from System.Threading import Thread
from System.Windows.Forms import Application,FormWindowState,MouseEventArgs,MouseButtons
from System.Drawing import Size
from MissionPlanner.Comms import TcpSerial
from MissionPlanner.Utilities import Locationwp,Settings,MissionFile
from System.Collections.Generic import List
from GMap.NET import GMaps,AccessMode
flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
output=Environment.GetEnvironmentVariable('MP_V05_OUTPUT')
Directory.CreateDirectory(output);report=[]
File.WriteAllText(Path.Combine(output,'tools-status.txt'),'1')
def log(value):
    report.append(str(value));File.WriteAllLines(Path.Combine(output,'tools-result.txt'),report)
def field(obj,name):return obj.GetType().GetField(name,flags).GetValue(obj)
def call(obj,name,args=[]):return obj.GetType().GetMethod(name,flags).Invoke(obj,Array[Object](args))
def ui(fn):return MainV2.Invoke(Action(fn))
def waitfor(predicate,label,seconds=40):
    for i in range(seconds*10):
        result=[];ui(lambda:result.append(predicate()))
        if result[0]:return
        Thread.Sleep(100)
    raise Exception('等待超时 '+label)
def capture(form,name):
    form.PerformLayout();form.Refresh()
    method=form.GetType().GetMethod('DrawToBitmap');types=method.GetParameters()
    bmp=Activator.CreateInstance(types[0].ParameterType,Array[Object]([Int32(form.Width),Int32(form.Height)]))
    rect=Activator.CreateInstance(types[1].ParameterType,Array[Object]([Int32(0),Int32(0),Int32(form.Width),Int32(form.Height)]))
    try:method.Invoke(form,Array[Object]([bmp,rect]));bmp.Save(Path.Combine(output,name+'.png'))
    finally:bmp.Dispose()
fp=MainV2.FlightPlanner

def setup():
    MainV2.Show();MainV2.WindowState=FormWindowState.Normal;MainV2.Size=Size(1440,900)
    field(MainV2,'MyView').ShowScreen('FlightData')
    field(FlightData,'hud1').opengl=False
    FlightData.SelectProjectMission()
    assert Object.ReferenceEquals(fp.MainMap,field(FlightData,'gMapControl1'))
    assert fp.Visible and fp.Commands.Visible and fp.MainMap.Visible
    assert fp.Parent==field(FlightData,'projectMissionTab')
    assert fp.MainMap.ContextMenuStrip is None
    assert not any(f.Text=='航点编辑' for f in Application.OpenForms)
    for name in ['batteryon','batteryon2','displaygps','displayekf','displayvibe']:assert not getattr(field(FlightData,'hud1'),name),name
    for name in ['displayheading','displayspeed','displayalt','displayrollpitch']:assert getattr(field(FlightData,'hud1'),name),name
    call(FlightData,'hud1_DoubleClick',[None,EventArgs.Empty])
    duplicate=field(FlightData,'projectHudCopy')
    for name in ['batteryon','batteryon2','displaygps','displayekf','displayvibe']:assert not getattr(duplicate,name)
    field(FlightData,'projectHudWindow').Close()
    assert not field(FlightData,'txt_messagebox').Visible
    field(FlightData,'projectMessageToggle').PerformClick();assert field(FlightData,'txt_messagebox').Visible
    field(FlightData,'projectMessageToggle').PerformClick();assert not field(FlightData,'txt_messagebox').Visible
    field(FlightData,'projectOperationTabs').SelectedIndex=0
    assert field(FlightData,'auxOptions1').Visible and field(FlightData,'auxOptions2').Visible
    assert not field(FlightData,'tabTLogs').Visible and not field(FlightData,'tabActionsSimple').Visible
    assert FlightData.Controls.Find('ProjectMonitorEntry',True)[0].Visible
    quick=field(FlightData,'tableLayoutPanelQuick')
    assert quick.Width<=quick.Parent.ClientSize.Width
    for item in quick.Controls: assert item.Right<=quick.ClientSize.Width
    capture(MainV2,'actions-inline')
    FlightData.SelectProjectMission()
    log('唯一原地图/右栏原任务表可见，HUD健康项移除，直接AUX、消息折叠、旧回放入口退出通过')

def connect():
    stream=TcpSerial();stream.client=TcpClient('127.0.0.1',5890);MAV.BaseStream=stream
    MainV2.doConnect(MAV,'preset','5890',True,True)

def mouse(kind,x,y,delta=0):
    event=MouseEventArgs(MouseButtons.Left,1,x,y,delta)
    call(fp.MainMap,'OnMouse'+kind,[event])
def click(x,y):mouse('Down',x,y);mouse('Up',x,y)
def gestures():
    fp.Commands.Rows.Clear();fp.MainMap.Zoom=16
    field(fp,"TXT_DefaultAlt").Text="80"
    fp.updateHome()
    add=field(fp,'projectMapAddPoints')
    assert not add.Checked
    click(130,120);assert fp.Commands.Rows.Count==0
    add.Checked=True
    for x,y in [(130,120),(200,150),(260,200)]:click(x,y)
    assert fp.Commands.Rows.Count==3,fp.Commands.Rows.Count
    marker=next(m for m in field(fp,'wpOverlay').overlay.Markers if m.GetType().Name=='GMapMarkerRect')
    location=fp.MainMap.FromLatLngToLocal(marker.Position)
    click(int(location.X),int(location.Y));click(int(location.X),int(location.Y))
    assert fp.Commands.Rows.Count==3
    log('同一已有航点不离开标记连续点击：不新增重复点')
    mouse('Down',180,160);mouse('Move',230,200);mouse('Up',230,200)
    assert fp.Commands.Rows.Count==3
    mouse('Down',180,160);mouse('Wheel',180,160,120);mouse('Up',180,160)
    assert fp.Commands.Rows.Count==3
    field(FlightData,'projectOperationTabs').SelectedIndex=0
    assert not add.Checked and not fp.ProjectPlanningActive
    click(280,240);assert fp.Commands.Rows.Count==3
    FlightData.SelectProjectMission();assert not add.Checked
    log('地图加点B：关闭不加/连续点3个/拖拽与滚轮不加/切离关闭通过')

def task():
    global expected
    commands=List[Locationwp]()
    for i in range(31):
        item=Locationwp().Set(-35.363261+i*0.00005,149.165230+i*0.00005,584 if i==0 else 80+i,UInt16(16))
        item.GetType().GetField('frame').SetValue(item,Byte(0 if i==0 else 3));commands.Add(item)
    fp.WPtoScreen(commands);fp.Commands.Rows[0].Cells[field(fp,'Alt').Index].Value='93'
    expected=signature(call(fp,'GetCommandList'))
    field(fp,'chk_usemavftp').Checked=False
    assert fp.Commands.Rows.Count==30
    capture(MainV2,'mission-inline')
    fp.BUT_write_Click(None,EventArgs.Empty)
    assert field(fp,'missionTransferRunning') and MainV2.Enabled

def signature(items):return [(int(w.id),int(w.frame),round(float(w.lat),6),round(float(w.lng),6),round(float(w.alt),2),float(w.p1),float(w.p2),float(w.p3),float(w.p4)) for w in items]

def monitors():
    global statuswindow,curvewindow,ecuwindow
    monitoring=field(FlightData,'projectMonitoring')
    monitoring.ShowStatus();monitoring.ShowCurve();monitoring.ShowEcu()
    statuswindow=field(FlightData,'tabStatus').FindForm();curvewindow=field(FlightData,'zg1').FindForm()
    ecuwindow=next(f for f in Application.OpenForms if 'ECU' in f.Text)
    assert statuswindow!=MainV2 and curvewindow!=MainV2 and statuswindow!=curvewindow and ecuwindow!=statuswindow
    assert field(FlightData,'projectStatusWindowVisible') and field(FlightData,'CB_tuning').Checked
    assert field(FlightData,'hud1').Visible and field(FlightData,'tableLayoutPanelQuick').Visible
    capture(statuswindow,'status-window');capture(curvewindow,'curve-window');capture(ecuwindow,'ecu-window')

def closemonitors():
    statuswindow.Close();curvewindow.Close();ecuwindow.Close()
    assert not field(FlightData,'projectStatusWindowVisible') and not field(FlightData,'CB_tuning').Checked
    field(FlightData,'projectMonitoring').ShowCurve();field(FlightData,'zg1').FindForm().Close()
    assert fp.MainMap.Visible and not fp.MainMap.IsDisposed
    log('全部状态/实时曲线/ECU三独立窗口与左监视共存，关闭重开及停止采样通过')
try:
    GMaps.Instance.Mode=AccessMode.CacheOnly
    ui(setup)
    def toolscheck():
        field(FlightData,'projectOperationTabs').SelectedIndex=3
        download=FlightData.Controls.Find('ProjectLogDownload',True)[0]
        assert download.Visible and download.Enabled
        download.PerformClick()
        window=next(f for f in Application.OpenForms if f.GetType().Name=='LogDownloadMavLink')
        capture(window,'bin-log-window');window.Close()
        entry=FlightData.Controls.Find('ProjectScripts',True)[0]
        entry.PerformClick();script=field(FlightData,'projectScriptWindow')
        original=field(FlightData,'BUT_select_script');assert original.Visible
        capture(script,'script-window');script.Close()
        assert not original.IsDisposed
        entry.PerformClick();assert field(FlightData,'BUT_select_script')==original and original.Visible
        field(FlightData,'projectScriptWindow').Close()
        capture(MainV2,'tools-inline')
        log('BIN入口可见、原日志窗口开关；原脚本控件窗口关闭重开不销毁，未运行脚本')
    ui(toolscheck)
    log('PASS');File.WriteAllText(Path.Combine(output,'tools-status.txt'),'0')
except Exception as ex:
    import traceback
    log('FAIL '+traceback.format_exc())

