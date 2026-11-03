# 本轮只离线打开原诊断窗口，不连接设备、不点击消息频率设置。
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


try:
    GMaps.Instance.Mode=AccessMode.CacheOnly
    def verify():
        MainV2.Show();MainV2.WindowState=FormWindowState.Normal;MainV2.Size=Size(1440,900)
        field(FlightData,'hud1').opengl=False
        FlightData.SelectProjectTools()
        assert not MAV.BaseStream.IsOpen
        menu=field(MainV2,'MainMenu')
        assert not any(item.Name=='ProjectTools' or item.Text=='工具' for item in menu.Items)
        assert field(MainV2,'MenuConnect').Visible
        panel=field(FlightData,'projectOperationSplit').Panel2
        bounds=panel.RectangleToScreen(panel.ClientRectangle)
        for buttonName in ['ProjectMavlinkInspector','ProjectMessageInterval']:
            button=FlightData.Controls.Find(buttonName,True)[0]
            assert button.Visible and button.Enabled
            assert bounds.Contains(button.RectangleToScreen(button.ClientRectangle)),buttonName
        assert field(FlightData,'hud1').Visible and field(FlightData,'tableLayoutPanelQuick').Visible and field(FlightData,'gMapControl1').Visible
        capture(MainV2,'tools-diagnostics')
        FlightData.Controls.Find('ProjectMavlinkInspector',True)[0].PerformClick()
        inspector=next(f for f in Application.OpenForms if f.GetType().Name=='MAVLinkInspector')
        assert inspector.Visible;capture(inspector,'mavlink-inspector');inspector.Close();assert inspector.IsDisposed
        FlightData.Controls.Find('ProjectMessageInterval',True)[0].PerformClick()
        interval=next(f for f in Application.OpenForms if f.Text=='Message Interval')
        assert interval.Visible;capture(interval,'message-interval');interval.Close();assert interval.IsDisposed
        assert field(FlightData,'hud1').Visible and field(FlightData,'tableLayoutPanelQuick').Visible and field(FlightData,'gMapControl1').Visible
        assert not MAV.BaseStream.IsOpen
        log('顶部工具菜单移除，连接保留；右工具诊断两按钮真实可见/可点且处于右栏内')
        log('原 MAVLinkInspector 与 Message Interval 窗口均实际打开/关闭，HUD/Quick/地图持续可见；全程离线，未点击Set/Set All')
    ui(verify)
    log('PASS');File.WriteAllText(Path.Combine(output,'status.txt'),'0')
except Exception as ex:
    import traceback
    log('FAIL '+traceback.format_exc())
