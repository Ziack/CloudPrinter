﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Microsoft.Win32;
using System.Reflection;
using System.Net;
using System.Printing;
using System.IO;
using System.Windows.Xps.Packaging;
using System.Threading;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Xml.Linq;
using System.Runtime.Remoting;
using System.ServiceModel;
using CloudPrint.Util;

namespace CloudPrint.Proxy
{
    public class CloudPrintProxy : IDisposable
    {
        #region constants

        private static TimeSpan PrintQueueUpdateInterval = TimeSpan.FromSeconds(Config.PrintQueuePollingInterval);
        private static TimeSpan PrintJobUpdateInterval = TimeSpan.FromSeconds(Config.PrintJobPollingInterval);
        private static TimeSpan XMPPReconnectInterval = TimeSpan.FromSeconds(Config.XMPPReconnectInterval);

        #endregion

        #region private properties
        public OAuthTicket OAuthTicket { get; set; }
        private object OAuthTicketLock { get; set; }
        private string OAuthCodePollURL { get; set; }
        private XMPP XMPP { get; set; }
        private Timer PrintQueueUpdateTimer { get; set; }
        private Timer PrintJobUpdateTimer { get; set; }
        private Timer XMPPReconnectTimer { get; set; }
        private IList<CloudPrinter> _Queues { get; set; }
        private ConcurrentDictionary<string, CloudPrintJob> _PrintJobs { get; set; }
        private DateTime PrintJobsLastUpdated { get; set; }
        private DateTime PrintQueuesLastUpdated { get; set; }
        private object UpdateLock { get; set; }
        private IPrintJobProcessor PrintJobProcessor { get; set; }
        private Action<CloudPrintProxy> OperationCancelled { get; set; }
        private bool PrintQueuesNeedUpdating { get; set; }
        private bool Disposed { get; set; }
        #endregion

        #region public properties
        public bool IsRegistered { get { return Config.OAuthRefreshToken != null; } }
        public IEnumerable<CloudPrinter> Queues { get { return GetPrintQueues(); } }
        public IEnumerable<CloudPrintJob> PrintJobs { get { return GetCloudPrintJobs(); } }
        #endregion

        #region constructors / destructors
        public CloudPrintProxy(IPrintJobProcessor printjobprocessor, Action<CloudPrintProxy> operationCancelledCallback)
        {
            if (printjobprocessor == null)
            {
                throw new ArgumentNullException("printjobprocessor");
            }

            OAuthTicketLock = new object();
            _PrintJobs = new ConcurrentDictionary<string,CloudPrintJob>();
            _Queues = new List<CloudPrinter>();
            OperationCancelled = operationCancelledCallback;
            PrintJobsLastUpdated = DateTime.MinValue;
            PrintJobProcessor = printjobprocessor;
            UpdateLock = new object();
        }

        ~CloudPrintProxy()
        {
            Dispose(false);
        }
        #endregion

        #region private / protected methods
        private void Dispose(bool disposing)
        {
            XMPP xmpp;
            Timer queuetimer;
            Timer jobtimer;

            lock (UpdateLock)
            {
                xmpp = this.XMPP;
                queuetimer = this.PrintQueueUpdateTimer;
                jobtimer = this.PrintJobUpdateTimer;
                this.XMPP = null;
                this.PrintJobUpdateTimer = null;
                this.PrintQueueUpdateTimer = null;
                this._Queues = null;
            }

            if (jobtimer != null)
            {
                jobtimer.Dispose();
            }

            if (queuetimer != null)
            {
                queuetimer.Dispose();
            }

            if (xmpp != null)
            {
                xmpp.Dispose();
            }

            if (disposing)
            {
                GC.SuppressFinalize(this);
                Disposed = true;
            }
        }

        private string RequestAuthCode(string URL)
        {
            var req = (HttpWebRequest)HttpWebRequest.Create(URL + Config.OAuthClientID);
            req.UserAgent = Config.CloudPrintUserAgent;
            req.Headers.Add("X-CloudPrint-Proxy", Config.CloudPrintProxyName);
            dynamic respdata = HTTPHelper.GetResponseJson(req);
            if (respdata.success == true)
            {
                string authcode = respdata.authorization_code;
                string useremail = respdata.user_email;
                Config.XMPP_JID = respdata.xmpp_jid;
                OAuthTicket = OAuthTicket.FromAuthCode(authcode, Config.OAuthClientID, Config.OAuthClientSecret, Config.OAuthRedirectURI);
                return useremail;
            }
            else
            {
                throw new PrintProxyException(respdata.message);
            }
        }

        private string RegisterCloudPrinter(CloudPrinter printer)
        {
            Logger.Log(LogLevel.Debug, "Registering cloud printer [{0}]", printer.Name);

            var reqdata = new
            {
                printproxydummyparameter = "",
                printer = printer.Name,
                proxy = Config.CloudPrintProxyID,
                description = printer.Description,
                capsHash = printer.CapsHash,
                status = printer.Status,
                capabilities = printer.Capabilities,
                defaults = printer.Defaults
            };

            var response = HTTPHelper.PostCloudPrintMultiPartRequest(OAuthTicket, "register", reqdata);

            if (response.success == true)
            {
                printer.SetPrinterID(response.printers[0].id);

                Logger.Log(LogLevel.Debug, "Printer [{0}] has ID {1}", printer.Name, printer.PrinterID);

                if (Config.OAuthRefreshToken == null && OAuthCodePollURL == null)
                {
                    OAuthCodePollURL = response.polling_url;
                    return response.complete_invite_url;
                }

                return null;
            }
            else
            {
                throw new PrintProxyException(response.message);
            }
        }

        private void UpdateCloudPrinter(CloudPrinter printer)
        {
            var reqdata = new
            {
                printproxydummyparameter = "",
                printerid = printer.PrinterID,
                printer = printer.Name,
                proxy = Config.CloudPrintProxyID,
                description = printer.Description,
                capsHash = printer.CapsHash,
                status = printer.Status,
                capabilities = printer.Capabilities,
                defaults = printer.Defaults
            };

            string printersdir = Path.Combine(Config.DataDirName, "Printers");
            Directory.CreateDirectory(printersdir);
            File.WriteAllBytes(Path.Combine(printersdir, printer.PrinterID + ".capabilities.xml"), Encoding.UTF8.GetBytes(printer.Capabilities));
            File.WriteAllBytes(Path.Combine(printersdir, printer.PrinterID + ".defaults.xml"), Encoding.UTF8.GetBytes(printer.Defaults));

            var response = HTTPHelper.PostCloudPrintMultiPartRequest(OAuthTicket, "update", reqdata);
        }

        private void DeleteCloudPrinter(string printerid)
        {
            Logger.Log(LogLevel.Info, "Deleting cloud printer {0}", printerid);

            HTTPHelper.PostCloudPrintUrlEncodedRequest(OAuthTicket, "delete", new { printerid = printerid });
        }

        private void PollUpdatePrintQueues()
        {
            if (PrintQueuesNeedUpdating)
            {
                UpdatePrintQueues();
            }

            PrintQueuesNeedUpdating = true;
        }

        private IEnumerable<CloudPrinter> GetPrintQueues()
        {
            PrintQueuesNeedUpdating = false;
            return UpdatePrintQueues();
        }
        
        private IEnumerable<CloudPrinter> UpdatePrintQueues()
        {
            if (PrintQueuesLastUpdated + PrintQueueUpdateInterval - TimeSpan.FromSeconds(5) < DateTime.Now)
            {
                lock (UpdateLock)
                {
                    try
                    {
                        Dictionary<string, string> printerIds;

                        if (_Queues == null || _Queues.Count == 0)
                        {
                            IEnumerable<dynamic> printers = HTTPHelper.PostCloudPrintUrlEncodedRequest(OAuthTicket, "list", new { proxy = Config.CloudPrintProxyID }).printers;
                            printerIds = printers.ToDictionary(p => (string)p.name, p => (string)p.id);
                        }
                        else
                        {
                            printerIds = _Queues.ToDictionary(p => p.Name, p => p.PrinterID);
                        }

                        List<CloudPrinter> queues = new List<CloudPrinter>();

                        foreach (CloudPrinter queue in PrintJobProcessor.GetPrintQueues())
                        {
                            if (!printerIds.ContainsKey(queue.Name))
                            {
                                RegisterCloudPrinter(queue);
                            }
                            else
                            {
                                queue.SetPrinterID(printerIds[queue.Name]);
                                UpdateCloudPrinter(queue);
                            }

                            queues.Add(queue);
                        }

                        foreach (KeyValuePair<string, string> printer_kvp in printerIds)
                        {
                            if (queues.Count(q => q.PrinterID == printer_kvp.Value) == 0)
                            {
                                DeleteCloudPrinter(printer_kvp.Value);
                            }
                        }

                        _Queues = queues;

                        PrintQueuesLastUpdated = DateTime.Now;

                        UpdateCloudPrintJobs();

                        return queues.AsEnumerable();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(LogLevel.Warning, "Caught exception while updating printer queues:\n{0}", ex.ToString());
                        return _Queues.AsEnumerable();
                    }
                }
            }
            else
            {
                return _Queues.AsEnumerable();
            }
        }

        private void PollUpdateCloudPrintJobs()
        {
            if (PrintJobsLastUpdated + PrintJobUpdateInterval - TimeSpan.FromSeconds(5) < DateTime.Now)
            {
                UpdateCloudPrintJobs();
            }
        }

        private IEnumerable<CloudPrintJob> GetCloudPrintJobs()
        {
            if (PrintJobsLastUpdated + PrintJobUpdateInterval - TimeSpan.FromSeconds(5) < DateTime.Now)
            {
                return UpdateCloudPrintJobs();
            }
            else
            {
                return _PrintJobs.Values;
            }
        }

        private IEnumerable<CloudPrintJob> UpdateCloudPrintJobs()
        {
            PrintJobsLastUpdated = DateTime.Now;
            Logger.Log(LogLevel.Debug, "Updating print jobs for all printers");

            try
            {
                Dictionary<string, CloudPrintJob> jobs = CloudPrintJobImpl.GetIncompletePrintJobs(this).ToDictionary(j => j.JobID, j => j);

                IEnumerable<CloudPrinter> printers = Queues;

                foreach (CloudPrinter printer in printers)
                {
                    foreach (CloudPrintJob job in UpdateCloudPrintJobs(printer))
                    {
                        jobs[job.JobID] = job;
                    }
                }

                return jobs.Values.ToList();
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Warning, "Caught exception while updating print jobs:\n{0}", ex.ToString());
                return _PrintJobs.Values;
            }
        }

        private IEnumerable<CloudPrintJob> UpdateCloudPrintJobs(CloudPrinter printer)
        {
            List<CloudPrintJob> jobs;

            dynamic fetchdata = HTTPHelper.PostCloudPrintUrlEncodedRequest(OAuthTicket, "fetch", new { printerid = printer.PrinterID });
            if (fetchdata.success)
            {
                jobs = ((IEnumerable<dynamic>)fetchdata.jobs).Select(j => new CloudPrintJobImpl(this, printer, j)).OfType<CloudPrintJob>().ToList();
            }
            else
            {
                jobs = new List<CloudPrintJob>();
            }

            foreach (CloudPrintJob job in jobs)
            {
                if (!_PrintJobs.ContainsKey(job.JobID))
                {
                    _PrintJobs[job.JobID] = job;
                    PrintJobProcessor.AddJob(job);
                    //Logger.Log(LogLevel.Info, "Received new print job {0} [{1}] owned by {2} for printer [{3}]", job.JobID, job.JobTitle, job.Username, job.Printer.Name);
                }
            }

            return jobs;
        }

        private void ThrowIfDisposed()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(this.GetType().Name);
            }
        }

        private void RunXMPP()
        {
            XMPP xmpp = new XMPP(Config.XMPP_JID, OAuthTicket.AccessToken, Config.XMPPResourceName, "X-OAUTH2", Config.XMPPUseProxy ? Config.WebProxyHost : null, Config.XMPPUseProxy ? Config.WebProxyPort : 0, Config.XMPPHost, Config.XMPPPort);
            xmpp.Start((ex, x) => XMPP_Ended(ex, x));
            xmpp.Subscribe("cloudprint.google.com", ProcessPush);
            XMPP = xmpp;
        }

        private void XMPP_Ended(Exception ex, XMPP xmpp)
        {
            if (xmpp.IsCancelled || ex == null)
            {
                OperationCancelled(this);
            }
            else if (xmpp.IsSubscribed)
            {
                RunXMPP();
            }
            else
            {
                XMPPReconnectTimer = new Timer(new TimerCallback(XMPP_Reconnect), null, XMPPReconnectInterval, new TimeSpan(-1));
                Logger.Log(LogLevel.Warning, "XMPP connection broken - exception:\n{0}", ex);
            }
        }

        private void XMPP_Reconnect(object state)
        {
            RunXMPP();
        }

        private void ProcessPush(XElement el, XMPP xmpp)
        {
            string printerid = Encoding.ASCII.GetString(Convert.FromBase64String(el.Value));
            UpdateCloudPrintJobs(printerid);
        }
        #endregion

        #region public methods
        public void Dispose()
        {
            Dispose(true);
        }

        public void Start(bool useXMPP)
        {
            lock (UpdateLock)
            {
                ThrowIfDisposed();

                if (Config.OAuthRefreshToken != null && Config.OAuthCodeAccepted)
                {
                    if (OAuthTicket == null)
                    {
                        OAuthTicket = new OAuthTicket(Config.OAuthRefreshToken, Config.OAuthClientID, Config.OAuthClientSecret, Config.OAuthRedirectURI);
                    }

                    if (PrintQueueUpdateTimer == null)
                    {
                        PrintQueueUpdateTimer = new Timer((obj) => { lock (UpdateLock) { PollUpdatePrintQueues(); } }, null, TimeSpan.Zero, PrintQueueUpdateInterval);
                    }

                    if (XMPP == null && PrintJobUpdateTimer == null)
                    {
                        PrintJobUpdateTimer = new Timer((obj) => PollUpdateCloudPrintJobs(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

                        if (useXMPP)
                        {
                            RunXMPP();
                        }
                    }

                    Logger.Log(LogLevel.Info, "Cloud Print Proxy started");
                }
                else
                {
                    throw new InvalidOperationException("Need to register and accept proxy before it can be started");
                }
            }
        }

        public void Stop()
        {
            Dispose(false);
        }

        public string Register()
        {
            lock (OAuthTicketLock)
            {
                ThrowIfDisposed();

                if (Config.OAuthRefreshToken == null)
                {
                    var printer = this.PrintJobProcessor.GetPrintQueues().First();
                    return RegisterCloudPrinter(printer);
                }
                else
                {
                    throw new InvalidOperationException("Already registered");
                }
            }
        }

        public string RequestAuthCode()
        {
            lock (OAuthTicketLock)
            {
                ThrowIfDisposed();

                if (Config.OAuthRefreshToken == null)
                {
                    if (OAuthCodePollURL == null)
                    {
                        throw new InvalidOperationException("Print Proxy must first be registered.");
                    }

                    Config.OAuthEmail = RequestAuthCode(OAuthCodePollURL);
                    Config.OAuthRefreshToken = OAuthTicket.RefreshToken;
                    OAuthCodePollURL = null;
                    return Config.OAuthEmail;
                }
                else
                {
                    throw new InvalidOperationException("Already registered");
                }
            }
        }

        public void ClearAuthCode()
        {
            Stop();

            lock (OAuthTicketLock)
            {
                OAuthCodePollURL = null;
                OAuthTicket = null;
                Config.OAuthRefreshToken = null;
                Config.OAuthCodeAccepted = false;
                Config.OAuthEmail = null;
            }
        }

        public void AcceptAuthCode(string email)
        {
            if (Config.OAuthEmail.Equals(email, StringComparison.InvariantCultureIgnoreCase))
            {
                Config.OAuthCodeAccepted = true;
            }
        }

        public CloudPrinter GetCloudPrinterById(string id)
        {
            return Queues.SingleOrDefault(q => q.PrinterID == id);
        }

        public CloudPrinter GetCloudPrinterByName(string name)
        {
            return Queues.SingleOrDefault(q => q.Name == name);
        }

        public IEnumerable<CloudPrintJob> UpdateCloudPrintJobs(string printerid)
        {
            CloudPrinter printer = GetCloudPrinterById(printerid);

            if (printer != null)
            {
                return UpdateCloudPrintJobs(printer);
            }
            else
            {
                return new CloudPrintJob[0];
            }
        }

        public IEnumerable<CloudPrintJob> GetCloudPrintJobsForUser(string username)
        {
            return PrintJobs.Where(j => WindowsIdentityStore.IsAcceptedDomain(j.Domain) && j.Username == username);
        }

        public CloudPrintJob GetCloudPrintJobById(string id)
        {
            return _PrintJobs[id];
        }

        public void EnqueuePrintJob(string jobid)
        {
            PrintJobProcessor.AddJob(GetCloudPrintJobById(jobid));
        }

        public void UpdatePrintJob(CloudPrintJob job)
        {
            Logger.Log(LogLevel.Debug, "Updated job {0} with status {1}", job.JobID, job.Status.ToString());

            var reqdata = new
            {
                jobid = job.JobID,
                status = job.Status.ToString(),
                code = job.ErrorCode,
                message = job.ErrorMessage
            };

            HTTPHelper.PostCloudPrintUrlEncodedRequest(OAuthTicket, "control", reqdata);

            if (job.Status == CloudPrintJobStatus.DONE)
            {
                CloudPrintJob _job;
                _PrintJobs.TryRemove(job.JobID, out _job);
            }
        }

        public PrintTicket GetPrintTicket(CloudPrintJob job)
        {
            return new PrintTicket(new MemoryStream(HTTPHelper.GetResponseData(HTTPHelper.CreateRequest(OAuthTicket, job.TicketUrl))));
        }

        public Stream GetPrintDataStream(CloudPrintJob job)
        {
            byte[] data = HTTPHelper.GetResponseData(HTTPHelper.CreateRequest(OAuthTicket, job.FileUrl));
            return new MemoryStream(data);
        }

        public IEnumerable<CloudPrintJob> GetQueuedJobs()
        {
            return this.PrintJobProcessor.GetQueuedJobs();
        }

        public IEnumerable<CloudPrintJob> GetQueuedJobs(string username)
        {
            return this.PrintJobProcessor.GetQueuedJobs(username);
        }

        public void RestartDeferredJobs(string username)
        {
            this.PrintJobProcessor.RestartDeferredPrintJobs(username);
        }

        #endregion
    }
}
