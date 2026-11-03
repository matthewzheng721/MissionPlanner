# 仅连接本机 SITL；验证任务编辑、文件往返、飞控任务传输和必要操作重叠。
import clr
from System import Action, DateTime, Array, Object, EventArgs, Environment, UInt16, Byte
from System.IO import File, Directory, Path
from System.Net.Sockets import TcpClient
from System.Threading import Thread
from System.Diagnostics import Stopwatch
from System.Reflection import BindingFlags
from System.Collections.Generic import List
from System.Windows.Forms import Application, FormWindowState, DataGridViewCellEventArgs, DialogResult, Timer, TextBox
from MissionPlanner.Comms import TcpSerial
from MissionPlanner.Utilities import Locationwp, MissionFile, Settings
from GMap.NET import PointLatLng, RectLatLng

output=Environment.GetEnvironmentVariable('MP_PLANNER_OUTPUT') or Path.Combine(Settings.GetUserDataDirectory(),'planner-test')
Directory.CreateDirectory(output)
result=Path.Combine(output,'result.txt')
status=Path.Combine(output,'status.txt')
File.WriteAllText(status,'1')
report=[]; rows=['utc,phase,transfer_running,packet_age_ms,ui_latency_ms,mode']
flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
fp=MainV2.FlightPlanner
original=None
health=[]; healthphase='prepare'; healthwatch=Stopwatch.StartNew(); lasttick=0
healthtimer=Timer();healthtimer.Interval=50
def healthtick(sender,args):
    global lasttick
    now=healthwatch.ElapsedMilliseconds
    health.append('{},{},{},{},{}'.format(DateTime.UtcNow.ToString('o'),healthphase,field(fp,'missionTransferRunning'),(DateTime.UtcNow-MAV.MAV.lastvalidpacket).TotalMilliseconds,now-lasttick))
    lasttick=now
healthtimer.Tick+=healthtick
def starthealth():
    global lasttick
    healthwatch.Restart();lasttick=0;healthtimer.Start()

def log(value):
    report.append(str(value));File.WriteAllLines(result,report)
def field(obj,name):return obj.GetType().GetField(name,flags).GetValue(obj)
def setfield(obj,name,value):obj.GetType().GetField(name,flags).SetValue(obj,value)
def call(obj,name,args):return obj.GetType().GetMethod(name,flags).Invoke(obj,Array[Object](args))
def ui(fn):MainV2.Invoke(Action(fn))
def pv(name):return next(float(p.Value) for p in MAV.MAV.param if p.Name==name)
def showplanner():call(FlightData,'ShowProjectMissionEditor',[])
def showdata():call(MainV2,'MenuFlightData_Click',[None,EventArgs.Empty])
def connect():
    stream=TcpSerial();stream.client=TcpClient('127.0.0.1',5890);MAV.BaseStream=stream
    MainV2.doConnect(MAV,'preset','5890',True,True)
def point(lat,lng,alt,frame=3):
    # IronPython 中值类型字段赋值会写入临时装箱；使用原 Set 方法返回完整结构体。
    boxed=Locationwp().Set(lat,lng,alt,UInt16(16))
    boxed.GetType().GetField('frame').SetValue(boxed,Byte(frame))
    return boxed
def signature(items):
    return [(int(w.id),int(w.frame),round(float(w.lat),6),round(float(w.lng),6),round(float(w.alt),3),float(w.p1),float(w.p2),float(w.p3),float(w.p4)) for w in items]
def getcommands():return call(fp,'GetCommandList',[])
def structure():
    MainV2.Show();MainV2.WindowState=FormWindowState.Normal;showplanner()
    menu=field(fp,'contextMenuStrip1')
    names=[m.Name for m in menu.Items]
    log('menu='+','.join(names))
    assert names==['deleteWPToolStripMenuItem','insertWpToolStripMenuItem','clearMissionToolStripMenuItem','setHomeHereToolStripMenuItem','mapToolToolStripMenuItem','fileLoadSaveToolStripMenuItem']
    assert [m.Name for m in field(fp,'mapToolToolStripMenuItem').DropDownItems]==['ContextMeasure','prefetchToolStripMenuItem']
    assert [m.Name for m in field(fp,'fileLoadSaveToolStripMenuItem').DropDownItems]==['loadWPFileToolStripMenuItem','saveWPFileToolStripMenuItem']
    assert field(fp,'comboBoxMapType').Items.Count==2
    assert field(fp,'cmb_missiontype').Items.Count==1
    log('map='+field(fp,'MainMap').MapProvider.Name)
    log('commands='+str(field(fp,'Command').Items.Count))
    for name in ['CHK_splinedefault','BUT_InjectCustomMap','lnk_kml','chk_grid']:
        assert not field(fp,name).Visible,name
    assert field(fp,'coords1').Visible
    assert fp.Visible and field(fp,'MainMap').Visible
    from System import Activator,Int32
    missionwindow=field(FlightData,'projectMissionWindow')
    method=missionwindow.GetType().GetMethod("DrawToBitmap");types=method.GetParameters()
    bmp=Activator.CreateInstance(types[0].ParameterType,Array[Object]([Int32(missionwindow.Width),Int32(missionwindow.Height)]))
    rect=Activator.CreateInstance(types[1].ParameterType,Array[Object]([Int32(0),Int32(0),Int32(missionwindow.Width),Int32(missionwindow.Height)]))
    method.Invoke(missionwindow,Array[Object]([bmp,rect]));bmp.Save(Path.Combine(output,'mission-window.png'));bmp.Dispose()
    log('原航点控件/坐标/地图均实际可见，已截取航点窗口自身')
    cmds=List[Locationwp]();cmds.Add(point(-35.363261,149.165230,584,0))
    for i in range(180):cmds.Add(point(-35.363261+i*0.00001,149.165230+i*0.00001,80+(i%5)))
    log('input_ids='+','.join(str(x.id) for x in list(cmds)[:3]))
    log('multipliers='+','.join(field(fp,'cmdParamMultipliers').Keys))
    fp.WPtoScreen(cmds)
    grid=field(fp,'Commands')
    log('actual_rows='+str(grid.Rows.Count)+' first_command='+str(grid.Rows[0].Cells[0].Value))
    assert grid.Rows.Count==180
    grid.Rows[0].Cells[field(fp,'Alt').Index].Value='93'
    # 实际调用表内上下移动，验证往返后编辑值仍在首航点。
    fp.Commands_CellContentClick(grid,DataGridViewCellEventArgs(field(fp,'Down').Index,0))
    fp.Commands_CellContentClick(grid,DataGridViewCellEventArgs(field(fp,'Up').Index,1))
    assert float(grid.Rows[0].Cells[field(fp,'Alt').Index].Value)==93
    grid.Rows[1].Cells[field(fp,'Frame').Index].Value=0
    grid.Rows[1].Cells[field(fp,'Alt').Index].Value='690'
    # 与原保存/加载按钮相同的 MissionFile 序列化和表格导入路径。
    save=getcommands();home=point(float(field(fp,'TXT_homelat').Text),float(field(fp,'TXT_homelng').Text),float(field(fp,'TXT_homealt').Text),0);save.Insert(0,home)
    path=Path.Combine(output,'roundtrip.mission')
    MissionFile.WriteFile(path,MissionFile.ConvertFromLocationwps(save))
    expected=signature(getcommands())
    grid.Rows.Clear()
    fp.WPtoScreen(MissionFile.ConvertToLocationwps(MissionFile.ReadFile(path)))
    loaded=signature(getcommands())
    if loaded!=expected:log('file_diff counts='+str(len(expected))+','+str(len(loaded))+' first='+str(next(((a,b) for a,b in zip(expected,loaded) if a!=b),None)))
    assert loaded==expected
    log('offline_roundtrip='+str(len(expected)))
    bmp=Activator.CreateInstance(types[0].ParameterType,Array[Object]([Int32(missionwindow.Width),Int32(missionwindow.Height)]))
    method.Invoke(missionwindow,Array[Object]([bmp,rect]));bmp.Save(Path.Combine(output,'mission-window.png'));bmp.Dispose()
    field(fp,'chk_usemavftp').Checked=False

def sample(phase):
    watch=Stopwatch.StartNew();ui(lambda:None);watch.Stop()
    rows.append('{},{},{},{},{},{}'.format(DateTime.UtcNow.ToString('o'),phase,field(fp,'missionTransferRunning'),(DateTime.UtcNow-MAV.MAV.lastvalidpacket).TotalMilliseconds,watch.ElapsedMilliseconds,MAV.MAV.cs.mode))
    File.WriteAllLines(Path.Combine(output,'telemetry.csv'),rows)
def mode(name):
    showdata()
    field(FlightData,'projectOperationSplit').Panel2Collapsed=False
    field(FlightData,'projectOperationTabs').SelectedIndex=0
    field(FlightData,'projectActionTabs').SelectedIndex=0
    field(FlightData,'CMB_modes').Text=name
    assert field(FlightData,'BUT_setmode').Visible and field(FlightData,'BUT_setmode').Enabled
    field(FlightData,'BUT_setmode').PerformClick()
def waittransfer(phase):
    for i in range(900):
        sample(phase)
        if not field(fp,'missionTransferRunning'):return
        Thread.Sleep(50)
    raise Exception('mission transfer did not finish')
def alltexts(control):
    values=[control.Text]
    for c in control.Controls:values.extend(alltexts(c))
    return values
measure=[]
def dialogs(sender,args):
    for form in list(Application.OpenForms):
        if form==MainV2:continue
        texts=alltexts(form)
        if form.Text in ['max zoom','min zoom']:
            def fill(c):
                if isinstance(c,TextBox):c.Text='0'
                for child in c.Controls:fill(child)
            fill(form);form.DialogResult=DialogResult.OK;form.Close()
        elif form.Text=='Measure Dist' or any('Distance:' in t for t in texts):
            measure.extend([t for t in texts if 'Distance:' in t]);form.DialogResult=DialogResult.OK;form.Close()
try:
    from GMap.NET import GMaps,AccessMode
    GMaps.Instance.Mode=AccessMode.CacheOnly
    ui(connect);Thread.Sleep(2000)
    assert MAV.MAV.param.ContainsKey('SIM_SPEEDUP') and not MAV.MAV.cs.armed
    original=pv('AIRSPEED_CRUISE')
    ui(structure)
    expected=signature(getcommands())
    ui(starthealth)
    healthphase='upload'
    ui(lambda:fp.BUT_write_Click(None,EventArgs.Empty))
    assert field(fp,'missionTransferRunning')
    log('upload_started_main_enabled='+str(MainV2.Enabled)+' planner_enabled='+str(fp.Enabled))
    watch=Stopwatch.StartNew();ui(lambda:mode('STABILIZE'));watch.Stop()
    log('mode_during_upload_ms='+str(watch.ElapsedMilliseconds)+' transfer_after='+str(field(fp,'missionTransferRunning')))
    log('write_overlap_before='+str(field(fp,'missionTransferRunning')))
    healthphase='parameter_write'
    watch=Stopwatch.StartNew();ok=MAV.setParam('AIRSPEED_CRUISE',original+1);watch.Stop()
    log('parameter_write_ok='+str(ok)+' ms='+str(watch.ElapsedMilliseconds)+' transfer_after='+str(field(fp,'missionTransferRunning')));assert ok
    healthphase='upload_after_write'
    waittransfer('upload')
    log('mode_readback='+MAV.MAV.cs.mode+' parameter_readback='+str(MAV.GetParam('AIRSPEED_CRUISE')))
    assert MAV.MAV.cs.mode.upper()=='STABILIZE'
    assert abs(MAV.GetParam('AIRSPEED_CRUISE')-(original+1))<.01
    def download():
        showplanner();field(fp,'Commands').Rows.Clear();fp.BUT_read_Click(None,EventArgs.Empty)
    healthphase='download'
    ui(download);waittransfer('download')
    actual=signature(getcommands())
    assert actual==expected, 'task mismatch '+str(len(actual))+' vs '+str(len(expected))
    log('sitl_roundtrip='+str(len(actual)))
    log('本轮只回归文件/任务传输/并发模式与参数；不执行外网地图测试')
    assert not MAV.MAV.cs.armed
    log('PASS');File.WriteAllText(status,'0')
except Exception as ex:
    import traceback
    log('FAIL '+traceback.format_exc())
finally:
    ui(lambda:healthtimer.Stop())
    File.WriteAllLines(Path.Combine(output,'continuous.csv'),['utc,phase,transfer_running,packet_age_ms,ui_tick_gap_ms']+health)
    if original is not None and MAV.BaseStream.IsOpen:
        try:MAV.setParam('AIRSPEED_CRUISE',original)
        except Exception as ex:log('FAIL restore '+str(ex));File.WriteAllText(status,'1')





