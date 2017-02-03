using System;
using CloudPrint.Printing;
using CloudPrint.Service;
using System.IO;

namespace CloudPrint
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "-print")
            {
                return PrintJob.Run(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.OpenStandardError());
            }
            else
            {
                var service = new GoogleCloudPrintProxyService();
                return service.Run(args);
            }
        }
    }
}
