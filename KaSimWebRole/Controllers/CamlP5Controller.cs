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

namespace MvcWebRole1.Controllers
{

    /// <summary>
    /// Preliminary experiment for getting CamplP5 running on Azure.
    /// </summary>
    public class CamlP5Controller : ApiController
    {
        string mErr = null;
        string mOut = null;
        
        
        /*static string path = Path.Combine(Environment.GetEnvironmentVariable("RoleRoot") + @"\", @"approot");
        //string ocamlExePath = Path.Combine(Environment.GetEnvironmentVariable("RoleRoot") + @"\", @"approot\ocamlc.exe");
        string ocamlExePath = path + @"\ocamlc.exe";
         
        private void executeAll()
        {
            mErr = null;
            string exeArguments = null;

            String files = "";
            foreach (string fileName in Directory.GetFiles(path, "*.*"))
            {
                files = files + fileName + "\n";
            }


            if (!execute(path + @"\ocamlc", "--help"))
            {
                return;
            }


            exeArguments = "-c -I +camlp5 -pp camlp5o pa_extension.ml";
            if (!execute(ocamlExePath, exeArguments))
            {
                return;
            }

            exeArguments = "-c '" + path + @"\speciesxxx.ml'";
            if (!execute(ocamlExePath, exeArguments))
            {
                return;
            }

            exeArguments = @"-c -I +camlp5 -pp 'camlp5o " + path + @"\pa_extension.cmo' bar";
            if (!execute(ocamlExePath, exeArguments))
            {
                return;
            }

            exeArguments = "@-o '" + path + @"\bar.exe' species.cmo bar.cmo";
            if (!execute(ocamlExePath, exeArguments))
            {
                return;
            }


            if (!execute( path + @"\bar.exe", ""))
            {
                return;
            }
        }
        */
         
        private void ErrorOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                mErr += e.Data;
            }
        }

        private void ReadOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                mOut = e.Data;
            }
        }

        /*private bool execute(string exePath, string exeArguments) {
            mOut = "";
            mErr = "";

            try
            {
                Process proc;
                proc = new Process();
                proc.StartInfo.FileName = exePath;

                proc.StartInfo.Arguments = exeArguments;

                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.StartInfo.UseShellExecute = false;
                proc.OutputDataReceived += new DataReceivedEventHandler(ReadOutput);
                proc.ErrorDataReceived += new DataReceivedEventHandler(ErrorOutput);

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();

                
                // seems that we CANNOT to sync reads on both streams at same time:
                // http://msdn.microsoft.com/en-us/library/system.diagnostics.process.standardoutput.aspx
                //mOut = proc.StandardOutput.ReadToEnd();
                //mErr = proc.StandardError.ReadToEnd();

                // must come after reading from output/err streams!
                //proc.WaitForExit();
                //output = System.IO.File.ReadAllText(inFilePath);
                //output = System.IO.File.ReadAllText(outFilePath);

            }
            catch (Exception e)
            {
                mErr = e.Message + "/" + e.ToString();
            }

            return mErr == null || mErr.Equals("");

        }
         */


        private void execute()
        {
            mOut = "";
            mErr = "";

            //string exePath = Path.Combine(Environment.GetEnvironmentVariable ("RoleRoot") + @"\", @"approot\bin\KaSim.exe");
            string exePath =   Path.Combine(Environment.GetEnvironmentVariable("RoleRoot") + @"\", @"approot\KaSim.exe");
            string inFilePath = Path.Combine(Environment.GetEnvironmentVariable("RoleRoot") + @"\", @"approot\testmodel.ka");
            string outFilePath = Path.Combine(Environment.GetEnvironmentVariable("RoleRoot") + @"\", @"approot\result.out");

            string exeArguments = "-i \"" + inFilePath + "\" -o \"" + outFilePath + "\" -e 1000 -p 100";
            try
            {
                string kappaStr = @"
%agent: a(x~u~p)
%agent: b(y~u~p)

a(x), b(y) -> a(x!1), b(y!1) @1
a(x!1), b(y!1) -> a(x), b(y) @1
a(x!1), b(y~u!1) -> a(x!1), b(y~p!1) @1

%init: 100000 a(x~u)
%init: 300000 b(y~u)
%obs: 'a phos' b(y~u)";

                System.IO.File.WriteAllText(inFilePath, kappaStr);
                System.IO.File.Delete(outFilePath);

                Process proc;
                proc = new Process();
                proc.StartInfo.FileName = exePath;
                proc.StartInfo.Arguments = exeArguments;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.StartInfo.UseShellExecute = false;
                proc.OutputDataReceived += new DataReceivedEventHandler(ReadOutput);
                proc.ErrorDataReceived += new DataReceivedEventHandler(ErrorOutput);

                proc.Start();
                proc.WaitForExit();
                //output = System.IO.File.ReadAllText(inFilePath);
                mOut = System.IO.File.ReadAllText(outFilePath);
                
            }
            catch (Exception e)
            {
                mErr = e.Message + "/" + e.ToString();
            }
             
        }

        // GET api/values
        public IEnumerable<string> Get()
        {
            execute();
            return new string[] { mOut, mErr };

            
        }

        // GET api/values/5
        [HttpGet]
        public HttpResponseMessage Get(int id)
        {
            string js = "";
            try
            {
                js = System.IO.File.ReadAllText(HttpContext.Current.Server.MapPath(@"~\") + @"\KaSimCloudWrapper.js");
            }
            catch (Exception e)
            {
                js = e.Message;
            }

            
            string baseURL = Request.Headers.GetValues("Host").FirstOrDefault();
            js = Regex.Replace(js, "{BASEURL}", baseURL);            
            
            string result = "js: " + js + "\n\n\n host is: " + baseURL + "</h1>";
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Headers.Add("Access-Control-Allow-Origin", "*");
            resp.Headers.Add("Access-Control-Allow-Credentials", "true");
            resp.Content = new StringContent(result, System.Text.Encoding.UTF8, "text/plain");
            return resp;
        }

        // GET api/values/5
        /*public string Get(int id)
        {
            return "value";
        }*/

        // POST api/values
        public void Post([FromBody]string value)
        {
        }

        // PUT api/values/5
        public void Put(int id, [FromBody]string value)
        {
        }

        // DELETE api/values/5
        public void Delete(int id)
        {
        }
    }
}