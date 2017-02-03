using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Xml.Linq;
using CloudPrint.Proxy;
using CloudPrint.Util;
using CloudPrint.InfoServer.Filters;
using System.IO;

namespace CloudPrint.InfoServer.Controllers
{
    class JobDataController : ApiController
    {
        protected CloudPrintProxy PrintProxy
        {
            get
            {
                return Request.GetPrintProxy();
            }
        }

        public HttpResponseMessage Get(string JobID)
        {
            CloudPrintJob job = PrintProxy.GetCloudPrintJobById(JobID);
            
            if (job == null)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            }

            string username = Request.GetSession()["username"];

            bool isadmin = WindowsIdentityStore.IsUserAdmin(username);

            if (isadmin || username == job.Username)
            {
                HttpResponseMessage response = new HttpResponseMessage
                {
                    Content = new StreamContent(new MemoryStream(job.GetPrintData()))
                };
                response.Content.Headers.ContentType.MediaType = "application/pdf";

                return response;
            }
            else
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden);
            }
        }
    }
}
