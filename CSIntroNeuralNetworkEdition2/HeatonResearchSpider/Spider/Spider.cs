// The Heaton Research Spider for .Net 
// Copyright 2007 by Heaton Research, Inc.
// 
// From the book:
// 
// HTTP Recipes for C# Bots, ISBN: 0-9773206-7-7
// http://www.heatonresearch.com/articles/series/20/
// 
// This class is released under the:
// GNU Lesser General Public License (LGPL)
// http://www.gnu.org/copyleft/lesser.html
//
using System;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;
using System.Text;
using System.Net;
using System.IO;
using HeatonResearch.Spider.Workload;
using HeatonResearch.Spider.Filter;
using HeatonResearch.Spider.Logging;

namespace HeatonResearch.Spider
{
    /// <summary>
    /// Spider: This is the main class that implements the Heaton
    /// Research Spider.
    /// </summary>
    public class Spider
    {
        /// <summary>
        /// The workload manager for the spider.
        /// </summary>
        public WorkloadManager Workload
        {
            get
            {
                return workloadManager;
            }
        }

        /// <summary>
        /// A list of URL filters to use.
        /// </summary>
        public List<SpiderFilter> Filters
        {
            get
            {
                return filters;
            }
        }

        /// <summary>
        /// The SpiderReportable object for the spider.  The spider
        /// will report all information to this class.
        /// </summary>
        public SpiderReportable Report
        {
            get
            {
                return report;
            }
        }

        /// <summary>
        /// Used to log spider events.  Using this object
        /// you can configure how the spider logs information.
        /// </summary>
        public Logger Logging
        {
            get
            {
                return logging;
            }
        }

        /// <summary>
        /// The configuration options for the spider.
        /// </summary>
        public SpiderOptions Options
        {
            get
            {
                return options;
            }
        }

        /// <summary>
        /// The object that the spider reports its findings to.
        /// </summary>
        private SpiderReportable report;

        /**
         * A flag that indicates if this process should be
         * canceled.
         */
        private bool cancel = false;

        /// <summary>
        /// The workload manager, the spider can use any of several
        /// different workload managers. The workload manager
        /// tracks all URL's found.
        /// </summary>
        private WorkloadManager workloadManager;

        /// <summary>
        /// The options for the spider.
        /// </summary>
        private SpiderOptions options;

        /// <summary>
        /// Filters used to block specific URL's.
        /// </summary>
        private List<SpiderFilter> filters = new List<SpiderFilter>();

        /// <summary>
        /// The time that the spider began.
        /// </summary>
        private DateTime startTime;

        /// <summary>
        /// The time that the spider ended.
        /// </summary>
        private DateTime stopTime;

        /// <summary>
        /// The logger.
        /// </summary>
        private Logger logging = new Logger();
     
        /// <summary>
        /// The types of link that can be encountered.
        /// </summary>
        public enum URLType
        {
            /// <summary>
            /// Hyperlinks from the &lt;A&gt; tag.
            /// </summary>
            HYPERLINK,
            /// <summary>
            /// Images from the &lt;IMG&gt; tag.
            /// </summary>
            IMAGE,
            /// <summary>
            /// External scripts from the &lt;SCRIPT&gt; tag.
            /// </summary>
            SCRIPT,
            /// <summary>
            /// External styles from the &lt;STYLE&gt; tag.
            /// </summary>
            STYLE
        }

        /// <summary>
        /// Construct a spider object. The options parameter
        /// specifies the options for this spider. The report
        /// parameter specifies the class that the spider is to
        /// report progress to.
        /// </summary>
        /// <param name="options">The configuration options for this spider.</param>
        /// <param name="report">A SpiderReportable class to report progress to</param>
        public Spider(SpiderOptions options, SpiderReportable report)
        {
            this.options = options;
            this.report = report;

            this.workloadManager = (WorkloadManager)Assembly.GetExecutingAssembly().CreateInstance(this.options.WorkloadManager);

            this.workloadManager.Init(this);
            report.Init(this);

            // add filters
            if (options.Filter != null)
            {
                foreach (String name in options.Filter)
                {
                    SpiderFilter filter = (SpiderFilter)Assembly.GetExecutingAssembly().CreateInstance(name);
                    if (filter == null)
                        throw new SpiderException("Invalid filter specified: " + name);
                    this.filters.Add(filter);
                }
            }

            // perform startup
            if (String.Compare(options.Startup, SpiderOptions.STARTUP_RESUME) == 0)
            {
                this.workloadManager.Resume();
            }
            else
            {
                this.workloadManager.Clear();
            }
        }

        /// <summary>
        /// Add a URL for processing. Accepts a SpiderURL.
        /// </summary>
        /// <param name="url">The URL to add.</param>
        /// <param name="source">Where this URL was found.</param>
        /// <param name="depth">The depth of this URL.</param>
        public void AddURL(Uri url, Uri source, int depth)
        {
            // Check the depth.
            if ((this.options.MaxDepth != -1) && (depth > this.options.MaxDepth))
            {
                return;
            }

            // Check to see if it does not pass any of the filters.
            foreach (SpiderFilter filter in this.filters)
            {
                if (filter.IsExcluded(url))
                {
                    return;
                }
            }

            // Add the item.
            if (this.workloadManager.Add(url, source, depth))
            {
                StringBuilder str = new StringBuilder();
                str.Append("Adding to workload: ");
                str.Append(url);
                str.Append("(depth=");
                str.Append(depth);
                str.Append(")");
                logging.Log(Logger.Level.INFO, str.ToString());
            }
        }

        /// <summary>
        /// This will halt the spider.
        /// </summary>
        public void Cancel()
        {
            this.cancel = true;
        }

        /// <summary>
        /// Generate basic status information about the spider.
        /// </summary>
        public String Status
        {
            get
            {
                StringBuilder result = new StringBuilder();
                TimeSpan duration = stopTime - startTime;
                result.Append("Start time:");
                result.Append(this.startTime.ToString());
                result.Append('\n');
                result.Append("Stop time:");
                result.Append(this.stopTime.ToString());
                result.Append('\n');
                result.Append("Minutes Elapsed:");
                result.Append(duration);
                result.Append('\n');

                return result.ToString();
            }
        }

        /// <summary>
        /// Called to start the spider.
        /// </summary>
        public void Process()
        {
            this.cancel = false;
            this.startTime = DateTime.Now;

            // Process all hosts/
            do
            {
                ProcessHost();
            } while (this.workloadManager.NextHost() != null);

            this.stopTime = DateTime.Now;
        }

        /// <summary>
        /// Process one individual host.
        /// </summary>
        private void ProcessHost()
        {
            Uri url = null;

            String host = this.workloadManager.GetCurrentHost();

            // First, notify the manager.
            if (!this.report.BeginHost(host))
            {
                return;
            }

            // Second, notify any filters of a new host/
            foreach (SpiderFilter filter in this.filters)
            {
                try
                {
                    filter.NewHost(host, this.options.UserAgent);
                }
                catch (IOException e)
                {
                    logging.Log(Logger.Level.INFO, "Error while reading robots.txt file:"
                        + e.Message);
                }
            }

            // Now process this host.
            do
            {
                url = this.workloadManager.GetWork();
                if (url != null)
                {

                    WaitCallback w = new WaitCallback(SpiderWorkerProc);
                    ThreadPool.QueueUserWorkItem(w, url);
                }
                else
                {
                    this.workloadManager.WaitForWork(60);
                }
            } while (!this.cancel && !workloadManager.WorkloadEmpty());
        }


     
        /// <summary>
        /// This method is called by the thread pool to process one
        /// single URL.
        /// </summary>
        /// <param name="stateInfo">Not used.</param>
        private void SpiderWorkerProc(Object stateInfo)
        {
            Stream istream = null;
            WebRequest http;
            HttpWebResponse response;
            Uri url = null;
            try
            {
                url = (Uri)stateInfo;
                logging.Log(Logger.Level.INFO, "Processing: " + url);
                // Get the URL's contents.
                http = HttpWebRequest.Create(url);
                http.Timeout = this.options.Timeout;
                if (this.options.UserAgent != null)
                {
                    http.Headers["User-Agent"] = this.options.UserAgent;
                }
                response = (HttpWebResponse)http.GetResponse();


                // Read the URL.
                istream = response.GetResponseStream();

                // Parse the URL.
                if (String.Compare(response.ContentType, "text/html") == 0)
                {
                    SpiderParseHTML parse = new SpiderParseHTML(response.ResponseUri,
                        new SpiderInputStream(istream, null), this);
                    this.report.SpiderProcessURL(url, parse);
                }
                else
                {
                    this.report.SpiderProcessURL(url, istream);
                }

            }
            catch (IOException e)
            {
                logging.Log(Logger.Level.INFO, "I/O error on URL:" + url);
                try
                {
                    this.workloadManager.MarkError(url);
                }
                catch (WorkloadException)
                {
                    logging.Log(Logger.Level.ERROR, "Error marking workload(1).", e);
                }
                this.report.SpiderURLError(url);
                return;
            }
            catch (WebException e)
            {
                logging.Log(Logger.Level.INFO, "Web error on URL:" + url);
                try
                {
                    this.workloadManager.MarkError(url);
                }
                catch (WorkloadException)
                {
                    logging.Log(Logger.Level.ERROR, "Error marking workload(2).", e);
                }
                this.report.SpiderURLError(url);
                return;
            }
            catch (Exception e)
            {
                try
                {
                    this.workloadManager.MarkError(url);
                }
                catch (WorkloadException)
                {
                    logging.Log(Logger.Level.ERROR, "Error marking workload(3).", e);
                }

                logging.Log(Logger.Level.ERROR, "Caught exception at URL:" + url.ToString(), e);
                this.report.SpiderURLError(url);
                return;
            }
            finally
            {
                if (istream != null)
                {

                    istream.Close();

                }
            }

            try
            {
                // Mark URL as complete.
                this.workloadManager.MarkProcessed(url);
                logging.Log(Logger.Level.INFO, "Complete: " + url);
                if (!url.Equals(response.ResponseUri))
                {
                    // save the URL(for redirect's)
                    this.workloadManager.Add(response.ResponseUri, url,
                        this.workloadManager.GetDepth(response.ResponseUri));
                    this.workloadManager.MarkProcessed(response.ResponseUri);
                }
            }
            catch (WorkloadException e)
            {
                logging.Log(Logger.Level.ERROR, "Error marking workload(3).", e);
            }

        }
    }
}
