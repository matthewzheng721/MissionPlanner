# 仅验证本次独立数据目录的布局恢复，不连接任何飞控。
from System import Action,Environment
from System.IO import File,Path
from System.Reflection import BindingFlags
from System.Windows.Forms import FormWindowState
from System.Drawing import Size
from System.Threading import Thread
flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
output=Environment.GetEnvironmentVariable('MP_V05_OUTPUT')
def field(o,n):return o.GetType().GetField(n,flags).GetValue(o)
def verify():
    MainV2.WindowState=FormWindowState.Normal;MainV2.Size=Size(1440,900)
    main=field(FlightData,'MainH');monitor=field(FlightData,'SubMainLeft');operations=field(FlightData,'projectOperationSplit')
    assert main.SplitterDistance==335,main.SplitterDistance
    assert monitor.SplitterDistance==270,monitor.SplitterDistance
    assert operations.Panel2Collapsed
    operations.Panel2Collapsed=False
    assert abs(operations.Panel2.Width-390)<=1,operations.Panel2.Width
    File.WriteAllText(Path.Combine(output,'layout-restore.txt'),'PASS：新进程从相同 Settings 恢复左335、HUD270、右390、收起状态。')
try:
    Thread.Sleep(500)
    MainV2.Invoke(Action(verify))
except Exception as ex:
    import traceback
    File.WriteAllText(Path.Combine(output,'layout-restore.txt'),'FAIL '+traceback.format_exc())
