# 仅用于本次独立本机 SITL 实例；不解锁、不发发动机命令、不写飞控参数。
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
def connect():
    stream=TcpSerial();stream.client=TcpClient('127.0.0.1',5890);MAV.BaseStream=stream
    MainV2.doConnect(MAV,'preset','5890',True,True)
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
try:
    ui(structure);ui(connect)
    assert MAV.MAV.param.ContainsKey('SIM_SPEEDUP') and not MAV.MAV.cs.armed
    log('仅连接新建本机 SITL 127.0.0.1:5890，未解锁')
    for i in range(100):
        ready=[]
        ui(lambda:ready.append('回读' in field(field(FlightData,'projectMotor'),'currentValue').Text and '尚未' not in field(field(FlightData,'projectMotor'),'currentValue').Text))
        if ready[0]:break
        Thread.Sleep(100)
    ui(lambda:log('电机区只读反馈='+field(field(FlightData,'projectMotor'),'currentValue').Text))
    ui(lambda:capture('actions-workspace'))
    ui(parameters)
    for i in range(200):
        ready=[]
        ui(lambda:ready.append(field(FlightData,'projectParameters') is not None and field(field(FlightData,'projectParameters'),'Params').Enabled))
        if ready[0]:break
        Thread.Sleep(100)
    ui(param_check);ui(comparison);ui(mission);ui(checks)
    ui(lambda:MainV2.doDisconnect(MAV))
    log('PASS');File.WriteAllText(Path.Combine(output,'status.txt'),'0')
except Exception as ex:
    import traceback
    log('FAIL '+str(ex));log(traceback.format_exc())
