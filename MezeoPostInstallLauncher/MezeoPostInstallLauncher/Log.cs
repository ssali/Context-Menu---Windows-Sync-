using System;
using System.Collections.Generic;
using System.Linq;

namespace MezeoPostInstallLauncher
{
    public static class Log
    {
        public static void Debug(string format, params object[] args)
        {
#if DEBUG
            Console.WriteLine("Debug: " + format, args);
#endif
        }

        public static void Error(string format, params object[] args)
        {
            Console.WriteLine("Error: " + format, args);
        }

        public static void Debug(Exception ex)
        {
#if DEBUG
            Console.WriteLine("Debug: {0}", ex);
#endif
        }

        public static void Error(Exception ex)
        {
            Console.WriteLine("Error: {0}", ex);
        }
    }
}
