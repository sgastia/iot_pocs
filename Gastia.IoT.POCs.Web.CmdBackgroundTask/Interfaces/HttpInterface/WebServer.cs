﻿using Gastia.IoT.POCs.Web.CmdBackgroundTask.Interfaces.DeviceInterfaces;
using Gastia.IoT.POCs.Web.CmdBackgroundTask.Managers;
using Gastia.IoT.POCs.Web.CmdBackgroundTask.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Gastia.IoT.POCs.Web.CmdBackgroundTask.Interfaces.HttpInterface
{
    internal class WebServer
    {
        private const uint BufferSize = 8192;
        private static int _port = 8000;
        private readonly StreamSocketListener _listener;
        private WebApiHelper _webApiHelper;
        private WebHelper _webhelper;
        private Devices _devices;
        private StartupTask _startupTask;

        private static readonly Webcam webcam = new Webcam();

        private static int calls = 0;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="serverPort">Port to start server on</param>
        internal WebServer(StartupTask st,int serverPort)
        {
            _startupTask = st;
            _webhelper = new WebHelper();
            _devices = new Devices();
            _listener = new StreamSocketListener();
            _webApiHelper = new WebApiHelper();
            _port = serverPort;
            _listener.ConnectionReceived += (s, e) =>
            {
                try
                {
                    if (_startupTask.IsClosing)
                    {
                        _listener.CancelIOAsync().GetResults();
                    }
                    else
                    {
                        // Process incoming request
                        processRequestAsync(e.Socket);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception in StreamSocketListener.ConnectionReceived(): " + ex.Message);
                }
            };
        }

        

        public async void StartServer()
        {
            try
            {
                await _webhelper.InitializeAsync();

#pragma warning disable CS4014
                _listener.BindServiceNameAsync(_port.ToString());
#pragma warning restore CS4014
            }
            catch(Exception ex)
            {
                Debug.WriteLine("Exception in StreamSocketListener.StartServer(): " + ex.Message);
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        /// <summary>
        /// Process the incoming request
        /// </summary>
        /// <param name="socket"></param>
        private async void processRequestAsync(StreamSocket socket)
        {
            try
            {
                StringBuilder request = new StringBuilder();
                using (IInputStream input = socket.InputStream)
                {
                    // Convert the request bytes to a string that we understand
                    byte[] data = new byte[BufferSize];
                    IBuffer buffer = data.AsBuffer();
                    uint dataRead = BufferSize;
                    while (dataRead == BufferSize)
                    {
                        await input.ReadAsync(buffer, BufferSize, InputStreamOptions.Partial);
                        request.Append(Encoding.UTF8.GetString(data, 0, data.Length));
                        dataRead = buffer.Length;
                    }
                }

                using (IOutputStream output = socket.OutputStream)
                {
                    // Parse the request
                    string[] requestParts = request.ToString().Split('\n');
                    string requestMethod = requestParts[0];
                    string[] requestMethodParts = requestMethod.Split(' ');

                    // Process the request and write a response to send back to the browser
                    if (requestMethodParts[0].ToUpper() == "GET")
                    {
                        Debug.WriteLine("request for: {0}", requestMethodParts[1]);
                        await WriteResponseAsync(requestMethodParts[1], output, socket.Information);
                    }
                    else if (requestMethodParts[0].ToUpper() == "POST")
                    {
                        string requestUri = string.Format("{0}?{1}", requestMethodParts[1], requestParts[requestParts.Length - 1]);
                        Debug.WriteLine("POST request for: {0} ", requestUri);
                        await WriteResponseAsync(requestUri, output, socket.Information);
                    }
                    else
                    {
                        throw new InvalidDataException("HTTP method not supported: " + requestMethodParts[0]);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception in processRequestAsync(): " + ex.Message);
            }
        }

        private async Task WriteResponseAsync(string requestUri, IOutputStream os, StreamSocketInformation socketInfo)
        {
            try
            {
                requestUri = requestUri.TrimEnd('\0'); //remove possible null from POST request

                string[] uriParts = requestUri.Split('/');

                
                if (requestUri == "/") 
                {
                    // Request for the root page, so redirect to home page
                    await redirectToPage(NavConstants.HOME_PAGE, os);
                    return;
                }
                else if (uriParts[1].ToLower().Contains("home") || uriParts[1].ToLower().Contains("index") || uriParts[1].ToLower().Contains("default"))
                {
                    // Request for the home page
                    string html = await GeneratePageHtml(NavConstants.HOME_PAGE);
                    await WebHelper.WriteToStream(html, os);
                    return;
                }
                else if (uriParts[1].ToLower() == "api")
                {
                    string json = await _webApiHelper.Execute(requestUri);
                    await WebHelper.WriteToStream(json, os);
                    return;
                }
                else// Request for a file that is in the Assets\Web folder (e.g. logo, css file)
                {
                    using (Stream resp = os.AsStreamForWrite())
                    {
                        // Map the requested path to Assets\Web folder
                        StorageFolder folder = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFolderAsync(NavConstants.ASSETSWEB);
                        bool exists = await RetrieveFile(requestUri, folder, resp);
                        if (!exists)
                        {
                            if (requestUri.Contains(NavConstants.TEMP_FOLDER))
                            {
                                folder = Windows.ApplicationModel.Package.Current.InstalledLocation;
                                exists = await RetrieveFile(requestUri, folder, resp);
                                if (!exists)
                                {
                                    // Send 404 not found if can't find file
                                    byte[] headerArray = Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length:0\r\nConnection: close\r\n\r\n");
                                    await resp.WriteAsync(headerArray, 0, headerArray.Length);

                                }
                            }
                            else
                            {

                                if (requestUri.Contains("favicon.ico"))//https://en.wikipedia.org/wiki/Favicon
                                {
                                    folder = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFolderAsync(NavConstants.ASSETSWEB);
                                    exists = await RetrieveFile(requestUri, folder, resp);
                                    if(!exists)
                                    {
                                        // Send 404 not found if can't find file
                                        byte[] headerArray = Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length:0\r\nConnection: close\r\n\r\n");
                                        await resp.WriteAsync(headerArray, 0, headerArray.Length);
                                    }
                                }
                                else
                                {
                                    //It raise warning message because web request shouldn't access to root installed folder
                                    byte[] headerArray = Encoding.UTF8.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length:0\r\nConnection: close\r\n\r\n");
                                    await resp.WriteAsync(headerArray, 0, headerArray.Length);
                                }
                            }
                        }
                        

                        await resp.FlushAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception in writeResponseAsync(): " + ex.Message);
                Debug.WriteLine(ex.StackTrace);

                // Log telemetry event about this exception
                var events = new Dictionary<string, string> { { "WebServer", ex.Message } };
                TelemetryManager.WriteTelemetryEvent("FailedToWriteResponse", events);

                try
                {
                    // Try to send an error page back if there was a problem servicing the request
                    string html = _webhelper.GenerateErrorPage("There's been an error: " + ex.Message + "<br><br>" + ex.StackTrace);
                    await WebHelper.WriteToStream(html, os);
                }
                catch (Exception e)
                {
                    TelemetryManager.WriteTelemetryException(e);
                }
            }
        }

        private async Task<bool> RetrieveFile(string requestUri, StorageFolder folder, Stream resp)
        {
            bool exists =false;
            try
            {
                string filePath = requestUri.Replace('/', '\\').Replace("%20"," ");

                // Open the file and write it to the stream
                using (Stream fs = await folder.OpenStreamForReadAsync(filePath))
                {
                    string contentType = "";
                    //https://developer.mozilla.org/es/docs/Web/HTTP/Basics_of_HTTP/MIME_types
                    if (requestUri.Contains(".css"))
                    {
                        contentType = "Content-Type: text/css\r\n";
                    }
                    else if (requestUri.Contains(".htm"))
                    {
                        contentType = "Content-Type: text/html\r\n";
                    }
                    else if (requestUri.Contains(".js"))
                    {
                        contentType = "Content-Type: application/javascript\r\n";
                    }
                    else if(requestUri.Contains(".jpg") || requestUri.Contains(".png") || requestUri.Contains(".gif") || requestUri.Contains(".ico"))//image
                    {
                        contentType = "Content-Type: image/gif, image/png, image/jpeg, image/jpg, image/bmp, image/webp, image/ico\r\n";
                    }
                    else if(requestUri.Contains(".mp4"))//video
                    {
                        contentType = "Content-Type: video/mp4\r\n";
                    }
                    else if(requestUri.Contains(".mp3"))//audio
                    {

                        contentType = "Content-Type: audio/mp3, audio/midi, audio/mpeg, audio/webm, audio/ogg, audio/wav\r\n";
                    }
                    
                    else
                    {
                        //unknown content type or not implemented yet
                        return false;
                    }


                    string header = String.Format("HTTP/1.1 200 OK\r\n" +
                                    "Content-Length: {0}\r\n{1}" +
                                    "Connection: close\r\n\r\n",
                                    fs.Length,
                                    contentType);
                    byte[] headerArray = Encoding.UTF8.GetBytes(header);
                    await resp.WriteAsync(headerArray, 0, headerArray.Length);
                    await fs.CopyToAsync(resp);

                    exists = true;
                }

            }
            catch (FileNotFoundException ex)
            {
                exists = false;

                // Log telemetry event about this exception
                var events = new Dictionary<string, string> { { "WebServer", ex.Message } };
                TelemetryManager.WriteTelemetryEvent("FailedToOpenStream", events);
            }

            
            return exists;
        }

        /// <summary>
        /// Get basic html for requested page, with list of stations populated
        /// </summary>
        /// <param name="requestedPage">nav enum ex: home.htm</param>
        /// <returns>string with full HTML, ready to have items replaced. ex: #onState#</returns>
        private async Task<string> GeneratePageHtml(string requestedPage)
        {
            string html = await _webhelper.GeneratePage(requestedPage);
            return html;
        }

        /// <summary>
        /// Redirect to a page
        /// </summary>
        /// <param name="path">Relative path to page</param>
        /// <param name="os"></param>
        /// <returns></returns>
        private async Task redirectToPage(string path, IOutputStream os)
        {
            using (Stream resp = os.AsStreamForWrite())
            {
                byte[] headerArray = Encoding.UTF8.GetBytes(
                                  "HTTP/1.1 302 Found\r\n" +
                                  "Content-Length:0\r\n" +
                                  "Location: /" + path + "\r\n" +
                                  "Connection: close\r\n\r\n");
                await resp.WriteAsync(headerArray, 0, headerArray.Length);
                await resp.FlushAsync();
            }
        }

        internal static string GetServerWebAddress()
        {
            var hostNames = NetworkInformation.GetHostNames();
            var hostName = hostNames.FirstOrDefault(name => name.Type == HostNameType.DomainName);
            return hostName.RawName + ":" + _port;
        }
    }
}
