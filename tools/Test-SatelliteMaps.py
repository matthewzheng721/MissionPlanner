import clr
# 通过 MP 的 IronPython -script 入口验证两种影像；不连接飞控。
from System import Action, Array, Object, Environment, EventArgs
from System.IO import File, Directory, Path
from System.Reflection import BindingFlags
from System.Threading import Thread
from System.Diagnostics import Stopwatch
from System.Windows.Forms import FormWindowState, Application, Timer, TextBox, DialogResult
from MissionPlanner.Utilities import Settings
from GMap.NET import GPoint, PointLatLng, RectLatLng, GMaps, AccessMode
from GMap.NET.MapProviders import GoogleSatelliteMapProvider, BingSatelliteMapProvider
from GMap.NET import TilePrefetcher
latitude=float(Environment.GetEnvironmentVariable('MP_MAP_LAT') or '-35.363261')
longitude=float(Environment.GetEnvironmentVariable('MP_MAP_LNG') or '149.165230')
zoom=int(Environment.GetEnvironmentVariable('MP_MAP_ZOOM') or '15')
output=Environment.GetEnvironmentVariable('MP_MAP_OUTPUT') or Path.Combine(Settings.GetUserDataDirectory(),'map-test')
Directory.CreateDirectory(output)
lines=[]
flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public
fp=MainV2.FlightPlanner
maps=GMaps.Instance
oldmode=maps.Mode
oldmemory=maps.UseMemoryCache
prefetch=None
def log(s):
    lines.append(str(s));File.WriteAllLines(Path.Combine(output,'result.txt'),lines)
def field(o,n):return o.GetType().GetField(n,flags).GetValue(o)
def ui(f):MainV2.Invoke(Action(f))
def setup():
    MainV2.Show();MainV2.WindowState=FormWindowState.Normal
    MainV2.GetType().GetMethod('MenuFlightPlanner_Click',flags).Invoke(MainV2,Array[Object]([None,EventArgs.Empty]))
    combo=field(fp,'comboBoxMapType')
    assert combo.Items.Count==2 and combo.Enabled
    log('startup='+field(fp,'MainMap').MapProvider.Name+' saved='+Settings.Instance['MapType'])
    if Environment.GetEnvironmentVariable('MP_MAP_EXPECT_BING')=='1':
        assert combo.SelectedItem==BingSatelliteMapProvider.Instance
        assert FlightData.mymap.MapProvider==BingSatelliteMapProvider.Instance
    field(fp,'MainMap').Position=PointLatLng(latitude,longitude)
    field(fp,'MainMap').Zoom=zoom
File.WriteAllText(Path.Combine(output,'status.txt'),'1')
try:
    ui(setup)
    for provider,key in [(GoogleSatelliteMapProvider.Instance,'GoogleSatelliteMap'),(BingSatelliteMapProvider.Instance,'BingSatelliteMap')]:
        def choose():
            field(fp,'comboBoxMapType').SelectedItem=provider
            assert field(fp,'MainMap').MapProvider==provider and FlightData.mymap.MapProvider==provider
            assert Settings.Instance['MapType']==key
        watch=Stopwatch.StartNew();ui(choose);watch.Stop()
        log(key+' switch_ms='+str(watch.ElapsedMilliseconds))
        try:
            pos=provider.Projection.FromPixelToTileXY(provider.Projection.FromLatLngToPixel(latitude,longitude,zoom))
            log(key+' version='+str(provider.Version)+' tile='+str(pos)+' zoom='+str(zoom))
            tile=provider.GetTileImage(pos,zoom)
            assert tile is not None
            File.WriteAllBytes(Path.Combine(output,key+'.jpg'),tile.Data.ToArray())
            log(key+' online_bytes='+str(tile.Data.Length));tile.Dispose()
        except Exception as ex:
            log(key+' online_error='+str(ex))
            if provider==BingSatelliteMapProvider.Instance:raise
    def startprefetch():
        global prefetch,task
        prefetch=TilePrefetcher()
        task=prefetch.StartNonModal(RectLatLng(-35.36,149.16,.001,.001),15,BingSatelliteMapProvider.Instance,0,0)
    ui(startprefetch)
    for i in range(600):
        if task.IsCompleted:break
        Thread.Sleep(100)
    assert task.IsCompleted
    task.GetAwaiter().GetResult()
    log('bing_prefetch='+str(prefetch.CompletedTiles)+'/'+str(prefetch.TotalTiles))
    assert prefetch.CompletedTiles==prefetch.TotalTiles and prefetch.CompletedTiles>0
    maps.Mode=AccessMode.CacheOnly;maps.UseMemoryCache=False
    for pos in BingSatelliteMapProvider.Instance.Projection.GetAreaTileList(RectLatLng(-35.36,149.16,.001,.001),15,0):
        tile,error=maps.GetImageFrom(BingSatelliteMapProvider.Instance,pos,15)
        assert tile is not None
        tile.Dispose()
    log('bing_disk_cache=PASS')
    maps.Mode=oldmode;maps.UseMemoryCache=oldmemory
    # 通过实际菜单验证区域预下载使用当前选择的必应影像。
    def answer(sender,args):
        for form in list(Application.OpenForms):
            if form.Text in ['max zoom','min zoom']:
                def fill(c):
                    if isinstance(c,TextBox):c.Text='15'
                    for child in c.Controls:fill(child)
                fill(form);form.DialogResult=DialogResult.OK;form.Close()
    timer=Timer();timer.Interval=100;timer.Tick+=answer
    def menu_prefetch():
        timer.Start();field(fp,'MainMap').SelectedArea=RectLatLng(-35.36,149.16,.001,.001)
        field(fp,'prefetchToolStripMenuItem').PerformClick()
    ui(menu_prefetch)
    for i in range(600):
        if not field(fp,'prefetchRunning'):break
        Thread.Sleep(100)
    ui(lambda:timer.Stop())
    message=field(fp,'lbl_status').Text
    log('bing_menu_prefetch='+message)
    assert not field(fp,'prefetchRunning') and '1/1' in message
    Settings.Instance.Save()
    Thread.Sleep(8000)
    log('PASS selection_bing_cache; Google online result above');File.WriteAllText(Path.Combine(output,'status.txt'),'0')
except Exception:
    import traceback
    log('FAIL '+traceback.format_exc())
finally:
    maps.Mode=oldmode;maps.UseMemoryCache=oldmemory