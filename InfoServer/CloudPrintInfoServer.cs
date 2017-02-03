﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Http;
using System.Web.Http.Dispatcher;
using System.Web.Http.Controllers;
using System.Web.Http.SelfHost;
using System.Threading;
using System.Reflection;
using CloudPrint.Proxy;
using CloudPrint.Util;

namespace CloudPrint.InfoServer
{
    public class CloudPrintInfoServer : IDisposable
    {
        protected HttpSelfHostServer Server;
        protected AuthServiceRegistration AuthServiceRegistration;

        public CloudPrintInfoServer(string baseurl, CloudPrintProxy printproxy)
        {
            HttpSelfHostConfiguration cfg = new HttpSelfHostConfiguration(baseurl);
            cfg.Routes.MapHttpRoute(
                "default",
                "{controller}/{id}",
                new { controller = "Home", id = RouteParameter.Optional }
            );
            cfg.Filters.Add(new Filters.HandleErrorFilter());
            cfg.Filters.Add(new Filters.CookiesFilter());
            cfg.Filters.Add(new Filters.WindowsAuthorizationFilter());
            cfg.Filters.Add(new Filters.CloudPrintProxyFilter { PrintProxy = printproxy });
            Server = new HttpSelfHostServer(cfg);
            AuthServiceRegistration = new AuthServiceRegistration(printproxy);
        }

        public CloudPrintInfoServer(int port, CloudPrintProxy printproxy)
            : this(String.Format("http://{0}:{1}", Environment.MachineName, port), printproxy)
        {
        }

        ~CloudPrintInfoServer()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected void Dispose(bool disposing)
        {
            Server.Dispose();

            if (disposing)
            {
                GC.SuppressFinalize(this);
            }
        }

        public void Start()
        {
            AuthServiceRegistration.Start();
            Server.OpenAsync().Wait();
        }
    }
}
