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
File.WriteAllText(Path.Combine(output,'status.txt'),'1')
def log(value):
    report.append(str(value));File.WriteAllLines(Path.Combine(output,'result.txt'),report)
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

from System.Threading import CancellationTokenSource
from System.Text import Encoding
from System.Windows.Forms import Timer,TextBox,DialogResult
from MissionPlanner.Utilities import Password
from System.IO import MemoryStream
from MissionPlanner.ArduPilot.Mavlink import MAVFtp

def setup():
    MainV2.Show();MainV2.WindowState=FormWindowState.Normal;MainV2.Size=Size(1440,900)
    field(MainV2,'MyView').ShowScreen('FlightData');field(FlightData,'hud1').opengl=False
    menu=field(MainV2,'MainMenu')
    assert not any(item.Name in ['MenuFlightData','MenuFlightPlanner','MenuInitConfig','MenuConfigTune'] for item in menu.Items)
    assert any(item.Name=='ProjectTools' for item in menu.Items)
    FlightData.SelectProjectSettings()
    settings=field(FlightData,'projectSettings')
    track=field(settings,'NUM_tracklength');before=track.Value;track.Value=before+1
    assert Settings.Instance.GetInt32('NUM_tracklength',0)==before+1
    FlightData.SelectProjectMission();FlightData.SelectProjectSettings()
    assert track.Value==before+1;track.Value=before
    assert field(settings,'label2').Visible and field(settings,'label99').Visible
    assert field(FlightData,'hud1').Visible and field(FlightData,'tableLayoutPanelQuick').Visible and MainV2.FlightPlanner.MainMap.Visible
    capture(MainV2,'settings')
    call(FlightData,'ShowProjectTool',['ftp'])
    assert field(FlightData,'projectFtp') is None
    capture(MainV2,'ftp-offline')
    call(FlightData,'ShowProjectTool',['rtk'])
    original=field(FlightData,'projectRtk');assert original.Visible and field(original,'timer1').Enabled
    capture(MainV2,'rtk')
    FlightData.SelectProjectSettings();assert not field(original,'timer1').Enabled
    call(FlightData,'ShowProjectTool',['rtk']);assert original==field(FlightData,'projectRtk') and field(original,'timer1').Enabled
    call(FlightData,'ShowProjectTool',[None]);assert not field(original,'timer1').Enabled
    capture(MainV2,'tools')
    log('顶部四页已移除；设置原控件即时保存/重开恢复；RTK同实例切页仅切UI timer；离线FTP占位通过')

def authorization():
    oldPassword=Settings.Instance['password'];oldProtect=Settings.Instance['password_protect']
    try:
        Settings.Instance['password_protect']='True';Password.EnterPassword('V07-local-test')
        def invoke(answer,action):
            timer=Timer();timer.Interval=100
            def tick(sender,event):
                dialog=next((f for f in Application.OpenForms if f.Text=='Enter Password'),None)
                if dialog is None:return
                timer.Stop()
                if answer:
                    next(c for c in dialog.Controls if isinstance(c,TextBox)).Text='V07-local-test'
                    dialog.AcceptButton.PerformClick()
                else:dialog.CancelButton.PerformClick()
            timer.Tick+=tick;timer.Start()
            try:action()
            finally:timer.Stop();timer.Dispose()
        FlightData.SelectProjectMission();tabs=field(FlightData,'projectOperationTabs')
        invoke(False,lambda:FlightData.SelectProjectSettings());assert tabs.SelectedTab==field(FlightData,'projectMissionTab')
        invoke(True,lambda:FlightData.SelectProjectSettings());assert tabs.SelectedTab==field(FlightData,'projectSettingsTab')
        call(FlightData,'ShowProjectTool',[None])
        for selection in ['rtk','ftp']:
            invoke(False,lambda:call(FlightData,'ShowProjectTool',[selection]));assert field(FlightData,'projectToolSelection') is None
            invoke(True,lambda:call(FlightData,'ShowProjectTool',[selection]));assert field(FlightData,'projectToolSelection')==selection
            call(FlightData,'ShowProjectTool',[None])
        log('既有密码保护：设置/RTK/FTP实际弹窗取消阻止、正确密码进入通过，已恢复测试设置')
    finally:
        Settings.Instance['password']=oldPassword or '';Settings.Instance['password_protect']=oldProtect or 'False'

def connect():
    stream=TcpSerial();stream.client=TcpClient('127.0.0.1',5890);MAV.BaseStream=stream
    MainV2.doConnect(MAV,'preset','5890',True,True)

try:
    GMaps.Instance.Mode=AccessMode.CacheOnly
    ui(setup);ui(authorization);ui(connect)
    waitfor(lambda:MAV.MAV.param.ContainsKey('SIM_SPEEDUP'),'专用SITL参数',90)
    assert not MAV.MAV.cs.armed
    ui(lambda:call(FlightData,'ShowProjectTool',['ftp']))
    waitfor(lambda:field(FlightData,'projectFtp') is not None,'FTP能力',60)
    ftp=[];ui(lambda:ftp.append(field(FlightData,'projectFtp')));ftp=ftp[0]
    waitfor(lambda:field(ftp,'treeView1').Nodes.Count>0 and field(ftp,'treeView1').Enabled,'原FTP根目录',60)
    ui(lambda:capture(MainV2,'ftp-online'))
    assert ftp.Visible and field(ftp,'treeView1').Visible and field(ftp,'listView1').Visible
    ui(lambda:FlightData.SelectProjectSettings());ui(lambda:call(FlightData,'ShowProjectTool',['ftp']))
    assert ftp==field(FlightData,'projectFtp') and not ftp.IsDisposed
    log('SITL FTP根目录读取完成，同连接切页复用原实例')
    name='v07-ui-roundtrip.txt';local=Path.Combine(output,name);payload='V07 original MAVFTP roundtrip\r\n'
    File.WriteAllText(local,payload,Encoding.ASCII)
    def upload():
        tree=field(ftp,'treeView1');tree.SelectedNode=tree.Nodes[0]
        pending.append(call(ftp,'UploadFile',[local]))
    pending=[];ui(upload)
    waitfor(lambda:pending[0].IsCompleted,'原UI上传小文件',60)
    if pending[0].IsFaulted:raise pending[0].Exception
    client=field(ftp,'_mavftp');cancel=CancellationTokenSource()
    data=client.GetFile('/'+name,cancel,False)
    assert Encoding.ASCII.GetString(data.ToArray())==payload
    client.kCmdRemoveFile('/'+name,cancel)
    log('原FTP UploadFile上传+原GetFile下载小文件逐字一致，SITL临时文件已删除')
    ui(lambda:MainV2.doDisconnect(MAV))
    waitfor(lambda:field(FlightData,'projectFtp') is None,'断连清旧实例')
    assert ftp.IsDisposed
    ui(lambda:capture(MainV2,'ftp-disconnected'))
    log('等待专用 SITL 重启');File.WriteAllText(Path.Combine(output,'restart-sitl.txt'),'ready')
    for i in range(900):
        if File.Exists(Path.Combine(output,'sitl-restarted.txt')):break
        Thread.Sleep(100)
    ui(connect);waitfor(lambda:MAV.MAV.param.ContainsKey('SIM_SPEEDUP'),'重连参数',90)
    waitfor(lambda:field(FlightData,'projectFtp') is not None,'新FTP实例',60)
    assert field(FlightData,'projectFtp')!=ftp
    log('断连清旧目录及旧客户端，重连新实例通过')
    ui(lambda:MainV2.doDisconnect(MAV))
    log('PASS');File.WriteAllText(Path.Combine(output,'status.txt'),'0')
except Exception as ex:
    import traceback
    log('FAIL '+traceback.format_exc())
