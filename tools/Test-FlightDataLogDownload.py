# -*- coding: utf-8 -*-
# 本机 SITL：验证脚本事件订阅存在时的 DataFlash 下载，不解锁、不起飞。
import clr
import MAVLink
from System import Action,Array,Object,EventArgs,Environment,DateTime
from System.IO import File,Directory,Path,FileInfo,FileSystemWatcher,FileMode,FileAccess,FileShare
from System.Net.Sockets import TcpClient
from System.Threading import Thread
from System.Diagnostics import Stopwatch
from System.Reflection import BindingFlags
from System.Windows.Forms import FormWindowState,Application
from MissionPlanner.Comms import TcpSerial
from MissionPlanner.Utilities import Settings
out=Environment.GetEnvironmentVariable('MP_FD_DETAIL_OUTPUT') or Path.Combine(Settings.GetUserDataDirectory(),'flightdata-detail')
Directory.CreateDirectory(out)
flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
report=[];events=[];samples=[]
def log(s):report.append(str(s));File.WriteAllLines(Path.Combine(out,'retest-result.txt'),report)
def field(o,n):return o.GetType().GetField(n,flags).GetValue(o)
def call(o,n,args):return o.GetType().GetMethod(n,flags).Invoke(o,Array[Object](args))
def ui(f):MainV2.Invoke(Action(f))
def waitfor(fn,seconds=30):
    for i in range(seconds*10):
        if fn():return
        Thread.Sleep(100)
    raise Exception('condition timed out')
def children(o):
    for c in o.Controls:
        yield c
        for x in children(c):yield x
def tab(name):field(FlightData,'tabControlactions').SelectedTab=field(FlightData,name)
def packet(sender,m):
    if m.msgid==77:
        p=m.ToStructure[MAVLink.mavlink_command_ack_t]();events.append('ACK {},{}'.format(p.command,p.result))
    elif m.msgid==119:events.append('LOG_REQUEST_DATA')
    elif m.msgid==120:events.append('LOG_DATA')
def connect():
    MainV2.Show();MainV2.WindowState=FormWindowState.Normal
    s=TcpSerial();s.client=TcpClient('127.0.0.1',5760);MAV.BaseStream=s
    MainV2.doConnect(MAV,'preset','5760',True,True)
def mode(name):
    tab('tabActions');field(FlightData,'CMB_modes').Text=name;field(FlightData,'BUT_setmode').PerformClick()
File.WriteAllText(Path.Combine(out,'retest-status.txt'),'1')
original=None;watcher=None;held=[]
try:
    Settings.Instance.LogDir=Path.Combine(out,'logs',DateTime.UtcNow.ToString('yyyyMMdd-HHmmss'));Directory.CreateDirectory(Settings.Instance.LogDir)
    ui(connect);Thread.Sleep(5000)
    assert MAV.MAV.param.ContainsKey('SIM_SPEEDUP') and not MAV.MAV.cs.armed
    # 先验证上一轮真实设置保存后的重新初始化。
    ui(lambda:tab('tabQuick'))
    qs=[c for c in children(field(FlightData,'tabQuick')) if c.GetType().Name=='QuickView']
    log('quick_restarted='+str([(q.Name,str(q.Tag)) for q in qs]))
    log('HUD_restarted_cells='+str(field(FlightData,'hud1').batterycellcount)+' single='+str(field(FlightData,'hud1').displayCellVoltage)+' custom='+str(field(FlightData,'hud1').CustomItems.ContainsKey('alt')))
    assert len(qs)==4 and field(FlightData,'hud1').batterycellcount==4 and field(FlightData,'hud1').displayCellVoltage and field(FlightData,'hud1').CustomItems.ContainsKey('alt')
    MAV.OnPacketReceived+=packet;MAV.OnPacketSent+=packet
    ui(lambda:mode('STABILIZE'));waitfor(lambda:MAV.MAV.cs.mode.upper()=='STABILIZE')
    def auxbutton(button):
        tab('tabAuxFunction');a=next(c for c in children(FlightData) if c.GetType().Name=='AuxOptions')
        field(a,'mavlinkComboBox1').SelectedValue=51;field(a,button).PerformClick()
    for button in ['BUT_Low','but_mid','BUT_High']:
        ui(lambda:auxbutton(button));Thread.Sleep(400);log(button+'_mode='+MAV.MAV.cs.mode)
    assert events.count('ACK 218,0')==3
    waitfor(lambda:MAV.MAV.cs.mode.upper()=='MANUAL')
    log('AUX_ack_and_high_manual=PASS')
    Settings.Instance.Save()
    ui(lambda:call(MainV2,'ShowConnectionStatsForm',[]));Thread.Sleep(6500)
    stats=field(MainV2,'_connectionStats')
    names=['txt_BytesReceived','txt_PacketsRx','txt_PacketsPerSecond','txt_BytesPerSecondRx','txt_LinkQuality','txt_BytesPerSecondSent']
    log('stats='+str([(n,field(stats,n).Text) for n in names]));assert all(field(stats,n).Text for n in names)
    ui(lambda:field(MainV2,'connectionStatsForm').Close())
    # 模拟后台预览读取新 BIN；只有最终发布后才出现 BIN，读者不会挡住重命名。
    watcher=FileSystemWatcher(Settings.Instance.LogDir,'*.bin');watcher.IncludeSubdirectories=True
    def read_new_bin(sender,args):
        held.append(File.Open(args.FullPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
    watcher.Created+=read_new_bin;watcher.EnableRaisingEvents=True
    ui(lambda:tab('tablogbrowse'));ui(lambda:field(FlightData,'BUT_DFMavlink').PerformClick())
    dl=next(f for f in Application.OpenForms if f.GetType().Name=='LogDownloadMavLink')
    waitfor(lambda:str(field(dl,'status'))=='Done',30)
    entries=field(dl,'logEntries');assert entries.Count>0
    ui(lambda:field(dl,'CHK_logs').SetItemChecked(0,True))
    ui(lambda:field(dl,'BUT_DLthese').PerformClick())
    log('download_active_before_mode='+str(not field(dl,'BUT_DLthese').Enabled))
    ui(lambda:mode('STABILIZE'));waitfor(lambda:MAV.MAV.cs.mode.upper()=='STABILIZE')
    original=next(float(p.Value) for p in MAV.MAV.param if p.Name=='AIRSPEED_CRUISE')
    log('download_active_before_param='+str(not field(dl,'BUT_DLthese').Enabled))
    watch=Stopwatch.StartNew();assert MAV.setParam('AIRSPEED_CRUISE',original+1)
    log('param_write_ms='+str(watch.ElapsedMilliseconds));assert abs(MAV.GetParam('AIRSPEED_CRUISE')-original-1)<0.01
    assert MAV.setParam('AIRSPEED_CRUISE',original);original=None
    for i in range(600):
        watch=Stopwatch.StartNew();ui(lambda:None)
        samples.append('{},{},{}'.format((DateTime.UtcNow-MAV.MAV.lastvalidpacket).TotalMilliseconds,watch.ElapsedMilliseconds,not field(dl,'BUT_DLthese').Enabled))
        if field(dl,'BUT_DLthese').Enabled:break
        Thread.Sleep(100)
    assert field(dl,'BUT_DLthese').Enabled
    filename=field(dl,'logfile');assert File.Exists(filename) and FileInfo(filename).Length==entries[0].size
    assert 'LOG_REQUEST_DATA' in events and 'LOG_DATA' in events
    Thread.Sleep(300);assert len(held)>0
    log('background_reader=PASS count='+str(len(held)))
    log('subscribed_download=PASS bytes='+str(FileInfo(filename).Length)+' path='+filename)
    log('PASS');File.WriteAllText(Path.Combine(out,'retest-status.txt'),'0')
except:
    import traceback
    log('FAIL '+traceback.format_exc())
finally:
    if watcher is not None:watcher.Dispose()
    for stream in held:stream.Dispose()
    if original is not None and MAV.BaseStream.IsOpen:MAV.setParam('AIRSPEED_CRUISE',original)
    MAV.OnPacketReceived-=packet;MAV.OnPacketSent-=packet
    File.WriteAllLines(Path.Combine(out,'retest-events.txt'),events)
    File.WriteAllLines(Path.Combine(out,'retest-health.csv'),['packet_age_ms,ui_wait_ms,download_active']+samples)
