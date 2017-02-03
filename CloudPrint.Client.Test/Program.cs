using CloudPrint.Client;
using System;
using System.IO;

namespace CloudPrint.Client.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            var service = new GoogleCloudPrintService(accessToken: "ya29.Ci_JAzDgONRSXVc9LEXFKplbA5afLe3pRK-CcgXNicpJwsKKmQCH2KscL4hRWnSXog");

            var printers = service.GetPrinters();
            
            var document = typeof(Program).Assembly.GetManifestResourceStream("CloudPrint.Client.Test.Sample.pdf").ReadFully();

            //foreach (var printer in printers.printers)
            //{
                var printJob = service.PrintAsync(printerId: "c5f2459e-42ac-a564-3c4c-ed195552b2bc", title: "Sample Printer", document: document, mimeType: "application/pdf");
                printJob.Wait();
                Console.WriteLine($"c5f2459e-42ac-a564-3c4c-ed195552b2bc -> {printJob.Result.message}");
            //}
            

            Console.ReadLine();
        }

        
    }
    public static class Helpers
    {

        public static byte[] ReadFully(this Stream input)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }
        }
    }
}
