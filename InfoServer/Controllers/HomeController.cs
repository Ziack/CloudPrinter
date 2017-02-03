﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Xml.Linq;
using CloudPrint.Proxy;
using CloudPrint.Util;
using CloudPrint.InfoServer.Filters;

namespace CloudPrint.InfoServer.Controllers
{
    public class HomeController : XHtmlController
    {
        protected CloudPrintProxy PrintProxy
        {
            get
            {
                return Request.GetPrintProxy();
            }
        }

        protected HttpResponseMessage Page(params object[] elements)
        {
            try
            {
                bool isadmin = WindowsIdentityStore.IsUserAdmin(Session["username"]);

                return Html(
                    Head("Cloud Print Server"),
                    Body(
                        H1("Cloud Print Server"),
                        new XElement("p", "Welcome to the Cloud Print Server, " + Session["username"]),
                        elements,
                        new XElement("p",
                            "Please go to ",
                            new XElement("a",
                                new XAttribute("href", "http://google.com/cloudprint"),
                                "Google Cloud Print"
                            ),
                            " to manage your printers."
                        ),
                         PrintProxy.OAuthTicket == null ? null: new XElement("p", new XElement("p", PrintProxy.OAuthTicket.AccessToken)),
                        //isadmin ? new XElement("p", new XElement("a", new XAttribute("href", Url.Route("default", new { controller = "Auditron" })), "Auditron Username to Printer Code mapping")) : null,
                        this.PrintProxy.Queues == null ? null : new XElement("dl",
                            new XElement("dt", "This server is sharing the following printers:"),
                            new XElement("dd",
                                new XElement("ul",
                                    this.PrintProxy.Queues.Select(q =>
                                        new XElement("li",
                                            new XElement("a",
                                                new XAttribute("href", "https://www.google.com/cloudprint#printer/id/" + q.PrinterID),
                                                q.Name
                                            )
                                        )
                                    )
                                )
                            ),
                            new XElement("dt", "This server has received the following print jobs:"),
                            this.PrintProxy.PrintJobs == null ? null : new XElement("dd",
                                new XElement("ul",
                                    this.PrintProxy.PrintJobs.Where(j => isadmin || j.Username == Session["username"])
                                                   .OrderByDescending(j => j.CreateTime)
                                                   .Select(j =>
                                        new XElement("li",
                                            new XElement("dl",
                                                new XElement("dt", 
                                                    new XElement("a",
                                                        new XAttribute("href", "JobData/?JobID=" + j.JobID),
                                                        j.JobTitle
                                                    )
                                                ),
                                                isadmin ? new XElement("dd", "Username: " + j.Username) : null,
                                                new XElement("dd", "Status: " + j.Status.ToString()),
                                                new XElement("dd", "Last Updated: " + j.UpdateTime.ToLocalTime().ToString("dd MMM yyyy  hh:mm tt"))
                                            )
                                        )
                                    )
                                )
                            )
                        )
                    )
                );
            }
            catch (Exception ex)
            {
                return Html(
                    Head("Cloud Print Server"),
                    Body(
                        H1("Cloud Print Server"),
                        new XElement("pre", "Error: " + ex.ToString())
                    )
                );
            }
        }

        public HttpResponseMessage Get()
        {
            bool isadmin = WindowsIdentityStore.IsUserAdmin(Session["username"]);

            if (!PrintProxy.IsRegistered)
            {
                return Page(
                    new XElement("div", "This cloud print proxy is not yet registered"),
                    isadmin ? new XElement("div", new XElement("a", new XAttribute("href", Url.Route("default", new { controller = "Register" })), "Register the print proxy")) : null
                );
            }
            else
            {
                HttpResponseMessage page = Page();
                this.PrintProxy.RestartDeferredJobs(Session["username"]);
                return page;
            }
        }
    }
}
