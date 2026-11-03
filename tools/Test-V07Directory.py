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
File.WriteAllText(Path.Combine(output,'directory-status.txt'),'1')
def log(value):
    report.append(str(value));File.WriteAllLines(Path.Combine(output,'directory-result.txt'),report)
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


from System.Windows.Forms import TreeNodeMouseClickEventArgs
try:
    GMaps.Instance.Mode=AccessMode.CacheOnly
    def setup():
        MainV2.Show();MainV2.WindowState=FormWindowState.Normal;MainV2.Size=Size(1440,900)
        field(FlightData,'hud1').opengl=False
        call(FlightData,'ShowProjectTool',['ftp'])
        stream=TcpSerial();stream.client=TcpClient('127.0.0.1',5890);MAV.BaseStream=stream
        MainV2.doConnect(MAV,'preset','5890',True,True)
        assert field(FlightData,'projectOperationTabs').SelectedTab==field(FlightData,'projectToolsTab')
    ui(setup)
    waitfor(lambda:field(FlightData,'projectFtp') is not None,'SITL FTP',90)
    ftp=field(FlightData,'projectFtp')
    waitfor(lambda:field(ftp,'treeView1').Nodes.Count>0 and field(ftp,'treeView1').Enabled,'原树目录')
    def select():
        tree=field(ftp,'treeView1');node=tree.Nodes[0];tree.SelectedNode=node;node.Expand()
        assert ftp.Visible and tree.Visible and field(ftp,'listView1').Visible
        panel=field(FlightData,'projectOperationSplit').Panel2
        bounds=panel.RectangleToScreen(panel.ClientRectangle)
        for c in [ftp,tree,field(ftp,'listView1')]:assert bounds.Contains(c.RectangleToScreen(c.ClientRectangle)),c.Name
        call(ftp,'TreeView1_NodeMouseClick',[tree,TreeNodeMouseClickEventArgs(node,MouseButtons.Left,1,node.Bounds.X+2,node.Bounds.Y+2)])
    ui(select)
    waitfor(lambda:field(ftp,'listView1').Items.Count>0,'点击根目录真实文件列表',60)
    def verify():
        assert field(FlightData,'hud1').Visible and field(FlightData,'tableLayoutPanelQuick').Visible and MainV2.FlightPlanner.MainMap.Visible
        capture(MainV2,'ftp-directory')
        log('实际点击目录节点读取文件列表 '+str(field(ftp,'listView1').Items.Count)+' 项；原树/列表均可见且完全位于右栏；HUD/Quick/唯一地图同时可见')
        MainV2.doDisconnect(MAV)
        assert field(FlightData,'projectOperationTabs').SelectedTab==field(FlightData,'projectToolsTab')
    ui(verify)
    log('连接/断连保持当前工具页；PASS');File.WriteAllText(Path.Combine(output,'directory-status.txt'),'0')
except Exception as ex:
    import traceback
    log('FAIL '+traceback.format_exc())
