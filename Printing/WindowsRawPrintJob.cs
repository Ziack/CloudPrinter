﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml.Linq;
using CloudPrint.Util;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Printing;

namespace CloudPrint.Printing
{
    [Serializable]
    public class WindowsRawPrintJob : PrintJob
    {
        public override byte[] PrintData { get { return PagedData == null ? null : PagedData.GetData(); } set { PagedData = new PaginatedPrintData { Prologue = value, PageData = null, Epilogue = null }; } }
        public PaginatedPrintData PagedData { get; set; }

        public enum PRINTER_ACCESS_MASK
        {
            PRINTER_ACCESS_ADMINISTER = 4,
            PRINTER_ACCESS_USE = 8,
            PRINTER_ALL_ACCESS = 0x000F000C
        }

        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            public ushort SpecVersion;
            public ushort DriverVersion;
            public ushort Size;
            public ushort DriverExtra;
            public uint Fields;
            public short Orientation;
            public short PaperSize;
            public short PaperLength;
            public short PaperWidth;
            public short Scale;
            public short Copies;
            public short DefaultSource;
            public short PrintQuality;
            public short Color;
            public short Duplex;
            public short YResolution;
            public short TTOption;
            public short Collate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string FormName;
            public ushort LogPixels;
            public uint BitsPerPel;
            public uint PelsWidth;
            public uint PelsHeight;
            public uint Nup;
            public uint DisplayFrequency;
            public uint ICMMethod;
            public uint ICMIntent;
            public uint MediaType;
            public uint DitherType;
            public uint Reserved1;
            public uint Reserved2;
            public uint PanningWidth;
            public uint PanningHeight;
        }

        public struct PRINTER_DEFAULTS
        {
            public string pDatatype;
            public IntPtr pDevMode;
            public PRINTER_ACCESS_MASK DesiredAccess;

            public DEVMODE? DevMode
            {
                get
                {
                    if (pDevMode != IntPtr.Zero)
                    {
                        return (DEVMODE)Marshal.PtrToStructure(pDevMode, typeof(DEVMODE));
                    }
                    else
                    {
                        return null;
                    }
                }
                set
                {
                    if (pDevMode != null)
                    {
                        Marshal.DestroyStructure(pDevMode, typeof(DEVMODE));
                        Marshal.FreeHGlobal(pDevMode);
                        pDevMode = IntPtr.Zero;
                    }

                    if (value != null)
                    {
                        pDevMode = Marshal.AllocHGlobal(Marshal.SizeOf(value));
                        Marshal.StructureToPtr(value, pDevMode, false);
                    }
                }
            }
        }

        public struct ADDJOB_INFO_1
        {
            public string Path;
            public uint JobId;
        }

        public struct DOC_INFO_1
        {
            public string DocName;
            public string OutputFile;
            public string Datatype;
        }

        public struct JOB_INFO_2
        {
            public uint JobId;
            public string PrinterName;
            public string MachineName;
            public string UserName;
            public string Document;
            public string NotifyName;
            public string Datatype;
            public string PrintProcessor;
            public string Parameters;
            public string DriverName;
            public IntPtr pDevMode;
            public uint Status;
            public uint Priority;
            public uint Position;
            public uint StartTime;
            public uint UntilTime;
            public uint TotalPages;
            public uint Size;
            public ulong Submitted;
            public uint Time;
            public uint PagesPrinted;

            public DEVMODE? DevMode
            {
                get
                {
                    if (pDevMode != IntPtr.Zero)
                    {
                        return (DEVMODE)Marshal.PtrToStructure(pDevMode, typeof(DEVMODE));
                    }
                    else
                    {
                        return null;
                    }
                }
                set
                {
                    if (pDevMode != null)
                    {
                        Marshal.DestroyStructure(pDevMode, typeof(DEVMODE));
                        Marshal.FreeHGlobal(pDevMode);
                        pDevMode = IntPtr.Zero;
                    }

                    if (value != null)
                    {
                        pDevMode = Marshal.AllocHGlobal(Marshal.SizeOf(value));
                        Marshal.StructureToPtr(value, pDevMode, false);
                    }
                }
            }
        }

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pPrinterDefaults);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool AddJob(IntPtr hPrinter, uint Level, IntPtr pData, uint cbBuf, out uint pcbNeeded);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool GetJob(IntPtr hPrinter, uint JobId, uint Level, IntPtr pJob, uint cbBuf, out uint pcbNeeded);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool SetJob(IntPtr hPrinter, uint JobId, uint Level, IntPtr pJob, uint Command);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ScheduleJob(IntPtr hPrinter, uint JobId);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern uint StartDocPrinter(IntPtr hPrinter, uint Level, ref DOC_INFO_1 pDocInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBuf, out uint pcWritten);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        private static void WritePrinter(IntPtr hPrinter, byte[] data)
        {
            int i = 0;

            while (i < data.Length)
            {
                byte[] _data = new byte[32768];
                uint len = (uint)Math.Min(data.Length - i, _data.Length);
                Array.Copy(data, i, _data, 0, len);

                if (!WritePrinter(hPrinter, _data, len, out len))
                {
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                }

                i += (int)len;
            }
        }

        protected override void Run()
        {
            if (PagedData != null)
            {
                IntPtr hPrinter;
                PRINTER_DEFAULTS defaults = new PRINTER_DEFAULTS
                {
                    DesiredAccess = PRINTER_ACCESS_MASK.PRINTER_ACCESS_USE,
                    pDatatype = "RAW"
                };

                if (OpenPrinter(PrinterName, out hPrinter, ref defaults))
                {
                    DOC_INFO_1 docInfo = new DOC_INFO_1 { Datatype = "RAW", DocName = JobName, OutputFile = null };
                    uint jobid = StartDocPrinter(hPrinter, 1, ref docInfo);

                    if (jobid < 0)
                    {
                        Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                    }

                    if (PagedData.Prologue != null)
                    {
                        WritePrinter(hPrinter, PagedData.Prologue);
                    }

                    if (PagedData.PageData != null)
                    {
                        foreach (byte[] pagedata in PagedData.PageData)
                        {
                            StartPagePrinter(hPrinter);
                            WritePrinter(hPrinter, pagedata);
                            EndPagePrinter(hPrinter);
                        }
                    }

                    if (PagedData.Epilogue != null)
                    {
                        WritePrinter(hPrinter, PagedData.Epilogue);
                    }

                    if (!EndDocPrinter(hPrinter))
                    {
                        Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                    }

                    ClosePrinter(hPrinter);
                }
            }
        }
    }
}
