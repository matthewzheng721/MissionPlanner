# 仅用于本次独立本机 SITL：不解锁、不发发动机命令；写入专用仿真任务及可恢复参数以验收。
import clr
from System import Action, Array, Object, EventArgs, Environment, Activator, Int32
from System.IO import File, Directory, Path
from System.Net.Sockets import TcpClient
from System.Threading import Thread
from System.Reflection import BindingFlags
from System.Windows.Forms import Application, FormWindowState
from System.Windows.Forms import DataGridViewCellEventArgs
from System.Drawing import Size
from MissionPlanner.Comms import TcpSerial
from MissionPlanner.Utilities import Settings

output=Environment.GetEnvironmentVariable('MP_V05_OUTPUT')
Directory.CreateDirectory(output)
flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
report=[]
File.WriteAllText(Path.Combine(output,'status.txt'),'1')
def log(s):
    report.append(str(s));File.WriteAllLines(Path.Combine(output,'result.txt'),report)
def field(o,n):return o.GetType().GetField(n,flags).GetValue(o)
def call(o,n,args=[]):return o.GetType().GetMethod(n,flags).Invoke(o,Array[Object](args))
def ui(f):MainV2.Invoke(Action(f))
def capture(name):
    MainV2.PerformLayout();MainV2.Refresh()
    method=MainV2.GetType().GetMethod('DrawToBitmap');types=method.GetParameters()
    bmp=Activator.CreateInstance(types[0].ParameterType,Array[Object]([Int32(MainV2.Width),Int32(MainV2.Height)]))
    rect=Activator.CreateInstance(types[1].ParameterType,Array[Object]([Int32(0),Int32(0),Int32(MainV2.Width),Int32(MainV2.Height)]))
    try:method.Invoke(MainV2,Array[Object]([bmp,rect]));bmp.Save(Path.Combine(output,name+'.png'))
    finally:bmp.Dispose()
def structure():
    MainV2.Show();MainV2.WindowState=FormWindowState.Normal;MainV2.Size=Size(1440,900)
    field(MainV2,'MyView').ShowScreen('FlightData')
    field(FlightData,'hud1').opengl=False
    tabs=field(FlightData,'projectOperationTabs')
    assert [p.Text for p in tabs.TabPages]==['动作','航点','参数','工具']
    assert field(FlightData,'hud1').Visible
    assert field(FlightData,'tableLayoutPanelQuick').Visible
    assert field(FlightData,'gMapControl1').Visible
    assert not field(FlightData,'BUT_ARM').Enabled
    assert not field(FlightData,'projectArmButton').Enabled
    assert FlightData.Controls.Find('ProjectChecks',True).Length==1
    assert FlightData.Controls.Find('ProjectChecks',True)[0].Visible
    log('底栏='+str(FlightData.Controls.Find('ProjectFooter',True)[0].Bounds))
    log('离线三区、HUD、Quick、地图及解锁禁用：通过')
    capture('offline-workspace')
def connect(port=5890):
    stream=TcpSerial();stream.client=TcpClient('127.0.0.1',port);MAV.BaseStream=stream
    MainV2.doConnect(MAV,'preset',str(port),True,True)
def parameters():
    # 沿用用户首次参数提示的设置开关，只在独立测试数据目录中关闭提示。
    from MissionPlanner import Strings
    Settings.Instance['SHOWAGAIN_'+Strings.RawParamWarning.Replace(' ','_').Replace('+','_').Replace('-','_').Replace('.','_')]='False'
    FlightData.SelectProjectParameters()
def param_check():
    global parameterPage, parameterGrid, sampleRow, originalCell
    parameterPage=field(FlightData,'projectParameters');parameterGrid=field(parameterPage,'Params')
    visible=sorted([c for c in parameterGrid.Columns if c.Visible],key=lambda c:c.DisplayIndex)
    assert [c.Name for c in visible]==['Fav','Command','Value']
    assert parameterGrid.GetColumnDisplayRectangle(1,False).Right<=parameterGrid.ClientSize.Width
    assert parameterGrid.Width<=parameterPage.Width
    assert parameterGrid.Rows.Count>500
    sampleRow=next(r for r in parameterGrid.Rows if str(r.Cells[0].Value)=='AIRSPEED_CRUISE')
    originalCell=sampleRow.Cells[1].Value
    sampleRow.Cells[1].Value=str(float(originalCell)+1)
    tabs=field(FlightData,'projectOperationTabs');tabs.SelectedIndex=0;tabs.SelectedIndex=2
    assert field(FlightData,'projectParameters')==parameterPage
    assert float(sampleRow.Cells[1].Value)==float(originalCell)+1
    assert field(FlightData,'hud1').Visible and field(FlightData,'tableLayoutPanelQuick').Visible
    assert not field(FlightData,'BUT_ARM').Enabled
    previousIndex=sampleRow.Index
    favorite=field(parameterPage,'Fav')
    sampleRow.Cells[favorite.Index].Value=True
    call(parameterPage,'Params_CellContentClick',[parameterGrid,DataGridViewCellEventArgs(favorite.Index,sampleRow.Index)])
    assert sampleRow.Index==previousIndex
    log('编辑期间收藏不跳行：通过')
    log('真实 SITL 参数数量='+str(parameterGrid.Rows.Count)+'；三列与切页编辑保留：通过')
    capture('parameters-workspace')
def comparison():
    from System.Collections.Generic import Dictionary
    from System import String, Double
    from MissionPlanner.Controls import ParamCompare
    fixture=Dictionary[String,Double]();fixture.Add('AIRSPEED_CRUISE',float(originalCell)+2)
    current=Dictionary[String,Double]();current.Add('AIRSPEED_CRUISE',float(originalCell))
    compare=ParamCompare(parameterGrid,current,fixture)
    call(parameterPage,'ShowProjectParameterComparison',[compare])
    grid=field(compare,'Params');assert grid.Columns.Count==4
    assert compare.Visible and not compare.Modal
    field(FlightData,'projectOperationTabs').SelectedIndex=0
    assert field(FlightData,'hud1').Visible and field(FlightData,'tableLayoutPanelQuick').Visible
    field(FlightData,'projectOperationTabs').SelectedIndex=2
    call(compare,'BUT_save_Click',[None,EventArgs.Empty])
    assert float(sampleRow.Cells[1].Value)==float(originalCell)+2
    log('原四列比较非模态、切页监视、Continue 回填：通过；未写入飞控')
    sampleRow.Cells[1].Value=originalCell
def mission():
    FlightData.SelectProjectMission();call(FlightData,'ShowProjectMissionEditor')
    window=field(FlightData,'projectMissionWindow');assert window.Visible and not window.Modal
    assert field(FlightData,'hud1').Visible and field(FlightData,'tableLayoutPanelQuick').Visible
    window.Close();log('原航点编辑器非模态打开、关闭：通过')
def checks():
    from MissionPlanner.Utilities import ProjectPreflight, ProjectCheckResult
    call(FlightData,'OpenProjectChecks')
    wizard=next(f for f in Application.OpenForms if f.GetType().Name=='ProjectPreflightWizard')
    assert not wizard.Modal
    capture('checks-workspace')
    wizard.Close()
    ProjectPreflight.RefreshConnection()
    for i in range(8):
        ProjectPreflight.Checklist.Set(i,ProjectCheckResult.Confirmed)
        if i<7:assert not field(FlightData,'projectArmButton').Enabled
    assert field(FlightData,'projectArmButton').Enabled and field(FlightData,'BUT_ARM').Enabled
    ProjectPreflight.Checklist.Set(7,ProjectCheckResult.Skipped)
    assert not field(FlightData,'projectArmButton').Enabled and not field(FlightData,'BUT_ARM').Enabled
    for i in range(8):ProjectPreflight.Checklist.Set(i,ProjectCheckResult.Unchecked)
    log('仅测试检查状态：八项全核对才启用两个解锁入口；跳过立即禁用。未点击解锁。')

from System.Windows.Forms import Timer, DialogResult, Button
from GMap.NET import GMaps, AccessMode
GMaps.Instance.Mode=AccessMode.CacheOnly
answerTimer=Timer();answerTimer.Interval=50
original=None
def descendants(control):
    for child in control.Controls:
        yield child
        for item in descendants(child):yield item
def answer(sender,args):
    # Application.OpenForms 仅包含当前独立测试进程。
    for form in list(Application.OpenForms):
        if form.Text in ['Confirm Parameter Changes','Saved','No changes']:
            log('参数确认='+form.Text)
            if form.AcceptButton is not None:form.AcceptButton.PerformClick()
            else:
                for b in descendants(form):
                    if getattr(b,'DialogResult',None)==DialogResult.OK:b.PerformClick();break
answerTimer.Tick+=answer
def waitfor(predicate,description,seconds=30):
    for i in range(seconds*10):
        value=[];ui(lambda:value.append(predicate()))
        if value[0]:return
        Thread.Sleep(100)
    raise Exception('等待超时：'+description)
def raw():return field(FlightData,'projectParameters')
def grid():return field(raw(),'Params')
def speedrow():return next(r for r in grid().Rows if str(r.Cells[0].Value)=='AIRSPEED_CRUISE')
def captureform(form,name):
    form.PerformLayout();form.Refresh()
    method=form.GetType().GetMethod('DrawToBitmap');types=method.GetParameters()
    bmp=Activator.CreateInstance(types[0].ParameterType,Array[Object]([Int32(form.Width),Int32(form.Height)]))
    rect=Activator.CreateInstance(types[1].ParameterType,Array[Object]([Int32(0),Int32(0),Int32(form.Width),Int32(form.Height)]))
    try:method.Invoke(form,Array[Object]([bmp,rect]));bmp.Save(Path.Combine(output,name+'.png'))
    finally:bmp.Dispose()
def hudopen():
    global hudwindow,hudcopy,hudparent
    hud=field(FlightData,'hud1');hudparent=hud.Parent
    call(FlightData,'hud1_DoubleClick',[None,EventArgs.Empty])
    hudwindow=field(FlightData,'projectHudWindow');hudcopy=field(FlightData,'projectHudCopy')
    assert hudwindow.Visible and not hudwindow.Modal
    assert hud.Parent==hudparent and hud.Visible
    assert hudcopy.CustomItems is hud.CustomItems or hudcopy.CustomItems.Equals(hud.CustomItems)
    assert hudcopy.DataBindings.Count==hud.DataBindings.Count
def hudcheck():
    hud=field(FlightData,'hud1')
    assert hud.Parent==hudparent and hud.Visible
    for name in ['roll','pitch','airspeed','heading','alt']:
        assert abs(float(getattr(hud,name))-float(getattr(hudcopy,name)))<0.01,name
    captureform(hudwindow,'hud-copy')
    log('HUD副本：原绑定数量='+str(hud.DataBindings.Count)+'，姿态/空速/高度/航向一致，常驻HUD仍在原父容器')
def hudclose():
    hudwindow.Close()
    assert field(FlightData,'projectHudCopy') is None
    assert field(FlightData,'hud1').Parent==hudparent
    call(FlightData,'hud1_DoubleClick',[None,EventArgs.Empty]);field(FlightData,'projectHudWindow').Close()
    log('HUD独立窗口关闭/重开通过')
def wizardopen():
    global wizard,polarity
    call(FlightData,'OpenProjectChecks')
    wizard=next(f for f in Application.OpenForms if f.GetType().Name=='ProjectPreflightWizard')
    captureform(wizard,'wizard-basic')
    for index,name in [(3,'wizard-servo'),(4,'wizard-polarity'),(5,'wizard-summary')]:
        wizard.GetType().GetField('index',flags).SetValue(wizard,index);call(wizard,'Render');captureform(wizard,name)
    wizard.GetType().GetField('index',flags).SetValue(wizard,4);call(wizard,'Render')
    polarity=field(wizard,'embedded').Controls[0]
def changemode(name):
    next(b for b in descendants(polarity) if isinstance(b,Button) and b.Text==name).PerformClick()
def prepare_auto_mission():
    from MissionPlanner.Utilities import Locationwp
    from System.Collections.Generic import List
    from System import UInt16,Byte
    fp=MainV2.FlightPlanner
    call(FlightData,'ShowProjectMissionEditor')
    points=List[Locationwp]()
    for lat,lng,alt,frame in [(-35.363261,149.165230,584,0),(-35.360261,149.165230,100,3),(-35.357261,149.168230,100,3)]:
        item=Locationwp().Set(lat,lng,alt,UInt16(16));item.GetType().GetField('frame').SetValue(item,Byte(frame));points.Add(item)
    fp.WPtoScreen(points);field(fp,'chk_usemavftp').Checked=False;fp.BUT_write_Click(None,EventArgs.Empty)
def comparison_bounds():
    from System.Collections.Generic import Dictionary
    from System import String,Double
    from MissionPlanner.Controls import ParamCompare
    fixture=Dictionary[String,Double]();fixture.Add('AIRSPEED_CRUISE',float(original)+2)
    current=Dictionary[String,Double]();current.Add('AIRSPEED_CRUISE',float(original))
    MainV2.Size=Size(1100,700);MainV2.PerformLayout()
    compare=ParamCompare(grid(),current,fixture);call(raw(),'ShowProjectParameterComparison',[compare])
    region=FlightData.GetProjectOperationScreenBounds()
    assert compare.Left>=region.Left and compare.Right<=region.Right,(compare.Bounds,region)
    assert field(FlightData,'hud1').Visible and field(FlightData,'tableLayoutPanelQuick').Visible
    captureform(compare,'compare-minimum-desktop')
    compare.Close();MainV2.Size=Size(1440,900)
    log('1100×700最小桌面：比较窗口完整位于地图/操作区，未压左HUD/Quick')
def editwrite(value):
    FlightData.SelectProjectParameters()
    row=speedrow();g=grid();g.CurrentCell=row.Cells[1];g.BeginEdit(True);g.EditingControl.Text=str(value);g.EndEdit()
    field(raw(),'BUT_writePIDS').PerformClick()
def lifecycle_pending():
    global oldcompare
    from System.Collections.Generic import Dictionary
    from System import String,Double
    from MissionPlanner.Controls import ParamCompare
    speedrow().Cells[1].Value=str(original+5)
    fixture=Dictionary[String,Double]();fixture.Add('AIRSPEED_CRUISE',original+6)
    current=Dictionary[String,Double]();current.Add('AIRSPEED_CRUISE',original)
    oldcompare=ParamCompare(grid(),current,fixture);call(raw(),'ShowProjectParameterComparison',[oldcompare])
    assert field(raw(),'_changes').Count>0
def layout_save():
    main=field(FlightData,'MainH');monitor=field(FlightData,'SubMainLeft');operations=field(FlightData,'projectOperationSplit')
    main.SplitterDistance=335;monitor.SplitterDistance=270
    operations.SplitterDistance=operations.Width-390-operations.SplitterWidth
    operations.Panel2Collapsed=True;call(FlightData,'SaveProjectWorkspaceLayout');Settings.Instance.Save()
    log('布局已保存：左335/HUD270/右390/收起')
try:
    ui(structure);ui(parameters)
    assert grid().Rows.Count==0
    log('离线先打开参数：空表且未写入')
    ui(hudopen);ui(connect)
    assert MAV.MAV.param.ContainsKey('SIM_SPEEDUP') and not MAV.MAV.cs.armed
    original=float(MAV.GetParam('AIRSPEED_CRUISE'))
    waitfor(lambda:grid().Rows.Count>500 and grid().Enabled and field(raw(),'BUT_writePIDS').Enabled,'离线页连接后自动加载')
    log('离线开页→连接：自动加载 '+str(grid().Rows.Count)+' 个参数且写入恢复')
    ui(prepare_auto_mission);waitfor(lambda:not field(MainV2.FlightPlanner,'missionTransferRunning'),'AUTO场景准备两条未完成航点')
    ui(lambda:field(FlightData,'projectMissionWindow').Close())
    log('专用SITL已载入两条未完成航点，避免空任务AUTO立即转RTL')
    ui(wizardopen)
    for name in ['FBWA','AUTO']:
        ui(lambda:changemode(name));waitfor(lambda:MAV.MAV.cs.mode.upper()==name,'新极性页模式 '+name)
        ui(hudcheck);log('新极性页按钮→heartbeat='+name)
    ui(lambda:wizard.Close());ui(hudclose)
    ui(param_check);ui(comparison);ui(comparison_bounds);ui(mission);ui(checks)
    ui(lambda:answerTimer.Start())
    ui(lambda:editwrite(original+1))
    waitfor(lambda:not field(raw(),'writingParameters') and field(raw(),'_changes').Count==0,'新三列表真实写入')
    assert abs(float(MAV.GetParam('AIRSPEED_CRUISE'))-original-1)<0.01
    log('新三列表实际写入及飞控回读='+str(original+1))
    # 外部改同名参数，不改变参数总数；F5 必须更新屏幕而非只更新缓存。
    assert MAV.setParam('AIRSPEED_CRUISE',original+2)
    ui(lambda:call(MainV2,'RefreshProjectParameters'))
    waitfor(lambda:not field(MainV2,'refreshingParameters') and not field(raw(),'loadingParameters') and float(speedrow().Cells[1].Value)==original+2,'F5同数量参数实际更新')
    log('外部改值→F5：同数量参数表更新为 '+str(original+2))
    assert MAV.setParam('AIRSPEED_CRUISE',original)
    ui(lambda:call(MainV2,'RefreshProjectParameters'))
    waitfor(lambda:not field(MainV2,'refreshingParameters') and not field(raw(),'loadingParameters') and float(speedrow().Cells[1].Value)==original,'F5恢复原值')
    ui(lifecycle_pending);ui(lambda:MainV2.doDisconnect(MAV))
    waitfor(lambda:field(raw(),'_changes').Count==0 and oldcompare.IsDisposed,'断联清旧编辑/比较窗')
    ui(lambda:connect(5910))
    assert MAV.MAV.param.ContainsKey('SIM_SPEEDUP') and int(MAV.MAV.sysid)==2 and not MAV.MAV.cs.armed
    waitfor(lambda:grid().Rows.Count>500 and grid().Enabled,'B连接参数重读')
    assert field(raw(),'_changes').Count==0
    assert float(speedrow().Cells[1].Value)==float(MAV.GetParam('AIRSPEED_CRUISE'))
    log('A待写→断联→B(sysid=2)：旧修改清空，旧比较关闭，B重新读取，无沿用待写')
    ui(lambda:MainV2.doDisconnect(MAV));ui(layout_save)
    log('PASS');File.WriteAllText(Path.Combine(output,'status.txt'),'0')
except Exception as ex:
    import traceback
    log('FAIL '+traceback.format_exc())
finally:
    ui(lambda:answerTimer.Stop())
    if original is not None and MAV.BaseStream.IsOpen and int(MAV.MAV.sysid)==1:
        try:MAV.setParam('AIRSPEED_CRUISE',original)
        except Exception as ex:log('恢复失败：'+str(ex))
