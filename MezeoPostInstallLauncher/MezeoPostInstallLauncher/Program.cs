using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;

namespace MezeoPostInstallLauncher
{
    public class Program
    {
        static int Main(string[] args)
        {
            try
            {
                var options = CommandLineOptions.Parse(args, new[] { "ProductName", "Exe" });
                var productName = options["ProductName"];
                var exeName = options["Exe"];

                var fullPath = GetApplicationExe(productName, exeName);

                try
                {
                    var process = Process.Start(fullPath);
                    if (!process.WaitForInputIdle())
                        throw new ApplicationException(string.Format("Process failed to start or enter idle state: {0}", fullPath));
                }
                catch (Exception ex)
                {
                    throw new ApplicationException(string.Format("Failed while launching process: {0}", fullPath), ex);
                }
            }

            catch (Exception ex)
            {
                Log.Error(ex.Message);
                Log.Debug(ex);
                return 1;
            }

            return 0;
        }

        public static string GetApplicationExe(string productName, string exeName)
        {
            var paths = MsiUtils.GetInstallPathOfProduct(productName).ToList();

            foreach (var path in paths)
            {
                Log.Debug("Found application path: {0}", path);
                if (string.IsNullOrWhiteSpace(path))
                {
                    Log.Debug("   Requested product not found: {0}", productName);
                    continue;
                }

                if (!Directory.Exists(path))
                {
                    Log.Debug("   Unable to find application install path for '{0}': {1}", productName, path);
                    continue;
                }

                var fullPath = Directory.EnumerateFiles(path, exeName, SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(fullPath))
                {
                    Log.Debug("   Unable to loacate exe '{0}' on path: {1}", exeName, path);
                    continue;
                }

                Log.Debug ("   Found exe: {0}", fullPath);
                return fullPath;
            }

            throw new ApplicationException(string.Format("Unable to locate executable '{0}' for application '{1}'", exeName, productName));
        }
    }
}