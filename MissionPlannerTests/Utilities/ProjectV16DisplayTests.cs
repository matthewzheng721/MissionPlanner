using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Controls;
using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace MissionPlannerTests.Utilities
{
    [TestClass, DoNotParallelize]
    public class ProjectV16DisplayTests
    {
        private sealed class Reading { public double Value { get; set; } }
        private sealed class SelectableQuick : QuickView
        {
            public void SelectData() { OnDoubleClick(EventArgs.Empty); }
        }
        private static void OnUi(Action<Form> action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                using (var form = new Form { ShowInTaskbar = false, Opacity = 0 })
                {
                    form.Shown += (sender, args) => form.BeginInvoke((Action)(() =>
                    {
                        try { action(form); } catch (Exception error) { failure = error; }
                        finally { form.Close(); }
                    }));
                    Application.Run(form);
                }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.IsTrue(thread.Join(15000), "显示测试超时。");
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        [TestMethod]
        public void ProjectQuickThemePreservesBindingSelectionAndDisconnectRecovery() => OnUi(form =>
        {
            using (var host = new Panel()) using (var quick = new SelectableQuick())
            {
                form.Controls.Add(host); host.Controls.Add(quick); host.CreateControl(); quick.CreateControl();
                var reading = new Reading { Value = 123.456 };
                var binding = quick.DataBindings.Add("number", reading, "Value", true, DataSourceUpdateMode.Never);
                var selections = 0; quick.DoubleClick += (sender, args) => selections++;
                quick.desc = "相对高度 (ft)"; quick.numberformat = "0.0";
                ProjectWorkspaceStyle.Apply(host); ProjectWorkspaceStyle.Apply(host);
                Assert.AreSame(binding, quick.DataBindings["number"]);
                Assert.AreSame(reading, binding.DataSource);
                binding.ReadValue();
                Assert.AreEqual("123.5", quick.ProjectFormattedNumber);
                CollectionAssert.AreEqual(new[] { "相对高度", "ft" }, QuickView.ProjectDisplayParts(quick.desc));
                quick.SelectData(); Assert.AreEqual(1, selections, "重复应用主题不能重复绑定选择事件。");
                quick.DataUnavailable = true;
                Assert.AreEqual("—", quick.ProjectFormattedNumber);
                Assert.AreEqual(123.456, quick.number, "断联占位只作用于显示，不篡改原数值。");
                reading.Value = -8.25; binding.ReadValue(); quick.DataUnavailable = false;
                Assert.AreEqual(-8.25, quick.number);
                Assert.AreEqual((-8.25).ToString("0.0"), quick.ProjectFormattedNumber);
            }
        });
        [TestMethod]
        public void ProjectQuickKeepsUnitsCultureAndInvalidReadingsDistinct() => OnUi(form =>
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                using (var quick = new QuickView { number = -12.5, numberformat = "0.00" })
                {
                    Assert.AreEqual("-12,50", quick.ProjectFormattedNumber);
                    CollectionAssert.AreEqual(new[] { "高度", "m" }, QuickView.ProjectDisplayParts("Altitude (m)"));
                    CollectionAssert.AreEqual(new[] { "自定义读数", "" }, QuickView.ProjectDisplayParts("自定义读数"));
                    quick.number = double.NaN; Assert.AreEqual("—", quick.ProjectFormattedNumber);
                    quick.number = double.PositiveInfinity; Assert.AreEqual("—", quick.ProjectFormattedNumber);
                    quick.number = 0; Assert.AreEqual("0,00", quick.ProjectFormattedNumber, "真实零值必须区别于无数据。");
                }
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        });
    }
}
