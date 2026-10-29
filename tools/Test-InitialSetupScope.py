# 仅连接本机 SITL 与本机差分数据源；不刷写固件，不解锁、不起飞。
import clr
import MAVLink
from System import Action,Array,Object,EventArgs,Environment,Byte,Double,UInt32,DateTime
from System.IO import File,Directory,Path
from System.Net import IPAddress
from System.Net.Sockets import TcpClient,TcpListener
from System.Threading import Thread, ThreadStart, Monitor, ManualResetEvent
from System.Diagnostics import Stopwatch
from System.Reflection import BindingFlags
from System.Windows.Forms import Timer,Application,TextBox,DialogResult,Message,Keys,FormWindowState
from MissionPlanner.Comms import TcpSerial
from MissionPlanner.Utilities import Settings,rtcm3
from GMap.NET import GMaps,AccessMode
output=Environment.GetEnvironmentVariable('MP_INITIAL_OUTPUT') or Path.Combine(Settings.GetUserDataDirectory(),'initial-test')
Directory.CreateDirectory(output)
flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
report=[];sent=[];health=[];original=None;motor=None;rtk=None;server=None;peer=None
healthtimer=Timer();healthtimer.Interval=50
watchclock=Stopwatch.StartNew();lasttick=0
phase='setup'
File.WriteAllText(Path.Combine(output,'status.txt'),'1')
def log(s):report.append(str(s));File.WriteAllLines(Path.Combine(output,'result.txt'),report)
def field(o,n):return o.GetType().GetField(n,flags).GetValue(o)
def call(o,n,args):return o.GetType().GetMethod(n,flags).Invoke(o,Array[Object](args))
def ui(f):MainV2.Invoke(Action(f))
def setup():
    MainV2.Show();MainV2.WindowState=FormWindowState.Normal
    MainV2.MenuSetup_Click(None,EventArgs.Empty)
    return field(MainV2,'MyView').current.Control
def select(name):
    global initial
    initial=setup();back=field(initial,'backstageView')
    page=next(p for p in back.Pages if p.PageType.Name==name)
    back.ActivatePage(page)
    return page.Page
def connect():
    call(MainV2,'MenuFlightData_Click',[None,EventArgs.Empty])
    stream=TcpSerial();stream.client=TcpClient('127.0.0.1',5760);MAV.BaseStream=stream
    MainV2.doConnect(MAV,'preset','5760',True,True)
def sample(sender,args):
    global lasttick
    now=watchclock.ElapsedMilliseconds
    health.append('{},{},{},{}'.format(phase,(DateTime.UtcNow-MAV.MAV.lastvalidpacket).TotalMilliseconds,now-lasttick,MAV.MAV.cs.mode));lasttick=now
def sentpacket(sender,msg):
    if msg.msgid==23:
        p=msg.ToStructure[MAVLink.mavlink_param_set_t]()
        from System.Text import Encoding
        if Encoding.ASCII.GetString(p.param_id).split(chr(0))[0]=='SERVO5_TRIM':sent.append(float(p.param_value))
    elif msg.msgid in [123,233]:sent.append('RTCM')
def waitfor(fn,seconds=12):
    for i in range(seconds*20):
        if fn():return
        Thread.Sleep(50)
    raise Exception('condition timed out')
def answer(sender,args):
    for form in list(Application.OpenForms):
        if form.Text=='大飞机罗盘校准':
            def fill(c):
                if isinstance(c,TextBox):c.Text='0'
                for cc in c.Controls:fill(cc)
            fill(form);form.DialogResult=DialogResult.OK;form.Close()
try:
    GMaps.Instance.Mode=AccessMode.CacheOnly
    def offline():
        page=select('ConfigFirmwareManifest')
        names=[p.PageType.Name for p in field(initial,'backstageView').Pages];log('offline_pages='+','.join(names))
        assert names==['ConfigFirmwareManifest','ConfigSerialInjectGPS']
        visible=[c.Name for c in page.Controls if c.Visible];log('firmware_visible='+','.join(visible))
        assert set(visible)==set(['lbl_Custom_firmware_label','lbl_status','progress'])
    ui(offline)
    ui(connect);Thread.Sleep(2500)
    assert MAV.MAV.param.ContainsKey('SIM_SPEEDUP') and not MAV.MAV.cs.armed
    original=next(float(p.Value) for p in MAV.MAV.param if p.Name=='SERVO5_TRIM')
    def connected():
        global motor
        motor=select('ConfigProjectMotor')
        names=[p.PageType.Name for p in field(initial,'backstageView').Pages];log('connected_pages='+','.join(names))
        assert names==['ConfigFirmwareDisabled','ConfigAccelerometerCalibration','ConfigHWCompass2','ConfigProjectMotor','ConfigSerialInjectGPS']
    ui(connected)
    waitfor(lambda:'回读' in field(motor,'status').Text)
    MAV.OnPacketSent+=sentpacket
    healthtimer.Tick+=sample
    def starthealth():
        global lasttick
        watchclock.Restart();lasttick=0;healthtimer.Start()
    ui(starthealth)
    phase='F5_and_motor'
    ui(lambda:call(MainV2,'ProcessCmdKey',[Message(),Keys.F5]))
    def rapid():
        field(motor,'startButton').PerformClick();field(motor,'restoreButton').PerformClick()
    w=Stopwatch.StartNew();ui(rapid);w.Stop();log('rapid_buttons_ms='+str(w.ElapsedMilliseconds)+' refreshing='+str(field(MainV2,'refreshingParameters')))
    waitfor(lambda:'2000' in field(motor,'status').Text and not field(motor,'awaitingReadback'))
    Thread.Sleep(3200)
    log('motor_writes='+str(sent));assert sent==[1300.0,2000.0]
    assert abs(MAV.GetParam('SERVO5_TRIM')-2000)<.01
    # F5 完成时可能重建初始设置页，随后重新获取当前控件核对显示。
    waitfor(lambda:not field(MainV2,'refreshingParameters'),70)
    log('F5_status='+field(MainV2,'parameterRefreshStatus').Text)
    def reacquire():
        global motor
        current=field(MainV2,'MyView').current.Control
        motor=next(p.Page for p in field(current,'backstageView').Pages if p.PageType.Name=='ConfigProjectMotor')
    ui(reacquire)
    waitfor(lambda:'2000' in field(motor,'currentValue').Text and '2000' in field(motor,'status').Text)
    log('motor_status='+field(motor,'status').Text+' value='+field(motor,'currentValue').Text)
    phase='busy_transport'
    held=ManualResetEvent(False)
    def holdwrite():
        lock=field(MAV,'writelock');Monitor.Enter(lock)
        try:held.Set();Thread.Sleep(400)
        finally:Monitor.Exit(lock)
    holder=Thread(ThreadStart(holdwrite));holder.Start();held.WaitOne()
    w=Stopwatch.StartNew();ui(lambda:field(motor,'restoreButton').PerformClick());w.Stop()
    log('busy_transport_button_ms='+str(w.ElapsedMilliseconds));assert w.ElapsedMilliseconds<350
    holder.Join();waitfor(lambda:not field(motor,'awaitingReadback') and len(sent)==3)
    assert sent==[1300.0,2000.0,2000.0]
    def missing():
        cached=next(p for p in MAV.MAV.param if p.Name=='SERVO5_TRIM')
        MAV.MAV.param.Remove(cached)
        try:
            field(motor,'startButton').PerformClick()
            log('missing_parameter_status='+field(motor,'status').Text)
            assert '没有 SERVO5_TRIM' in field(motor,'status').Text
        finally:MAV.MAV.param.Add(cached)
    ui(missing)
    assert len(sent)==3
    ui(lambda:call(motor,'RequestRead',[]))
    waitfor(lambda:'飞控参数已回读：2000'==field(motor,'status').Text)
    if Environment.GetEnvironmentVariable('MP_INITIAL_CAPTURE')=='1':
        phase='capture'
        ui(lambda:File.WriteAllText(Path.Combine(output,'handle.txt'),str(MainV2.Handle.ToInt64())))
        log('motor_screenshot_ready')
        waitfor(lambda:File.Exists(Path.Combine(output,'capture.done')),90)
    phase='level'
    def level():
        global lev
        lev=select('ConfigAccelerometerCalibration')
        log('level_visible='+','.join(c.Name for c in lev.Controls if c.Visible))
        field(lev,'BUT_level').PerformClick()
    ui(level);waitfor(lambda:field(lev,'BUT_level').Enabled)
    log('level_status='+field(lev,'BUT_level').Text)
    assert field(lev,'BUT_level').Text=='校准水平完成'
    phase='compass'
    dialogtimer=Timer();dialogtimer.Interval=100;dialogtimer.Tick+=answer
    def compass():
        global mag
        mag=select('ConfigHWCompass2');dialogtimer.Start();field(mag,'but_largemagcal').PerformClick()
    ui(compass);waitfor(lambda:field(mag,'but_largemagcal').Enabled);ui(lambda:dialogtimer.Stop())
    log('compass_status='+field(mag,'projectStatus').Text)
    assert field(mag,'projectStatus').Text=='大飞机罗盘校准完成'
    phase='RTK'
    server=TcpListener(IPAddress.Loopback,0);server.Start()
    rtstream=TcpSerial();rtstream.client=TcpClient('127.0.0.1',server.LocalEndpoint.Port);peer=server.AcceptTcpClient()
    def rtkconnect():
        global rtk,rtktask
        rtk=select('ConfigSerialInjectGPS')
        ports=[str(x) for x in field(rtk,'CMB_serialport').Items]
        log('rtk_options='+','.join(ports))
        for name in ['UDP Host','UDP Client','TCP Client','NTRIP']:assert name in ports
        field(rtk,'chk_autoconfig').Checked=False
        rtk.GetType().GetField('comPort',BindingFlags.Static|BindingFlags.NonPublic).SetValue(None,rtstream)
        field(rtk,'CMB_serialport').Text='TCP Client'
        rtktask=rtk.DoConnect()
    ui(rtkconnect);waitfor(lambda:rtktask.IsCompleted)
    rtktask.GetAwaiter().GetResult()
    frame=Array.CreateInstance(Byte,100)
    base=rtcm3.type1005();base.ecefposition=Array[Double]([-2260000,5000000,-3600000])
    bits=base.Write(frame);length=int((bits+7)/8)
    frame[0]=0xD3;frame[1]=((length-3)>>8)&3;frame[2]=(length-3)&255
    crc=rtcm3.crc24.crc24q(frame,UInt32(length),UInt32(0))
    frame[length]=(crc>>16)&255;frame[length+1]=(crc>>8)&255;frame[length+2]=crc&255
    for i in range(4):peer.GetStream().Write(frame,0,length+3);Thread.Sleep(100)
    waitfor(lambda:'RTCM' in sent)
    log('rtcm_forwarded='+str(sent.count('RTCM')))
    ui(lambda:rtk.BUT_connect_Click(None,EventArgs.Empty))
    phase='disconnect'
    ui(connected)
    MAV.OnPacketSent-=sentpacket
    assert MAV.setParam('SERVO5_TRIM',original)
    original=None
    MAV.BaseStream.Close()
    ui(lambda:field(motor,'startButton').PerformClick())
    log('disconnected_status='+field(motor,'status').Text)
    assert '断开' in field(motor,'status').Text
    assert not MAV.MAV.cs.armed
    ui(lambda:File.WriteAllText(Path.Combine(output,'handle.txt'),str(MainV2.Handle.ToInt64())))
    log('PASS');File.WriteAllText(Path.Combine(output,'status.txt'),'0')
except Exception:
    import traceback
    log('FAIL '+traceback.format_exc())
finally:
    ui(lambda:healthtimer.Stop())
    File.WriteAllLines(Path.Combine(output,'telemetry.csv'),['phase,packet_age_ms,ui_tick_gap_ms,mode']+health)
    try:MAV.OnPacketSent-=sentpacket
    except:pass
    if original is not None and MAV.BaseStream.IsOpen:
        try:MAV.setParam('SERVO5_TRIM',original)
        except Exception as ex:log('FAIL restore '+str(ex));File.WriteAllText(Path.Combine(output,'status.txt'),'1')
    if peer is not None:peer.Close()
    if server is not None:server.Stop()