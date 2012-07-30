using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MezeoPostInstallLauncher.Tests
{
    [TestClass]
    public class ProgramTests
    {
        /// <summary>
        /// This test will only pass if you have MS Office installed in the default path on a 64-bit system.
        /// </summary>
        [TestMethod]
        public void GetApplicationExeTest()
        {
            var expectedExe = @"C:\Program Files (x86)\Microsoft Office\Office14\WINWORD.EXE";
            var actualExe = Program.GetApplicationExe("Microsoft Office", "winword.exe");
            Assert.AreEqual(expectedExe, actualExe);
        }
    }
}
