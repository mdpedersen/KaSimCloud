using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Diagnostics;
using System.IO;
using Microsoft.AspNet.SignalR;
using System.Web;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;

namespace MvcWebRole1.Controllers
{
    public class KaSimCloudController : ApiController
    {

        // GET api/values/5
        [HttpGet]
        public HttpResponseMessage Get(string id)
        {            
            if (id.Equals("wrapper"))
            {
                return ProcessTemplate("KaSimCloudWrapper.js");
            }
            else if (id.Equals("json"))
            {
                return ProcessTemplate("KaSimCloudJSON.js");
            }
            else if (id.Equals("kasimex"))
            {
                return ProcessTemplate("KaSimEx.js");
            } 
            else   
            {
                return ProcessTemplate("");
            }
        }

        private HttpResponseMessage ProcessTemplate(string templatefile) {
            string js = "";
            try
            {
                js = System.IO.File.ReadAllText(HttpContext.Current.Server.MapPath(@"~\") + @"\" + templatefile);
            }
            catch (Exception e)
            {
                js = e.Message;
            }


            string baseURL = @"http://" + Request.Headers.GetValues("Host").FirstOrDefault();
            js = Regex.Replace(js, "{BASEURL}", baseURL);

            string result = js;
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Headers.Add("Access-Control-Allow-Origin", "*");                        
            resp.Headers.Add("Access-Control-Allow-Credentials", "true");
            resp.Content = new StringContent(result, System.Text.Encoding.UTF8, "text/plain");
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("text/javascript");
            return resp;
        }
    }
}