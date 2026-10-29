# 仅连接本机 SITL，不解锁、不起飞；通过实际参数表按钮验证写入。
import clr
import System
import MAVLink
from System import Action,Array,Object,EventArgs,Environment,DateTime
from System.IO import File,Directory,Path
from System.Net.Sockets import TcpClient
from System.Threading import Thread,ThreadStart,Monitor,ManualResetEvent
from System.Diagnostics import Stopwatch
from System.Reflection import BindingFlags
from System.Windows.Forms import Timer,Application,Button,DialogResult,FormWindowState
from MissionPlanner.Comms import TcpSerial
from MissionPlanner.Controls import MAVLinkInspector
from MissionPlanner.Utilities import Settings
from GMap.NET import GMaps,AccessMode
output=Environment.GetEnvironmentVariable('MP_CONFIG_OUTPUT') or Path.Combine(Settings.GetUserDataDirectory(),'config-test')
Directory.CreateDirectory(output)
flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
report=[];health=[];param_events=[];phase='setup';lasttick=0;raw=None;ftp=None;transfer=None;inspector=None;original=None
clock=Stopwatch();timer=Timer();timer.Interval=50
answers=Timer();answers.Interval=100
File.WriteAllText(Path.Combine(output,'status.txt'),'1')
def log(s):report.append(str(s));File.WriteAllLines(Path.Combine(output,'result.txt'),report)
def field(o,n):return o.GetType().GetField(n,flags).GetValue(o)
def call(o,n,args):return o.GetType().GetMethod(n,flags).Invoke(o,Array[Object](args))
def ui(f):MainV2.Invoke(Action(f))
def waitfor(fn,seconds=30):
    for i in range(seconds*20):
        if fn():return
        Thread.Sleep(50)
    raise Exception('condition timed out')
def select(name):
    call(MainV2,'MenuTuning_Click',[None,EventArgs.Empty])
    current=field(MainV2,'MyView').current.Control
    back=field(current,'backstageView');p=next(p for p in back.Pages if p.PageType.Name==name)
    back.ActivatePage(p)
    return p.Page
def children(c):
    for cc in c.Controls:
        yield cc
        for ccc in children(cc):yield ccc

def answer(sender,args):
    for f in list(Application.OpenForms):
        if f.Text in ['Confirm Parameter Changes','Saved','No changes']:
            if f.AcceptButton is not None:f.AcceptButton.PerformClick()

def parameter_event(sender,msg):
    from System.Text import Encoding
    if msg.msgid==23:
        p=msg.ToStructure[MAVLink.mavlink_param_set_t]()
    elif msg.msgid==22:
        p=msg.ToStructure[MAVLink.mavlink_param_value_t]()
    else:return
    if Encoding.ASCII.GetString(p.param_id).split(chr(0))[0]=='AIRSPEED_CRUISE':
        param_events.append('{},{},{},{},{}'.format(clock.ElapsedMilliseconds,phase,msg.msgid,p.param_value,transfer is not None and not transfer.IsCompleted))
def sample(sender,args):
    global lasttick
    now=clock.ElapsedMilliseconds
    health.append('{},{},{},{},{}'.format(phase,transfer is not None and not transfer.IsCompleted,(DateTime.UtcNow-MAV.MAV.lastvalidpacket).TotalMilliseconds,now-lasttick,MAV.MAV.cs.mode));lasttick=now

def connect():
    MainV2.Show();MainV2.WindowState=FormWindowState.Normal
    call(MainV2,'MenuFlightData_Click',[None,EventArgs.Empty])
    stream=TcpSerial();stream.client=TcpClient('127.0.0.1',5760);MAV.BaseStream=stream
    MainV2.doConnect(MAV,'preset','5760',True,True)

def mode(name):
    call(MainV2,'MenuFlightData_Click',[None,EventArgs.Empty])
    field(FlightData,'tabControlactions').SelectedTab=field(FlightData,'tabActions')
    field(FlightData,'CMB_modes').Text=name;field(FlightData,'BUT_setmode').PerformClick()

def write(value):
    global raw
    raw=select('ConfigRawParams')
    grid=field(raw,'Params');row=next(r for r in grid.Rows if str(r.Cells[0].Value)=='AIRSPEED_CRUISE')
    grid.CurrentCell=row.Cells[1];grid.BeginEdit(True)
    grid.EditingControl.Text=str(value);grid.EndEdit()
    log('grid_pending='+str(field(raw,'_changes')['AIRSPEED_CRUISE']))
    field(raw,'BUT_writePIDS').PerformClick()
    log('write_dispatched_ftp_running='+str(transfer is not None and not transfer.IsCompleted))
try:
    GMaps.Instance.Mode=AccessMode.CacheOnly
    ui(connect);Thread.Sleep(2000)
    assert not MAV.MAV.cs.armed
    original=MAV.GetParam('AIRSPEED_CRUISE')
    def inspect():
        global planner
        planner=select('ConfigPlanner')
        current=field(MainV2,'MyView').current.Control
        names=[p.PageType.Name for p in field(current,'backstageView').Pages]
        log('pages='+','.join(names));assert set(names)==set(['ConfigArducopter','MavFTPUI','ConfigUserDefined','ConfigRawParams','ConfigPlanner'])
        visible=[c.Name for c in planner.Controls if c.Visible];log('planner_visible='+','.join(visible))
        assert not any(n in visible for n in ['CHK_mavdebug','CHK_enablespeech','CMB_theme','CMB_Layout','BUT_videostart','BUT_Joystick','CHK_beta'])
        log('rates='+','.join(field(planner,n).Text for n in ['CMB_rateattitude','CMB_rateposition','CMB_ratestatus','CMB_raterc','CMB_ratesensors']))
    ui(inspect)
    answers.Tick+=answer;timer.Tick+=sample
    def start():
        global lasttick,inspector
        clock.Restart();lasttick=0;timer.Start();answers.Start()
        inspector=MAVLinkInspector(MAV);inspector.Show(MainV2)
    ui(start)
    MAV.OnPacketSent+=parameter_event;MAV.OnPacketReceived+=parameter_event
    tools_only=Environment.GetEnvironmentVariable('MP_CONFIG_TOOLS_ONLY')=='1'
    log('scope='+('tools_only' if tools_only else 'full'))
    if not tools_only:
        phase='ftp_start'
        def ftpstart():
            global ftp
            ftp=select('MavFTPUI')
        ui(ftpstart)
        waitfor(lambda:field(ftp,'treeView1').Nodes.Count>=2 and field(ftp,'toolStripStatusLabel1').Text=='Ready')
        Thread.Sleep(1000)
        testfile=Path.Combine(output,'config-transfer.bin')
        File.WriteAllBytes(testfile,Array[System.Byte]([i%251 for i in range(1048576)]))
        def upload():
            global transfer
            tree=field(ftp,'treeView1');tree.SelectedNode=tree.Nodes[0]
            transfer=call(ftp,'UploadFile',[testfile])
        ui(upload);Thread.Sleep(200)
        assert not transfer.IsCompleted
        phase='ftp_mode'
        w=Stopwatch.StartNew();ui(lambda:mode('STABILIZE'));w.Stop();log('mode_button_ms='+str(w.ElapsedMilliseconds)+' ftp_running='+str(not transfer.IsCompleted))
        waitfor(lambda:MAV.MAV.cs.mode.upper()=='STABILIZE')
        assert not transfer.IsCompleted
        phase='ftp_parameter'
        w=Stopwatch.StartNew();ui(lambda:write(original+1));waitfor(lambda:not field(raw,'writingParameters'));w.Stop()
        log('grid_edit_confirm_write_ms='+str(w.ElapsedMilliseconds)+' ftp_running='+str(not transfer.IsCompleted)+' readback='+str(MAV.GetParam('AIRSPEED_CRUISE')))
        assert not transfer.IsCompleted
        assert abs(MAV.GetParam('AIRSPEED_CRUISE')-(original+1))<.01
        phase='ftp_complete';waitfor(lambda:transfer.IsCompleted,300)
        if transfer.IsFaulted:raise transfer.Exception
        log('ftp_upload_crc_complete=True')
        phase='restore';ui(lambda:write(original));waitfor(lambda:not field(raw,'writingParameters'))
        assert abs(MAV.GetParam('AIRSPEED_CRUISE')-original)<.01
        log('parameter_restored=True')
        phase='edit_during_write'
        held=ManualResetEvent(False);release=ManualResetEvent(False)
        def hold_transport():
            lock=field(MAV,'writelock');Monitor.Enter(lock)
            try:held.Set();release.WaitOne(15000)
            finally:Monitor.Exit(lock)
        holder=Thread(ThreadStart(hold_transport));holder.Start();held.WaitOne()
        try:
            ui(lambda:write(original+1))
            assert field(raw,'writingParameters')
            def edit_again():
                grid=field(raw,'Params');row=next(r for r in grid.Rows if str(r.Cells[0].Value)=='AIRSPEED_CRUISE')
                grid.CurrentCell=row.Cells[1];grid.BeginEdit(True);grid.EditingControl.Text=str(original+2);grid.EndEdit()
            ui(edit_again)
        finally:release.Set();holder.Join()
        waitfor(lambda:not field(raw,'writingParameters'))
        pending=field(raw,'_changes')['AIRSPEED_CRUISE'];log('new_edit_retained='+str(pending))
        assert pending==original+2
        assert abs(MAV.GetParam('AIRSPEED_CRUISE')-(original+1))<.01
        ui(lambda:write(original));waitfor(lambda:not field(raw,'writingParameters'))
        assert abs(MAV.GetParam('AIRSPEED_CRUISE')-original)<.01
        phase='message_interval'
    counts=[]
    def received(sender,msg):
        if msg.msgid==30:counts.append(DateTime.UtcNow)
    MAV.OnPacketReceived+=received
    from MissionPlanner import temp
    def interval():
        global intervalform,ratebox,setbutton,intervalstatus
        temp.ShowMessageInterval(MainV2)
        intervalform=next(f for f in Application.OpenForms if f.Text=='Message Interval')
        controls=list(intervalform.Controls[0].Controls)
        controls[0].Text='ATTITUDE';ratebox=controls[1];ratebox.Text='5'
        setbutton=controls[2];intervalstatus=controls[4];setbutton.PerformClick()
    ui(interval);waitfor(lambda:setbutton.Enabled)
    log('interval_status='+intervalstatus.Text);assert '确认' in intervalstatus.Text
    Thread.Sleep(1000);n=len(counts);Thread.Sleep(4000);intervalrate=(len(counts)-n)/4.0
    log('attitude_after_interval_hz='+str(intervalrate));assert 3.5<=intervalrate<=6.5
    def group():
        global planner,oldrate
        planner=select('ConfigPlanner');oldrate=field(planner,'CMB_rateattitude').Text
        field(planner,'CMB_rateattitude').Text='3'
    ui(group);Thread.Sleep(1000);n=len(counts);Thread.Sleep(4000)
    log('attitude_after_planner_3_hz='+str((len(counts)-n)/4.0))
    log('interval_before_restore_disposed='+str(intervalform.IsDisposed)+' visible='+str(intervalform.Visible)+' enabled='+str(setbutton.Enabled))
    assert not intervalform.IsDisposed
    def restore_rates():
        field(planner,'CMB_rateattitude').Text=oldrate
        ratebox.Text=oldrate;setbutton.PerformClick()
    ui(restore_rates);waitfor(lambda:setbutton.Enabled or intervalform.IsDisposed)
    assert not intervalform.IsDisposed
    log('interval_restored_status='+intervalstatus.Text);assert '确认' in intervalstatus.Text
    ui(lambda:intervalform.Close())
    MAV.OnPacketReceived-=received
    phase='capture'
    ui(inspect)
    if Environment.GetEnvironmentVariable('MP_CONFIG_CAPTURE')=='1':
        from System.Drawing import Bitmap,Rectangle,Size
        from System.Drawing.Imaging import ImageFormat
        for capturepage in ['ConfigPlanner','ConfigRawParams','MavFTPUI']:
            def capture():
                MainV2.WindowState=FormWindowState.Normal;MainV2.Size=Size(1100,760)
                selected=select(capturepage)
                MainV2.PerformLayout();selected.PerformLayout();MainV2.Refresh()
                current=field(MainV2,'MyView').current.Control
                actual=field(current,'backstageView').SelectedPage.PageType.Name
                assert actual==capturepage and selected.Visible
                log('capture_actual='+actual)
                # MP 同时加载两套 Drawing 类型，按实际控件签名创建截图对象。
                from System import Activator,Int32
                draw=MainV2.GetType().GetMethod('DrawToBitmap')
                types=draw.GetParameters()
                bmp=Activator.CreateInstance(types[0].ParameterType,Array[Object]([Int32(MainV2.Width),Int32(MainV2.Height)]))
                rect=Activator.CreateInstance(types[1].ParameterType,Array[Object]([Int32(0),Int32(0),Int32(MainV2.Width),Int32(MainV2.Height)]))
                try:
                    draw.Invoke(MainV2,Array[Object]([bmp,rect]))
                    bmp.Save(Path.Combine(output,capturepage+'.png'))
                finally:bmp.Dispose()
            ui(capture)
    assert not MAV.MAV.cs.armed
    log('PASS');File.WriteAllText(Path.Combine(output,'status.txt'),'0')
except Exception as ex:
    import traceback
    log('FAIL '+str(ex));log(traceback.format_exc())
finally:
    ui(lambda:timer.Stop());ui(lambda:answers.Stop())
    if inspector is not None:ui(lambda:inspector.Close())
    File.WriteAllLines(Path.Combine(output,'health.csv'),['phase,ftp_running,packet_age_ms,ui_tick_gap_ms,mode']+health)
    MAV.OnPacketSent-=parameter_event;MAV.OnPacketReceived-=parameter_event
    File.WriteAllLines(Path.Combine(output,'parameter-events.csv'),['elapsed_ms,phase,msgid,value,ftp_running']+param_events)
    log('DONE')