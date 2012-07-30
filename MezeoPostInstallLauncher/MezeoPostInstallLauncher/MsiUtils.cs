using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MezeoPostInstallLauncher
{
    public static class MsiUtils
    {
        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        static extern Int32 MsiGetProductInfo(string product, string property, [Out] StringBuilder valueBuf, ref Int32 len);

        [DllImport("msi.dll", SetLastError = true)]
        static extern int MsiEnumProducts(int iProductIndex, StringBuilder lpProductBuf);

        /// <summary>
        /// Utility method for fetching the install path of a MSI installed product, given the name of that product.
        /// Adapted from: http://stackoverflow.com/questions/3526449/how-to-get-a-list-of-installed-software-products
        /// </summary>
        /// <param name="productName"></param>
        /// <returns></returns>
        public static IEnumerable<string> GetInstallPathOfProduct(string productName)
        {
            StringBuilder productCode = new StringBuilder(39);
            int index = 0;
            while (0 == MsiEnumProducts(index++, productCode))
            {
                Log.Debug("Found ProductCode: {0}", productCode.ToString());
                Int32 productNameLen = 512;
                StringBuilder foundProductName = new StringBuilder(productNameLen);

                MsiGetProductInfo(productCode.ToString(), "ProductName", foundProductName, ref productNameLen);
                Log.Debug("   Product name is: {0}", foundProductName.ToString());

                if (foundProductName.ToString().ToLower().Contains(productName.ToLower()))
                {
                    Log.Debug("   Product name matches: {0}", productName);

                    Int32 installPathLength = 1024;
                    StringBuilder installPath = new StringBuilder(installPathLength);

                    MsiGetProductInfo(productCode.ToString(), "InstallLocation", installPath, ref installPathLength);

                    Log.Debug("   Install path: {0}", installPath.ToString());

                    if (string.IsNullOrWhiteSpace(installPath.ToString()))
                        continue;

                    yield return installPath.ToString();
                }
            }
        }
    }
}
