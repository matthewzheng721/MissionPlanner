# -*- coding: utf-8 -*-
# 仅连接本机固定翼 SITL，验证自定义参数控件、发送回读和恢复；不解锁、不起飞。
import clr
from System import Action,Array,Object,EventArgs,Environment,Decimal,DateTime,Activator,Int32
from System.IO import File,Directory,Path
from System.Net.Sockets import TcpClient,UdpClient
from System.Threading import Thread
from System.Reflection import BindingFlags
from System.Windows.Forms import Application,Timer,TextBox,FormWindowState
from MissionPlanner.Comms import TcpSerial,UdpSerial
from MissionPlanner.Utilities import Settings
out=Environment.GetEnvironmentVariable('MP_USERPARAM_OUTPUT') or Path.Combine(Settings.GetUserDataDirectory(),'userparam-test')
Directory.CreateDirectory(out);flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
report=[];originals={};user=None
File.WriteAllText(Path.Combine(out,'userparam-status.txt'),'1')
def log(s):report.append(str(s));File.WriteAllLines(Path.Combine(out,'userparam-result.txt'),report)
def field(o,n):return o.GetType().GetField(n,flags).GetValue(o)
def call(o,n,args):return o.GetType().GetMethod(n,flags).Invoke(o,Array[Object](args))
def ui(f):MainV2.Invoke(Action(f))
def waitfor(fn,seconds=20):
    for i in range(seconds*10):
        if fn():return
        Thread.Sleep(100)
    raise Exception('condition timed out')
def children(o):
    for c in o.Controls:
        yield c
        for x in children(c):yield x
def pv(n):return next(float(p.Value) for p in MAV.MAV.param if p.Name==n)
def tab(n):field(FlightData,'tabControlactions').SelectedTab=field(FlightData,n)
def connect():
    MainV2.Show();MainV2.WindowState=FormWindowState.Normal
    if Environment.GetEnvironmentVariable('MP_DETAIL_UDP')=='1':
        s=UdpSerial(UdpClient(14650));s.Port='14650';MAV.BaseStream=s;MainV2.doConnect(MAV,'preset','14650',True,True)
    else:
        s=TcpSerial();s.client=TcpClient('127.0.0.1',5760);MAV.BaseStream=s;MainV2.doConnect(MAV,'preset','5760',True,True)
    log('connection='+MAV.BaseStream.GetType().Name+' params='+str(MAV.MAV.param.Count))
options='AIRSPEED_CRUISE\r\nRC6_OPTION';answer=Timer();answer.Interval=100
def dialogs(sender,args):
    for f in list(Application.OpenForms):
        if f.Text=='Params':
            for c in children(f):
                if isinstance(c,TextBox):c.Text=options
            if f.AcceptButton is not None:f.AcceptButton.PerformClick()
answer.Tick+=dialogs
try:
    Settings.Instance.LogDir=Path.Combine(out,'userparam-logs');Directory.CreateDirectory(Settings.Instance.LogDir)
    ui(connect);assert MAV.MAV.param.ContainsKey('SIM_SPEEDUP') and not MAV.MAV.cs.armed
    if Environment.GetEnvironmentVariable('MP_DETAIL_MEMORY')=='1':
        ui(lambda:tab('tabQuick'));q=next(c for c in children(FlightData) if c.Name=='quickView1');assert str(q.Tag)=='airspeed'
        assert any(str(c.Tag)=='airspeed' for c in field(FlightData,'zg1').GraphPane.CurveList)
        ui(lambda:tab('tabAuxFunction'));a=next(c for c in children(FlightData) if c.GetType().Name=='AuxOptions');assert field(a,'mavlinkComboBox1').SelectedValue==51
        log('restart_quick_airspeed_curve_airspeed_aux51=PASS')
    ui(lambda:call(MainV2,'MenuTuning_Click',[None,EventArgs.Empty]));Thread.Sleep(600)
    def activate():
        global user
        back=field(field(MainV2,'MyView').current.Control,'backstageView');p=next(p for p in back.Pages if p.PageType.Name=='ConfigUserDefined');back.ActivatePage(p);user=p.Page
    ui(activate);ui(lambda:answer.Start())
    ui(lambda:next(c for c in children(user) if c.Name=='Modify').PerformClick())
    nums=[c for c in children(user) if c.GetType().Name=='MavlinkNumericUpDown'];combos=[c for c in children(user) if c.GetType().Name=='MavlinkComboBox']
    assert len(nums)==1 and len(combos)==1 and nums[0].Visible and nums[0].Enabled
    log('numeric_visible=PASS param='+nums[0].ParamName+' range='+str(nums[0].Minimum)+'..'+str(nums[0].Maximum))
    n=nums[0];originals[n.ParamName]=pv(n.ParamName)
    candidate=originals[n.ParamName]-1 if originals[n.ParamName]-1>=float(n.Minimum) else originals[n.ParamName]+1
    assert float(n.Minimum)<=candidate<=float(n.Maximum)
    for value in [candidate,originals[n.ParamName]]:
        ui(lambda:setattr(n,'Value',Decimal.Parse(str(value))));waitfor(lambda:abs(pv(n.ParamName)-value)<0.01)
        assert abs(MAV.GetParam(n.ParamName)-value)<0.01
    log('numeric_write_read_restore=PASS')
    c=combos[0];originals[c.ParamName]=pv(c.ParamName)
    for value in [51,int(originals[c.ParamName])]:
        ui(lambda:setattr(c,'SelectedValue',value));waitfor(lambda:int(pv(c.ParamName))==value);assert int(MAV.GetParam(c.ParamName))==value
    log('enum_write_read_restore=PASS')
    options='AIRSPEED_CRUISE\r\nRC7_OPTION'
    ui(lambda:next(c for c in children(user) if c.Name=='Modify').PerformClick())
    assert list(user.Options)==['AIRSPEED_CRUISE','RC7_OPTION'];assert any(getattr(c,'ParamName',None)=='RC7_OPTION' for c in children(user))
    ui(lambda:user.Activate());assert len([c for c in children(user) if c.GetType().Name=='MavlinkNumericUpDown'])==1
    log('modify_list_reactivate=PASS')
    def capture():
        MainV2.Width=1100;MainV2.Height=760;MainV2.Refresh()
        method=MainV2.GetType().GetMethod('DrawToBitmap');args=method.GetParameters()
        bmp=Activator.CreateInstance(args[0].ParameterType,Array[Object]([Int32(MainV2.Width),Int32(MainV2.Height)]))
        rect=Activator.CreateInstance(args[1].ParameterType,Array[Object]([Int32(0),Int32(0),Int32(MainV2.Width),Int32(MainV2.Height)]))
        method.Invoke(MainV2,Array[Object]([bmp,rect]));bmp.Save(Path.Combine(out,'userparams-fixed.png'));bmp.Dispose()
    ui(capture);Settings.Instance.Save()
    log('PASS');File.WriteAllText(Path.Combine(out,'userparam-status.txt'),'0')
except:
    import traceback
    log('FAIL '+traceback.format_exc())
finally:
    ui(lambda:answer.Stop())
    for name,value in originals.items():
        if MAV.BaseStream.IsOpen and abs(pv(name)-value)>0.0001:MAV.setParam(name,value)

if Environment.GetEnvironmentVariable('MP_DETAIL_FINAL_CONTROLS')=='1':
    exec(File.ReadAllText(Path.Combine(Path.GetDirectoryName(__file__),'Test-RetainedControlDetails.py')),globals())
