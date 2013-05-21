using System;
using System.IO;
using System.Web;
using Microsoft.AspNet.SignalR;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Web.Routing;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SignalRChat
{
    /// <summary>
    /// A signalR hub for running KaSim and updating with sim results.
    /// We observe that we can't rely on object state being preserved between
    /// signalR method calls; different calls on the same connection may be made
    /// to methods on different objects. Hence we maintain a static map from 
    /// connection ids to process ID which is used when calling the StopSimulation
    /// method. Note that we could have included state in method calls except that
    /// state would not be recoverable in the OnDisconnected method, and hence
    /// we would be unable to stop simulations when e.g. a connection to the client 
    /// is lost. Note also that some reports indicate that state can be maintained 
    /// by settings members on the Clients.Caller object; however, this doesn't 
    /// appear to work.
    /// </summary>
    public class KaSimHub : Hub
    {
        // for capturing KaSim std out and err:
        private string mOut = null;
        private string mErr = null;

        // for mapping connection IDs to KaSim process IDs.
        private static Dictionary<string, int> connToProcDict = new Dictionary<string, int>();

        // for ensuring that temporary filenames are unique to the connected client / browser instance.
        private static int mRequestID = 0;


        /// <summary>
        /// Called when signalr connection disconnected.
        /// </summary>
        /// <returns></returns>
        public override Task OnDisconnected()
        {
            StopSimulation();
            return base.OnDisconnected();
        }


        /// <summary>
        /// Stops the simulation: kills the given process and sets stop flag to true for 
        /// thread to react on.
        /// </summary>
        public void StopSimulation()
        {
            if (!connToProcDict.ContainsKey(Context.ConnectionId))
            {
                return;
            }

            int procID = connToProcDict[Context.ConnectionId];
            connToProcDict.Remove(Context.ConnectionId);

            try
            {
                Process proc = Process.GetProcessById(procID);
                if (proc != null && !proc.HasExited)
                {
                    proc.Kill();
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception while stopping simulation: " + e);
            }
        }


        /// <summary>
        /// Helper method for sending a signalR message to the client.
        /// </summary>
        /// <param name="time"></param>
        /// <param name="plotSpec"></param>
        /// <param name="plotData"></param>
        /// <param name="message"></param>
        /// <param name="isComplete"></param>
        private void Send(float time, string[] plotSpec, string[] plotData, string message, Boolean isComplete)
        {            
            Clients.Caller.simulationUpdate(time, plotSpec, plotData, message, isComplete);
        }


        /// <summary>
        /// Gets the server application root where deployed files are stored, including the KaSim executable.
        /// </summary>
        /// <returns>The server application root.</returns>
        private String getAppRoot()
        {
            string appRoot = HttpContext.Current.Server.MapPath(@"~\");            
            return appRoot;
        }


        /// <summary>
        /// Monitors output from the KaSim process and reports back to client. 
        /// To be run in separate th
        /// </summary>
        /// <param name="outFilePath">The KaSim output file path.</param>
        private void MonitorKaSimOutput(Process proc, string outFilePath)  
        {
            // wait until the output file has been created.
            while (!File.Exists(outFilePath))
            {
                // if process exited, there was a KaSim error before starting simulation so report back.
                if (proc.HasExited)
                {
                    Send(0, null, null, mErr, true);
                    return;
                }

                Thread.Sleep(100);
            }

            // open output file steam.
            FileStream fileStream = File.Open(outFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var file = new StreamReader(fileStream);

            // for storing plot species and any messages.
            string[] plotSpec = null;
            string msg = null;

            // captures any partial line (i.e. with no newline) from most recent iteration.
            string partialLine = "";

            // loop until the process has stopped and there is no more output.           
            while (!proc.HasExited || !file.EndOfStream)
            {
                // if there is no new content, wait. 
                // would be better implemented without a busy loop - perhaps there is a file 
                // system watcher in .net?
                while (!proc.HasExited && file.EndOfStream)
                {
                    Thread.Sleep(100);
                }
                string content = file.ReadToEnd();

                // add any previous left over content:
                content = partialLine + content;

                // if last line not ended by newline, we get the empty string in last entry.
                string[] lines = content.Split(new string[] { "\n" }, int.MaxValue, System.StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    // check if this is a partial line, and continue if so.
                    Boolean isPartialLine = (i == lines.Length - 1) && !line.Equals("");
                    if (isPartialLine)
                    {
                        partialLine = line;
                        continue;
                    }

                    // split line on white space.
                    string[] entries = line.Split(new string[] { " " }, int.MaxValue, System.StringSplitOptions.RemoveEmptyEntries);

                    // just in case:
                    if (entries.Length <= 1)
                    {
                        continue;
                    }

                    // if we haven't parsed the plot species yet, this is the time.
                    if (plotSpec == null)
                    {
                        plotSpec = new string[entries.Length - 2];
                        for (int j = 2; j < entries.Length; j++)
                        {
                            plotSpec[j - 2] = entries[j];
                        }
                        Send(0, plotSpec, null, null, false);
                    }
                    // otherwise we can parse data points.
                    else
                    {
                        float time = (float)Double.Parse(entries[0]);
                        string[] dataPoints = new string[entries.Length - 1];
                        for (int j = 1; j < entries.Length; j++)
                        {
                            dataPoints[j - 1] = entries[j];
                        }

                        // also capture any output to std out and err; treat uniformly.
                        msg = null;
                        if (mOut != null)
                        {
                            msg = mOut;
                            mOut = null;
                        }
                        if (mErr != null)
                        {
                            msg += mErr;
                            mErr = null;
                        }
                        
                        // send to client, but only if the process hasn't been explicitly killed.
                        if (!proc.HasExited || proc.ExitCode == 0)
                        {
                            Send(time, null, dataPoints, msg, false);
                        }
                    }
                }
            };
            fileStream.Close();
            // done - send completion message to client.
            Send(0, null, null, "Simulation complete.", true);
        }
        


        /// <summary>
        /// Runs KaSim - this method is called directly from the client via signalR.
        /// </summary>
        /// <param name="kappaStr">The Kappa program.</param>
        /// <param name="customArgs">The KaSim command line parameters (excluding file i/o options).</param>
        public void RunKappa(string kappaStr, string customArgs)
        {            
            // get a request ID for ensuring that temp files are unique to this client / browser instance.
            long requestID;
            lock (GetType())
            {
                requestID = mRequestID++;
            }

            // reset strings for capturing KaSim std out and err.
            mOut = null;
            mErr = null;

            // construct paths to KaSim, infile and outfile.
            string appRoot = getAppRoot();            
            string exePath = appRoot + @"\KaSim.exe"; //Path.Combine(dir, @"\KaSim.exe");
            string inFilePath = appRoot + @"\model_" + requestID + ".ka"; // Path.Combine(dir, @"\testmodel.ka");
            string outFilePath = appRoot + @"\result.out_" + requestID + "out"; // Path.Combine(dir, @"\result.out");

            // construct exe arguments based on these files and custom arguments.
            string exeArguments = "-i \"" + inFilePath + "\" -o \"" + outFilePath + "\" " + customArgs;
            Clients.Caller.mStopped = false;
            try
            {
                // write kappa program to input file, and delete the output file if it exists.
                System.IO.File.WriteAllText(inFilePath, kappaStr);
                try { System.IO.File.Delete(outFilePath); }
                catch (Exception e) { }

                // start a KaSim process.                 
                Process proc = new Process();
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
                connToProcDict.Add(Context.ConnectionId, proc.Id);
                // run a thread for monitoring KaSim output. we could just to it here, given that 
                // we no longer wait for process to exit, but we need to return with process ID
                // for the client as this is needed when calling the StopSimulation method.
                new Thread(delegate()
                {
                    // Note that weird things happen if we let the thread have access to
                    // object members - hence pass relevant parameters directly.
                    try
                    {
                        MonitorKaSimOutput(proc, outFilePath);
                    }
                    finally
                    {
                        try
                        {
                            System.IO.File.Delete(outFilePath);
                            System.IO.File.Delete(inFilePath);
                        }
                        catch (Exception e) { }
                    }
                }).Start();               
                                 
            }
            catch (Exception e)
            {
                Send(0, null, null, "An error occurred while executing KaSim: " + e.Message + "/" + e.ToString() + "/" + exePath + "/" + inFilePath + "/" + outFilePath, false);

                try
                {
                    System.IO.File.Delete(outFilePath);
                    System.IO.File.Delete(inFilePath);
                }
                catch (Exception e2) { }
            }
        }


        /// <summary>
        /// Add to error message when KaSim prints to stdErr.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ErrorOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                mErr += e.Data;
            }
        }


        /// <summary>
        /// Add to output message when KaSim prints to stdOut.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ReadOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                mOut += e.Data + "\n";
            }
        }
    }
}