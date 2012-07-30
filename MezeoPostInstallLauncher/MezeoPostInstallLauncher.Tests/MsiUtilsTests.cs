using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MezeoPostInstallLauncher.Tests
{
    [TestClass]
    public class MsiUtilsTests
    {
        /// <summary>
        /// This test will only pass if you have MS Office installed in the default path on a 64-bit system.
        /// </summary>
        [TestMethod]
        public void DetectPathOfMicrosoftOffice()
        {
            string expectedPath = @"C:\Program Files (x86)\Microsoft Office\";

            var paths = MsiUtils.GetInstallPathOfProduct("microsoft office");
            var path = paths.Select(item => { Console.WriteLine(item); return item; }).Where(p => p.Contains(expectedPath)).FirstOrDefault();
            Assert.AreEqual(expectedPath, path);
        }
    }
}
