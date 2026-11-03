# 只连接本轮自建 localhost SITL；不解锁、不执行发动机或实机舵机命令。
import clr
from System import Action,Array,Object,EventArgs,Environment,Activator,Int32,DateTime
from System.IO import File,Directory,Path
from System.Reflection import BindingFlags
from System.Net.Sockets import TcpClient
from System.Threading import Thread
from System.Windows.Forms import Application,FormWindowState
from System.Drawing import Size
from MissionPlanner.Comms import TcpSerial
from MissionPlanner.Utilities import ProjectPreflight,ProjectCheckResult,Settings
from MissionPlanner.Controls import ProjectPreflightWizard
from GMap.NET import GMaps,AccessMode
flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
output=Environment.GetEnvironmentVariable('MP_V05_OUTPUT')
Directory.CreateDirectory(output);report=[]
File.WriteAllText(Path.Combine(output,'status.txt'),'1')
def log(value):
    report.append(str(value));File.WriteAllLines(Path.Combine(output,'result.txt'),report)
def field(obj,name):return obj.GetType().GetField(name,flags).GetValue(obj)
def setfield(obj,name,value):obj.GetType().GetField(name,flags).SetValue(obj,value)
def call(obj,name,args=[]):return obj.GetType().GetMethod(name,flags).Invoke(obj,Array[Object](args))
def ui(fn):return MainV2.Invoke(Action(fn))
def param(name):return next(p.Value for p in MAV.MAV.param if p.Name==name)
def waitfor(predicate,label,seconds=30):
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
try:
    GMaps.Instance.Mode=AccessMode.CacheOnly
    ui(lambda: MainV2.Show())
    stream=TcpSerial();stream.client=TcpClient('127.0.0.1',5900);MAV.BaseStream=stream
    MainV2.doConnect(MAV,'preset','5900',True,True)
    waitfor(lambda:MAV.MAV.param.ContainsKey('RCMAP_ROLL'),'SITL参数')
    log('SITL已连接，参数数='+str(MAV.MAV.param.Count)+'；SDT_RUN存在='+str(MAV.MAV.param.ContainsKey('SDT_RUN')))
    def setup():
        global wizard,panel
        MainV2.WindowState=FormWindowState.Normal;MainV2.Size=Size(1366,740)
        field(FlightData,'hud1').opengl=False
        wizard=ProjectPreflightWizard();wizard.Show(MainV2);wizard.Size=Size(900,720)
        setfield(wizard,'index',Int32(4));call(wizard,'Render')
        panel=field(wizard,'embedded').Controls[0]
        assert not field(wizard,'confirmed').Visible
    ui(setup)
    ui(lambda:next(b for b in panel.Controls[0].Controls[1].Controls if b.Text=='FBWA').PerformClick());waitfor(lambda:MAV.MAV.cs.mode.upper()=='FBWA','FBWA模式')
    Thread.Sleep(600)
    def fbwa():
        call(panel,'RefreshState')
        assert not field(panel,'inputs').Visible
        assert field(panel,'fbwaChecked').Visible and not field(panel,'manualChecked').Visible
        field(panel,'fbwaChecked').Checked=True
        assert ProjectPreflight.Checklist.FbwaPolarityConfirmed and not ProjectPreflight.Checklist.ManualPolarityConfirmed
        assert ProjectPreflight.Checklist[4]!=ProjectCheckResult.Confirmed
        capture(wizard,'fbwa-check')
    ui(fbwa)
    ui(lambda:call(panel,'ChangeView',[True]))
    assert not ProjectPreflight.Checklist.ManualPolarityConfirmed
    ui(lambda:next(b for b in panel.Controls[0].Controls[1].Controls if b.Text=='MANUAL').PerformClick());waitfor(lambda:MAV.MAV.cs.mode.upper()=='MANUAL','MANUAL模式')
    ui(lambda:setattr(field(panel,'fraction'),'Value',15))
    ui(lambda:call(panel,'Begin',['PITCH',Int32(1)]))
    waitfor(lambda:MAV.MAV.cs.ch2in>1500,'抬头输入回报')
    log('MANUAL按住抬头 RC2='+str(MAV.MAV.cs.ch2in))
    ui(lambda:capture(wizard,'manual-check-held'))
    ui(lambda:call(panel,'Center'))
    waitfor(lambda:abs(MAV.MAV.cs.ch2in-param('RC2_TRIM'))<3,'松开回中')
    ui(lambda:call(panel,'Begin',['ROLL',Int32(1)]))
    waitfor(lambda:MAV.MAV.cs.ch1in>1500,'右滚输入回报')
    log('MANUAL按住右滚 RC1='+str(MAV.MAV.cs.ch1in))
    ui(lambda:call(panel,'Center'))
    waitfor(lambda:abs(MAV.MAV.cs.ch1in-param('RC1_TRIM'))<3,'滚转回中')
    def finish_manual():
        field(panel,'manualChecked').Checked=True
        assert ProjectPreflight.Checklist[4]==ProjectCheckResult.Confirmed
        nav=field(wizard,'nextButton');rect=wizard.RectangleToClient(nav.RectangleToScreen(nav.ClientRectangle))
        assert rect.Bottom<wizard.ClientSize.Height and rect.Right<wizard.ClientSize.Width
        capture(wizard,'manual-check-centered')
    ui(finish_manual)
    MAV.setMode('FBWA');waitfor(lambda:MAV.MAV.cs.mode.upper()=='FBWA','外部FBWA模式')
    waitfor(lambda:field(panel,'rollChannel')==0 and field(panel,'pitchChannel')==0,'外部模式变化释放')
    log('实际SITL模式反馈、俯仰/滚转按住与回中、外部切模式释放、两子项核对：通过')
    ui(lambda:next(b for b in panel.Controls[0].Controls[1].Controls if b.Text=='AUTO').PerformClick())
    Thread.Sleep(1000)
    log('AUTO已通过页面请求；本次无任务SITL当前实际模式='+str(MAV.MAV.cs.mode)+'；页面以实际回报显示')
    def servo_page():
        global servo
        setfield(wizard,'index',Int32(3));call(wizard,'Render');servo=field(wizard,'embedded').Controls[0]
        layout=servo.Controls[0];grid=field(servo,'grid')
        assert grid.Bottom<=layout.Controls[4].Top
        assert grid.Height>50
        grid.FirstDisplayedScrollingRowIndex=14
        assert grid.Rows[14].Displayed
        grid.FirstDisplayedScrollingRowIndex=0
        capture(wizard,'servo-test')
    ui(servo_page)
    if MAV.MAV.param.ContainsKey('SDT_RUN'):
        originalMode=param('SDT_MODE')
        def write_mode(value):
            global applyTask
            grid=field(servo,'grid');row=next(r for r in grid.Rows if str(r.Cells[0].Value)=='SDT_MODE');row.Cells[1].Value=str(value)
            applyTask=call(servo,'Apply')
        ui(lambda:write_mode(2))
        waitfor(lambda:applyTask.IsCompleted,'SDT写入回报')
        assert field(servo,'written') and param('SDT_MODE')==2
        log('本页SDT_MODE改为正弦，整组PARAM_VALUE确认与预览更新：通过')
        ui(lambda:call(servo,'TogglePreview'))
        Thread.Sleep(250)
        ui(lambda:capture(wizard,'servo-preview'))
        ui(lambda:call(servo,'TogglePreview'))
        ui(lambda:write_mode(originalMode))
        waitfor(lambda:applyTask.IsCompleted,'恢复SDT参数')
        assert field(servo,'written') and param('SDT_MODE')==originalMode
        log('SITL实际SDT参数可读取，预览播放/暂停：通过；未启动脚本实际输出')
    else:log('本机SITL未提供SDT脚本参数；波形验证使用离线模型测试，截图显示等待参数')
    ui(lambda:wizard.Close())
    MAV.BaseStream.Close()
    ui(lambda:ProjectPreflight.RefreshConnection())
    assert not ProjectPreflight.Checklist.FbwaPolarityConfirmed and not ProjectPreflight.Checklist.ManualPolarityConfirmed
    log('断联清除两组核对：通过')
    log('PASS');File.WriteAllText(Path.Combine(output,'status.txt'),'0')
except Exception:
    import traceback
    log('FAIL '+traceback.format_exc())
