# 本轮离线检查HUD绘制；时间和质量值仅用于本地绘图验证，不连接设备。
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


from System import DateTime,Single
try:
    GMaps.Instance.Mode=AccessMode.CacheOnly
    def verify():
        MainV2.Show();MainV2.WindowState=FormWindowState.Normal;MainV2.Size=Size(1440,900)
        assert not MAV.BaseStream.IsOpen
        hud=field(FlightData,'hud1');hud.opengl=False
        original=Activator.CreateInstance(hud.GetType())
        assert original.displayconninfo;original.Dispose()
        assert not hud.displayconninfo
        hud.datetime=DateTime(2026,10,31,12,34,56);hud.linkqualitygcs=Single(99)
        capture(MainV2,'main-hud')
        call(FlightData,'hud1_DoubleClick',[None,EventArgs.Empty])
        duplicate=field(FlightData,'projectHudCopy')
        assert duplicate.Visible and not duplicate.displayconninfo
        duplicate.datetime=DateTime(2026,10,31,12,34,56);duplicate.linkqualitygcs=Single(99)
        capture(field(FlightData,'projectHudWindow'),'independent-hud')
        field(FlightData,'projectHudWindow').Close()
        assert hud.Visible and field(FlightData,'projectLinkStatus').Visible
        assert field(FlightData,'projectLinkStatus').Text=='数传  未连接'
        assert field(FlightData,'tableLayoutPanelQuick').Visible and field(FlightData,'gMapControl1').Visible
        assert not MAV.BaseStream.IsOpen
        log('原HUD默认displayconninfo=true；项目主HUD/独立副本=false，质量条/百分比及时间均隐藏')
        log('中间数传状态标签仍可见，独立窗关闭后主HUD/Quick/地图保留；全程离线')
    ui(verify)
    log('PASS');File.WriteAllText(Path.Combine(output,'status.txt'),'0')
except Exception as ex:
    import traceback
    log('FAIL '+traceback.format_exc())

