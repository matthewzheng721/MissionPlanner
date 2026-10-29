# -*- coding: utf-8 -*-
import clr
from System import Action,Array,Object,EventArgs,Environment
from System.IO import File,Directory,Path
from System.Threading import Thread
from System.Reflection import BindingFlags
from System.Windows.Forms import Application,Timer
from MissionPlanner.Utilities import Settings
out=Environment.GetEnvironmentVariable('MP_USERPARAM_OUTPUT') or Path.Combine(Settings.GetUserDataDirectory(),'userparam-test')
Directory.CreateDirectory(out);flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic

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
def tab(n):field(FlightData,'tabControlactions').SelectedTab=field(FlightData,n)
# -*- coding: utf-8 -*-
# 在已连接的本机 SITL 上验证保留动作；仅用于新建且没有任务的模拟器目录。
import MAVLink
from System import UInt16,Byte
from System.Collections.Generic import List
from System.Windows.Forms import DialogResult
from MissionPlanner.Utilities import Locationwp
control_report=[];feedbacks=[];radii={};mission_created=False;passed=False
File.WriteAllText(Path.Combine(out,'control-status.txt'),'1')
def control_log(s):control_report.append(str(s));File.WriteAllLines(Path.Combine(out,'control-result.txt'),control_report)
def feedback(sender,m):
    if m.msgid==42:
        p=m.ToStructure[MAVLink.mavlink_mission_current_t]();feedbacks.append('MISSION_CURRENT '+str(p.seq))
    elif m.msgid==77:
        p=m.ToStructure[MAVLink.mavlink_command_ack_t]();feedbacks.append('ACK {},{}'.format(p.command,p.result))
ct=Timer();ct.Interval=100
def confirm(sender,args):
    for f in list(Application.OpenForms):
        if f.Text in ['Reset Home Coords','Action']:
            if f.AcceptButton is not None:f.AcceptButton.PerformClick()
        elif f.Text in ['Error',u'错误']:
            control_log('dialog='+' '.join(c.Text for c in children(f)))
            if f.AcceptButton is not None:f.AcceptButton.PerformClick()
ct.Tick+=confirm
try:
    assert MAV.MAV.param.ContainsKey('SIM_SPEEDUP') and not MAV.MAV.cs.armed
    initial_count=MAV.getWPCount();control_log('initial_mission_count='+str(initial_count))
    assert initial_count<=1,'Use a fresh isolated SITL directory with no navigation items'
    MAV.OnPacketReceived+=feedback;ui(lambda:ct.Start())
    fp=MainV2.FlightPlanner
    ui(lambda:call(MainV2,'MenuFlightPlanner_Click',[None,EventArgs.Empty]))
    def p(lat,lng,alt):return Locationwp().Set(lat,lng,alt,UInt16(16))
    cmds=List[Locationwp]();home=p(-35.363261,149.165230,584);home.GetType().GetField('frame').SetValue(home,Byte(0));cmds.Add(home)
    cmds.Add(p(-35.3629,149.1654,100));cmds.Add(p(-35.3625,149.1658,110))
    ui(lambda:fp.WPtoScreen(cmds))
    radii['WP_RADIUS']=MAV.GetParam('WP_RADIUS');radii['WP_LOITER_RAD']=MAV.GetParam('WP_LOITER_RAD')
    ui(lambda:setattr(field(fp,'TXT_WPRad'),'Text','25'));ui(lambda:setattr(field(fp,'TXT_loiterrad'),'Text','60'))
    assert field(fp,'TXT_WPRad').Enabled and field(fp,'TXT_loiterrad').Enabled
    mission_created=True;ui(lambda:fp.BUT_write_Click(None,EventArgs.Empty));waitfor(lambda:not field(fp,'missionTransferRunning'),40)
    assert MAV.getWPCount()==3
    assert abs(MAV.GetParam('WP_RADIUS')-25)<0.01 and abs(MAV.GetParam('WP_LOITER_RAD')-60)<0.01
    control_log('mission_upload_and_radius_readback=PASS 25/60')
    ui(lambda:call(MainV2,'MenuFlightData_Click',[None,EventArgs.Empty]));ui(lambda:tab('tabActions'))
    ui(lambda:call(FlightData,'CMB_setwp_Click',[None,EventArgs.Empty]))
    ui(lambda:setattr(field(FlightData,'CMB_setwp'),'SelectedIndex',1));ui(lambda:field(FlightData,'BUT_setwp').PerformClick())
    waitfor(lambda:'MISSION_CURRENT 1' in feedbacks)
    control_log('set_current_waypoint=PASS MISSION_CURRENT 1')
    ui(lambda:setattr(field(FlightData,'CMB_action'),'Text','Loiter_Unlim'));ui(lambda:field(FlightData,'BUTactiondo').PerformClick())
    waitfor(lambda:MAV.MAV.cs.mode.upper()=='LOITER')
    assert 'ACK 17,0' in feedbacks;control_log('action_dropdown_loiter=PASS ACK17 accepted and LOITER')
    for page,button in [('tabActionsSimple','myButton3'),('tabActions','BUT_quickauto')]:
        ui(lambda:tab(page));ui(lambda:field(FlightData,button).PerformClick());waitfor(lambda:MAV.MAV.cs.mode.upper()=='AUTO')
        assert not MAV.MAV.cs.armed;control_log(button+'=PASS AUTO while disarmed')
        ui(lambda:tab('tabActions'));ui(lambda:setattr(field(FlightData,'CMB_modes'),'Text','STABILIZE'));ui(lambda:field(FlightData,'BUT_setmode').PerformClick());waitfor(lambda:MAV.MAV.cs.mode.upper()=='STABILIZE')
    passed=True
except:
    import traceback
    control_log('FAIL '+traceback.format_exc())
finally:
    for name,value in radii.items():
        assert MAV.setParam(name,value);assert abs(MAV.GetParam(name)-value)<0.01
    if mission_created:
        MAV.setWPTotal(UInt16(0));count=MAV.getWPCount();assert count<=1;control_log('test_mission_cleared=PASS home_only_count='+str(count))
    control_log('radii_restored='+str(radii)+' armed='+str(MAV.MAV.cs.armed))
    ui(lambda:ct.Stop());MAV.OnPacketReceived-=feedback
    File.WriteAllLines(Path.Combine(out,'control-feedback.txt'),feedbacks)

if passed:
    control_log('PASS');File.WriteAllText(Path.Combine(out,'control-status.txt'),'0')
