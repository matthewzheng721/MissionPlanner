using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using MissionPlanner.ArduPilot;
using MissionPlanner.Comms;
using MissionPlanner.GCSViews.ConfigurationView;
using MissionPlanner.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace MissionPlannerTests.Utilities
{
    [TestClass, DoNotParallelize]
    public class ProjectFavPersistenceTests
    {
        private const string First = "AIRSPEED_CRUISE";
        private const string Second = "PTCH_TRIM_DEG";
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        private static void OnUi(Action<Form> action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                EventHandler<FirstChanceExceptionEventArgs> diagnostic = (sender, args) =>
                {
                    // 原页面会捕获建表异常并提示；保留最初堆栈，避免提示宿主错误遮住根因。
                    if ((args.Exception.StackTrace ?? "").Contains("ConfigRawParams"))
                        Console.WriteLine("原参数表首次异常：" + args.Exception);
                };
                AppDomain.CurrentDomain.FirstChanceException += diagnostic;
                try
                {
                // 原表格的异步建行和编辑事件需要真实消息循环，不启动主程序或外部连接。
                using (var host = new Form { ShowInTaskbar = false, Opacity = 0, Width = 800, Height = 600 })
                {
                    host.Shown += (sender, args) => host.BeginInvoke((Action)(() =>
                    {
                        try { action(host); }
                        catch (Exception error) { failure = error; }
                        finally { host.Close(); }
                    }));
                    Application.Run(host);
                }
                }
                finally { AppDomain.CurrentDomain.FirstChanceException -= diagnostic; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(30000), "收藏测试消息循环超时。");
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private sealed class Scope : IDisposable
        {
            private readonly FieldInfo singleton = typeof(Settings).GetField("_instance", StaticPrivate);
            private readonly FieldInfo mainPort = typeof(MainV2).GetField("_comPort", StaticPrivate);
            private readonly FieldInfo startup = typeof(ConfigRawParams).GetField("startup", StaticPrivate);
            private readonly FieldInfo rowlist = typeof(ConfigRawParams).GetField("rowlist", StaticPrivate);
            private readonly string previousRoot = Settings.RuntimeDataDirectory;
            private readonly string previousFile = Settings.FileName;
            private readonly Dictionary<string, string> previousConfig = Settings.config;
            private readonly object previousSingleton;
            private readonly object previousPort;
            private readonly object previousStartup;
            private readonly object previousRows;
            private readonly Form host;
            private readonly MAVLinkInterface port;
            private readonly CommsInjection stream;
            private int writes;
            public readonly string Root = Path.Combine(Path.GetTempPath(), "MP-fav-test-" + Guid.NewGuid());
            public ConfigRawParams Page { get; private set; }
            public DataGridView Grid => (DataGridView)typeof(ConfigRawParams).GetField("Params", InstancePrivate).GetValue(Page);
            public string ConfigPath => Path.Combine(Root, "config", "config.xml");

            public Scope(Form form)
            {
                host = form;
                previousSingleton = singleton.GetValue(null);
                previousPort = mainPort.GetValue(null);
                previousStartup = startup.GetValue(null);
                previousRows = rowlist.GetValue(null);
                try
                {
                Settings.SetRuntimeDataDirectory(Root);
                Settings.FileName = "config.xml";
                Settings.config = new Dictionary<string, string>();
                singleton.SetValue(null, null);
                // 先隔离配置，再初始化原通信对象；内存流仅计数，绝不接触真实设备。
                rowlist.SetValue(null, new List<DataGridViewRow>());
                port = new MAVLinkInterface();
                stream = new CommsInjection();
                stream.WriteCallback += (sender, bytes) => Interlocked.Increment(ref writes);
                port.BaseStream = stream;
                port.sysidcurrent = 1;
                port.compidcurrent = 1;
                port.MAV.cs.firmware = Firmwares.ArduPlane;
                port.MAV.param.Add(new MAVLink.MAVLinkParam(First, 22, MAVLink.MAV_PARAM_TYPE.REAL32));
                port.MAV.param.Add(new MAVLink.MAVLinkParam(Second, 1, MAVLink.MAV_PARAM_TYPE.REAL32));
                mainPort.SetValue(null, port);
                OpenPage();
                }
                catch
                {
                    // using 尚未接管构造失败的对象，必须在这里恢复单例、配置和通信静态引用。
                    Dispose();
                    throw;
                }
            }

            private void OpenPage()
            {
                Page = new ConfigRawParams { Dock = DockStyle.Fill };
                Page.ConfigureProjectWorkspace();
                host.Controls.Add(Page);
                Page.CreateControl();
                // 与原 Activate 第97行一致：项目模式不构建已隐藏的旧参数树。
                ((SplitContainer)typeof(ConfigRawParams).GetField("splitContainer1", InstancePrivate).GetValue(Page)).Panel1Collapsed = true;
                startup.SetValue(null, true);
                var task = (Task<bool>)typeof(ConfigRawParams).GetMethod("processToScreen", InstancePrivate).Invoke(Page, null);
                var limit = DateTime.UtcNow.AddSeconds(10);
                while (!task.IsCompleted && DateTime.UtcNow < limit) { Application.DoEvents(); Thread.Sleep(5); }
                Assert.IsTrue(task.IsCompleted, "原参数表建行超时。");
                Assert.IsTrue(task.GetAwaiter().GetResult(), "原参数表应从内存参数和原收藏设置建行。");
                // 与 Activate 成功收尾相同；不运行其参数文件联网更新支路。
                startup.SetValue(null, false);
                Page.Enabled = true;
                Grid.Enabled = true;
                Assert.AreEqual(2, Grid.Rows.Count);
                AssertNoParameterChanges();
            }

            public DataGridViewRow Row(string name) => Grid.Rows.Cast<DataGridViewRow>()
                .Single(row => Convert.ToString(row.Cells["Command"].Value) == name);

            public void SetFavorite(string name, bool value)
            {
                var row = Row(name);
                var originalIndex = row.Index;
                var cell = (DataGridViewCheckBoxCell)row.Cells["Fav"];
                Grid.CurrentCell = cell;
                Assert.IsTrue(Grid.BeginEdit(false), "应进入原收藏复选框编辑状态。");
                Console.WriteLine("收藏点击前：存储=" + cell.Value + "；编辑=" + cell.EditedFormattedValue + "；配置=" + Settings.Instance["fav_params"]);
                Assert.AreEqual(!value, cell.EditedFormattedValue, "点击前应为相反状态，由原单元格自行翻转。");
                // 原事件分发先让复选框自行翻转，再通知收藏处理器；不能提前赋编辑值导致双翻转。
                typeof(DataGridView).GetMethod("OnCellContentClick", InstancePrivate).Invoke(Grid,
                    new object[] { new DataGridViewCellEventArgs(cell.ColumnIndex, row.Index) });
                Console.WriteLine("收藏点击后：存储=" + cell.Value + "；编辑=" + cell.EditedFormattedValue + "；配置=" + Settings.Instance["fav_params"]);
                Assert.AreEqual(value, cell.EditedFormattedValue, "原事件应只翻转一次。");
                Assert.AreEqual(value, Settings.Instance.GetList("fav_params").Contains(name), "原处理器应读取翻转后的编辑值。");
                Assert.AreSame(cell, Grid.CurrentCell, "收藏操作不能跳走正在编辑的单元格。");
                Assert.AreEqual(originalIndex, row.Index, "项目参数表收藏后不立即重排编辑行。");
                Grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                Grid.EndEdit();
                Assert.AreEqual(value, cell.Value);
                AssertNoParameterChanges();
            }

            public XmlNode SaveAndReadFavoriteNode()
            {
                Settings.Instance.Save();
                var document = new XmlDocument();
                document.Load(ConfigPath);
                AssertNoParameterChanges();
                return document.SelectSingleNode("/Config/fav_params");
            }

            public void ReloadSettingsAndPage()
            {
                Page.Dispose();
                Page = null;
                // 清空进程静态配置及单例，重新走 Instance→Load；这不冒充整程序重启。
                Settings.config = new Dictionary<string, string>();
                singleton.SetValue(null, null);
                OpenPage();
            }

            public void AssertNoParameterChanges()
            {
                Assert.AreEqual(0, Volatile.Read(ref writes), "收藏及配置持久化不得向飞控发送任何报文。");
                Assert.AreEqual(0, ((Hashtable)typeof(ConfigRawParams).GetField("_changes", InstancePrivate).GetValue(Page)).Count,
                    "收藏操作不得进入参数写入队列。");
                Assert.AreEqual(22d, (double)port.MAV.param[First]);
                Assert.AreEqual(1d, (double)port.MAV.param[Second]);
            }

            public void Dispose()
            {
                try
                {
                    Page?.Dispose();
                    port?.Dispose();
                    stream?.Dispose();
                }
                finally
                {
                mainPort.SetValue(null, previousPort);
                rowlist.SetValue(null, previousRows);
                startup.SetValue(null, previousStartup);
                Settings.SetRuntimeDataDirectory(previousRoot);
                Settings.FileName = previousFile;
                Settings.config = previousConfig;
                singleton.SetValue(null, previousSingleton);
                // 只删除本用例创建的随机临时目录，配置及对象引用均已先还原。
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
                }
            }
        }

        [TestMethod]
        public void OriginalFavoriteCheckboxAddsWithoutParameterWrites() => OnUi(host =>
        {
            using (var scope = new Scope(host))
            {
                scope.SetFavorite(First, true);
                CollectionAssert.AreEqual(new[] { First }, Settings.Instance.GetList("fav_params").ToArray());
                Assert.AreEqual(First, scope.SaveAndReadFavoriteNode().InnerText);
            }
        });

        [TestMethod]
        public void RemovingOneFavoritePreservesOtherFavoriteAndSavedXml() => OnUi(host =>
        {
            using (var scope = new Scope(host))
            {
                scope.SetFavorite(First, true);
                scope.SetFavorite(Second, true);
                scope.SetFavorite(First, false);
                CollectionAssert.AreEqual(new[] { Second }, Settings.Instance.GetList("fav_params").ToArray());
                Assert.AreEqual(Second, scope.SaveAndReadFavoriteNode().InnerText);
                scope.ReloadSettingsAndPage();
                Assert.AreEqual(false, scope.Row(First).Cells["Fav"].Value);
                Assert.AreEqual(true, scope.Row(Second).Cells["Fav"].Value);
            }
        });

        [TestMethod]
        public void RemovingLastFavoriteRemovesKeyAndXmlElement() => OnUi(host =>
        {
            using (var scope = new Scope(host))
            {
                scope.SetFavorite(First, true);
                Assert.AreEqual(First, scope.SaveAndReadFavoriteNode().InnerText);
                scope.SetFavorite(First, false);
                Assert.IsFalse(Settings.Instance.ContainsKey("fav_params"), "最后一项删除后必须移除旧键。");
                Assert.AreEqual(0, Settings.Instance.GetList("fav_params").Count());
                Assert.IsNull(scope.SaveAndReadFavoriteNode(), "XML不能遗留旧收藏或空字符串收藏。");
            }
        });

        [TestMethod]
        public void RemovedLastFavoriteStaysAbsentAfterColdLoadAndOriginalTableRebuild() => OnUi(host =>
        {
            using (var scope = new Scope(host))
            {
                scope.SetFavorite(First, true);
                scope.SaveAndReadFavoriteNode();
                scope.SetFavorite(First, false);
                scope.SaveAndReadFavoriteNode();
                scope.ReloadSettingsAndPage();
                Assert.AreEqual(0, Settings.Instance.GetList("fav_params").Count());
                Assert.IsFalse(Settings.Instance.ContainsKey("fav_params"));
                Assert.AreEqual(false, scope.Row(First).Cells["Fav"].Value);
                Assert.AreEqual(false, scope.Row(Second).Cells["Fav"].Value);
                Assert.IsNull(scope.SaveAndReadFavoriteNode());
            }
        });

        [TestMethod]
        public void FavoriteCanBeAddedAgainAfterEmptyListColdLoad() => OnUi(host =>
        {
            using (var scope = new Scope(host))
            {
                scope.SetFavorite(First, true);
                scope.SetFavorite(First, false);
                scope.SaveAndReadFavoriteNode();
                scope.ReloadSettingsAndPage();
                Assert.AreEqual(false, scope.Row(First).Cells["Fav"].Value);
                scope.SetFavorite(First, true);
                Assert.AreEqual(First, scope.SaveAndReadFavoriteNode().InnerText);
                scope.ReloadSettingsAndPage();
                CollectionAssert.AreEqual(new[] { First }, Settings.Instance.GetList("fav_params").ToArray());
                Assert.AreEqual(true, scope.Row(First).Cells["Fav"].Value);
            }
        });

        [DataTestMethod]
        [DataRow("DisabledPlugins")]
        [DataRow("serialpasslist")]
        [DataRow("InputBoxFavoriteRegression")]
        public void OtherListCallersKeepNullEmptyEncodingAndDistinctSemantics(string key) => OnUi(host =>
        {
            using (var scope = new Scope(host))
            {
                var values = new[] { "alpha;中文+space value", "beta%value" };
                Settings.Instance.SetList(key, new[] { values[0], values[1], values[0] });
                var encoded = Settings.Instance[key];
                CollectionAssert.AreEqual(values, Settings.Instance.GetList(key).ToArray());
                Settings.Instance.SetList(key, null);
                Settings.Instance.SetList(key, new string[0]);
                Assert.AreEqual(encoded, Settings.Instance[key], "现有空/null SetList仍保留原值，不能因收藏修复改变全局语义。");
                scope.SetFavorite(First, true);
                scope.SetFavorite(First, false);
                scope.SaveAndReadFavoriteNode();
                scope.ReloadSettingsAndPage();
                Assert.AreEqual(encoded, Settings.Instance[key]);
                CollectionAssert.AreEqual(values, Settings.Instance.GetList(key).ToArray());
                scope.AssertNoParameterChanges();
            }
        });
    }
}
