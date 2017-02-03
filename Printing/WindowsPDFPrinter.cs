﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Printing;
using CloudPrint.Util;

namespace CloudPrint.Printing
{
    public class WindowsPDFPrinter : JobPrinter
    {
        protected void PaginatePDF(byte[] pdfdata, out byte[] prologue, out byte[][] pagedata, out byte[] epilogue)
        {
            throw new NotImplementedException();
        }

        public override bool NeedUserAuth { get { return true; } }

        public override bool UserCanPrint(string username)
        {
            return WindowsIdentityStore.HasWindowsIdentity(username);
        }

        public override void Print(CloudPrintJob job)
        {
            using (Ghostscript gs = new Ghostscript())
            {
                PrintTicket printTicket = job.GetPrintTicket();
                byte[] printData = job.GetPrintData();
                List<string> args = new List<string>();

                args.Add("-dAutoRotatePages=/None");

                if (printTicket.OutputColor != OutputColor.Color)
                {
                    args.Add("-sColorConversionStrategy=Gray");
                    args.Add("-dProcessColorModel=/DeviceGray");
                }

                byte[] printdata = gs.ProcessData(printTicket, printData, "pdfwrite", args.ToArray(), null);

                WindowsRawPrintJob pj = new WindowsRawPrintJob
                {
                    JobName = job.JobTitle,
                    UserName = job.Username,
                    PrinterName = job.Printer.Name,
                    PrintData = printdata,
                    RunAsUser = true
                };

                pj.Print();
            }
        }
    }
}
