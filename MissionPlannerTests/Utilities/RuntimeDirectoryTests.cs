using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;

namespace MissionPlanner.Utilities.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class RuntimeDirectoryTests
    {
        [TestMethod]
        public void IsolatedSettingsPersistWithoutLegacyDefaults()
        {
            var previousRoot = Settings.RuntimeDataDirectory;
            var previousFile = Settings.FileName;
            var previousValues = new Dictionary<string, string>(Settings.config);
            var root = Path.Combine(Path.GetTempPath(), "MP-runtime-test-" + Guid.NewGuid());
            try
            {
                Settings.SetRuntimeDataDirectory(root);
                Settings.FileName = "config.xml";
                Settings.config.Clear();
                var settings = new Settings();
                settings.Load();
                Assert.AreEqual(0, settings.Count, "新目录不应继承旧配置或默认配置。");
                settings["runtime_test"] = "保存成功";
                settings.Save();
                Assert.IsTrue(File.Exists(Path.Combine(root, "config", "config.xml")));
                Settings.config.Clear();
                new Settings().Load();
                Assert.AreEqual("保存成功", settings["runtime_test"]);
                Assert.AreEqual(Path.Combine(root, "data") + Path.DirectorySeparatorChar, Settings.GetDataDirectory());
                Assert.AreEqual(Path.Combine(root, "logs"), Settings.GetDefaultLogDir());
                // 用户主动选择日志位置仍可生效，不引入文件访问限制。
                settings.LogDir = Path.Combine(root, "selected-logs");
                Assert.AreEqual(Path.Combine(root, "selected-logs"), settings.LogDir);
            }
            finally
            {
                Settings.SetRuntimeDataDirectory(previousRoot);
                Settings.FileName = previousFile;
                Settings.config.Clear();
                foreach (var value in previousValues) Settings.config[value.Key] = value.Value;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void RelativeDirectoryIsRejectedAndDefaultPathsRemainUnchanged()
        {
            var previousRoot = Settings.RuntimeDataDirectory;
            try
            {
                Settings.SetRuntimeDataDirectory(null);
                var originalData = Settings.GetDataDirectory();
                var originalUser = Settings.GetUserDataDirectory();
                foreach (var relative in new[] { "relative", "C:relative", @"\relative" })
                    Assert.ThrowsException<ArgumentException>(() => Settings.SetRuntimeDataDirectory(relative));
                Assert.IsNull(Settings.RuntimeDataDirectory);
                Assert.AreEqual(originalData, Settings.GetDataDirectory());
                Assert.AreEqual(originalUser, Settings.GetUserDataDirectory());
                Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    Settings.AppConfigName) + Path.DirectorySeparatorChar, originalData);
            }
            finally
            {
                Settings.SetRuntimeDataDirectory(previousRoot);
            }
        }
    }
}
