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
output=Environment.GetEnvironmentVariable('MP_PARAM_PERF_OUTPUT') or Path.Combine(Settings.GetUserDataDirectory(),'param-performance')
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
        if f.Text in ['Confirm Parameter Changes','Saved','No changes','原始参数警告','Raw Param Warning']:
            log('dialog='+f.Text+' elapsed_ms='+str(clock.ElapsedMilliseconds))
            if f.AcceptButton is not None:f.AcceptButton.PerformClick()
            else:
                for button in f.Controls:
                    if getattr(button,'DialogResult',None)==DialogResult.OK:button.PerformClick();break
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
def measured(name,action):
    global phase
    phase=name;watch=Stopwatch.StartNew();ui(action);watch.Stop()
    log(name+'_ui_call_ms='+str(watch.ElapsedMilliseconds))
    Thread.Sleep(150)
def ready(name):
    watch=Stopwatch.StartNew()
    waitfor(lambda:raw.Enabled and field(raw,'Params').Enabled and field(raw,'Params').Rows.Count>=1000)
    log(name+'_additional_ready_wait_ms='+str(watch.ElapsedMilliseconds))
def openraw():
    global raw
    raw=select('ConfigRawParams')
def verify_rows():
    from MissionPlanner.Utilities import ParameterMetaDataRepository,ParameterMetaDataConstants
    grid=field(raw,'Params');firmware=MAV.MAV.cs.firmware.ToString()
    assert grid.Rows.Count==MAV.MAV.param.TotalReceived
    favorites=list(Settings.Instance.GetList('fav_params'))
    for name in ['AIRSPEED_CRUISE','SERVO5_TRIM','RC6_OPTION']:
        row=next(r for r in grid.Rows if str(r.Cells[0].Value)==name)
        param=next(p for p in MAV.MAV.param if p.Name==name)
        assert str(row.Cells[1].Value)==param.ToString()
        assert bool(row.Cells[field(raw,'Fav').Index].Value)==(name in favorites)
        description=ParameterMetaDataRepository.GetParameterMetaData(name,ParameterMetaDataConstants.Description,firmware)
        if description:
            assert str(row.Cells[field(raw,'Desc').Index].Value)==description
            units=ParameterMetaDataRepository.GetParameterMetaData(name,ParameterMetaDataConstants.Units,firmware)
            assert str(row.Cells[field(raw,'Units').Index].Value)==units
    log('row_count_and_metadata_verified='+str(grid.Rows.Count))
def edit(value):
    grid=field(raw,'Params');row=next(r for r in grid.Rows if str(r.Cells[0].Value)=='AIRSPEED_CRUISE')
    grid.CurrentCell=row.Cells[1];grid.BeginEdit(True);grid.EditingControl.Text=str(value);grid.EndEdit()
def search(value):
    field(raw,'txt_search').Text=value
    call(raw,'FilterTimerOnElapsed',[None,None])
try:
    GMaps.Instance.Mode=AccessMode.CacheOnly
    ui(connect);Thread.Sleep(1500)
    assert not MAV.MAV.cs.armed
    original=MAV.GetParam('AIRSPEED_CRUISE')
    answers.Tick+=answer;timer.Tick+=sample
    def start():
        global lasttick,inspector
        clock.Restart();lasttick=0;timer.Start();answers.Start()
        inspector=MAVLinkInspector(MAV);inspector.Show(MainV2)
    ui(start);MAV.OnPacketSent+=parameter_event;MAV.OnPacketReceived+=parameter_event
    def ftpstart():
        global ftp
        ftp=select('MavFTPUI')
    measured('ftp_open',ftpstart)
    waitfor(lambda:field(ftp,'treeView1').Nodes.Count>=2 and field(ftp,'toolStripStatusLabel1').Text=='Ready')
    Thread.Sleep(500)
    testfile=Path.Combine(output,'param-perf-transfer.bin')
    File.WriteAllBytes(testfile,Array[System.Byte]([i%251 for i in range(262144)]))
    def upload():
        global transfer
        tree=field(ftp,'treeView1');tree.SelectedNode=tree.Nodes[0]
        transfer=call(ftp,'UploadFile',[testfile])
    ui(upload);Thread.Sleep(200);assert not transfer.IsCompleted
    measured('mode_during_ftp',lambda:mode('STABILIZE'))
    waitfor(lambda:MAV.MAV.cs.mode.upper()=='STABILIZE')
    measured('first_open',openraw);ready('first_open')
    measured('verify_rows',verify_rows)
    measured('search_air',lambda:search('AIRSPEED'))
    assert any(str(r.Cells[0].Value)=='AIRSPEED_CRUISE' and r.Visible for r in field(raw,'Params').Rows)
    measured('search_clear',lambda:search(''))
    measured('first_numeric_edit',lambda:edit(original+1))
    measured('repeat_numeric_edit',lambda:edit(original+1))
    phase='confirm_and_write';log('before_confirm_elapsed_ms='+str(clock.ElapsedMilliseconds))
    ui(lambda:field(raw,'BUT_writePIDS').PerformClick())
    log('write_dispatched_elapsed_ms='+str(clock.ElapsedMilliseconds)+' ftp_running='+str(not transfer.IsCompleted))
    waitfor(lambda:not field(raw,'writingParameters'))
    log('write_finished_elapsed_ms='+str(clock.ElapsedMilliseconds)+' ftp_running='+str(not transfer.IsCompleted))
    assert not transfer.IsCompleted
    assert abs(MAV.GetParam('AIRSPEED_CRUISE')-(original+1))<.01
    measured('mode_after_write',lambda:mode('MANUAL'));waitfor(lambda:MAV.MAV.cs.mode.upper()=='MANUAL')
    measured('second_open',openraw);ready('second_open')
    measured('restore_edit',lambda:edit(original))
    phase='restore_write';ui(lambda:field(raw,'BUT_writePIDS').PerformClick());waitfor(lambda:not field(raw,'writingParameters'))
    assert abs(MAV.GetParam('AIRSPEED_CRUISE')-original)<.01
    def leave_loading():
        global cancelled_load,oldraw
        oldraw=raw
        call(raw,'Activate',[])
        cancelled_load=field(raw,'rowLoadCancellation')
        assert field(raw,'loadingParameters')
        mode('STABILIZE')
    measured('leave_loading_and_mode',leave_loading)
    waitfor(lambda:not field(oldraw,'loadingParameters'))
    assert oldraw.IsDisposed and cancelled_load.IsCancellationRequested
    waitfor(lambda:MAV.MAV.cs.mode.upper()=='STABILIZE')
    log('cancelled_page_did_not_apply=True')
    phase='ftp_completion';waitfor(lambda:transfer.IsCompleted,120)
    if transfer.IsFaulted:raise transfer.Exception
    log('ftp_crc_complete=True')
    assert not MAV.MAV.cs.armed
    log('PASS');File.WriteAllText(Path.Combine(output,'status.txt'),'0')
except Exception as ex:
    import traceback
    log('FAIL '+str(ex));log(traceback.format_exc())
finally:
    ui(lambda:timer.Stop());ui(lambda:answers.Stop())
    if inspector is not None and not inspector.IsDisposed:ui(lambda:inspector.Close())
    MAV.OnPacketSent-=parameter_event;MAV.OnPacketReceived-=parameter_event
    File.WriteAllLines(Path.Combine(output,'health.csv'),['phase,ftp_running,packet_age_ms,ui_tick_gap_ms,mode']+health)
    File.WriteAllLines(Path.Combine(output,'parameter-events.csv'),['elapsed_ms,phase,msgid,value,ftp_running']+param_events)
    log('DONE')