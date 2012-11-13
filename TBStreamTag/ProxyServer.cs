using System;
using System.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TBStreamTag;
using System.Security.Cryptography;
using System.Diagnostics;

namespace HTTPProxyServer
{
    public sealed class ProxyServer
    {
        // From http://www.codeproject.com/Articles/93301/Implementing-a-Multithreaded-HTTP-HTTPS-Debugging

        private static readonly ProxyServer _server = new ProxyServer();
        
        private static readonly int BUFFER_SIZE = 65535;
        private static readonly char[] semiSplit = new char[] { ';' };
        private static readonly char[] equalSplit = new char[] { '=' };
        private static readonly String[] colonSpaceSplit = new string[] { ": " };
        private static readonly char[] spaceSplit = new char[] { ' ' };
        private static readonly char[] commaSplit = new char[] { ',' };
        private static readonly Regex cookieSplitRegEx = new Regex(@",(?! )");
        private static X509Certificate2 _certificate;
        private static object _outputLockObj = new object();


        private const string certFile = "cert.cer";
        private const string listeningIPInterface = "127.0.0.1";
        private const string listeningPort = "8888";

        private TcpListener _listener;
        private Thread _listenerThread;
        private Thread _cacheMaintenanceThread;

        public IPAddress ListeningIPInterface
        {
            get
            {
                IPAddress addr = IPAddress.Loopback;
                if (listeningIPInterface != null)
                    IPAddress.TryParse(listeningIPInterface, out addr);

                return addr;
            }
        }

        public Int32 ListeningPort
        {
            get
            {
                Int32 port = 8081;
                if(listeningPort != null)
                    Int32.TryParse(listeningPort,out port);
                
                return port;
            }
        }

        private ProxyServer()
        {
            _listener = new TcpListener(ListeningIPInterface, ListeningPort);
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
        }

        public Boolean DumpHeaders { get; set; }
        public Boolean DumpPostData { get; set; }
        public Boolean DumpResponseData { get; set; }

        public static ProxyServer Server
        {
            get { return _server; }
        }

        public bool Start()
        {
            Console.WriteLine("Listening at " + listeningIPInterface + ":" + listeningPort);

            try
            {
                String certFilePath = String.Empty;
                if (certFile != null)
                    certFilePath = certFile;
                try
                {
                    _certificate = new X509Certificate2(certFilePath);
                }
                catch (Exception ex)
                {
                    
                }
                _listener.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }

            _listenerThread = new Thread(new ParameterizedThreadStart(Listen));
            _cacheMaintenanceThread = new Thread(new ThreadStart(ProxyCache.CacheMaintenance));

            _listenerThread.Start(_listener);
            //_cacheMaintenanceThread.Start();

            return true;
        }

        public void Stop()
        {
            _listener.Stop();

            //wait for server to finish processing current connections...

            _listenerThread.Abort();
            _cacheMaintenanceThread.Abort();
            _listenerThread.Join();
            _listenerThread.Join();
        }

        private static void Listen(Object obj)
        {
            TcpListener listener = (TcpListener)obj;
            try
            {
                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();
                    while (!ThreadPool.QueueUserWorkItem(new WaitCallback(ProxyServer.ProcessClient), client)) ;
                }
            }
            catch (ThreadAbortException) { }
            catch (SocketException) { }
        }


        private static void ProcessClient(Object obj)
        {
            TcpClient client = (TcpClient)obj;
            try
            {
                DoHttpProcessing(client);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                client.Close();
            }
        }

        private static void DoHttpProcessing(TcpClient client)
        {
            Stream clientStream = client.GetStream();
            Stream outStream = clientStream; //use this stream for writing out - may change if we use ssl
            SslStream sslStream = null;
            StreamReader clientStreamReader = new StreamReader(clientStream);
            CacheEntry cacheEntry = null;
            MemoryStream cacheStream = null;
            
            if (Server.DumpHeaders || Server.DumpPostData || Server.DumpResponseData)
            {
                //make sure that things print out in order - NOTE: this is bad for performance
                Monitor.TryEnter(_outputLockObj, TimeSpan.FromMilliseconds(-1.0));
            }
            
            try
            {
                //read the first line HTTP command
                String httpCmd = clientStreamReader.ReadLine();
                if (String.IsNullOrEmpty(httpCmd))
                {
                    clientStreamReader.Close();
                    clientStream.Close();
                    return;
                }
                //break up the line into three components
                String[] splitBuffer = httpCmd.Split(spaceSplit, 3);

                String method = splitBuffer[0];
                String remoteUri = splitBuffer[1];
                Version version = new Version(1, 0);

                HttpWebRequest webReq;
                HttpWebResponse response = null;
                if (splitBuffer[0].ToUpper() == "CONNECT")
                {
                    //Browser wants to create a secure tunnel
                    //instead = we are going to perform a man in the middle "attack"
                    //the user's browser should warn them of the certification errors however.
                    //Please note: THIS IS ONLY FOR TESTING PURPOSES - you are responsible for the use of this code
                    remoteUri = "https://" + splitBuffer[1];
                    while (!String.IsNullOrEmpty(clientStreamReader.ReadLine())) ;
                    StreamWriter connectStreamWriter = new StreamWriter(clientStream);
                    connectStreamWriter.WriteLine("HTTP/1.0 200 Connection established");
                    connectStreamWriter.WriteLine(String.Format("Timestamp: {0}", DateTime.Now.ToString()));
                    connectStreamWriter.WriteLine("Proxy-agent: tb-proxy");
                    connectStreamWriter.WriteLine();
                    connectStreamWriter.Flush();

                    sslStream = new SslStream(clientStream, false);
                    try
                    {
                        sslStream.AuthenticateAsServer(_certificate, false, SslProtocols.Tls | SslProtocols.Ssl3 | SslProtocols.Ssl2, true);
                    }
                    catch (Exception)
                    {
                        sslStream.Close();
                        clientStreamReader.Close();
                        connectStreamWriter.Close();
                        clientStream.Close();
                        return;
                    }

                    //HTTPS server created - we can now decrypt the client's traffic
                    clientStream = sslStream;
                    clientStreamReader = new StreamReader(sslStream);
                    outStream = sslStream;
                    //read the new http command.
                    httpCmd = clientStreamReader.ReadLine();
                    if (String.IsNullOrEmpty(httpCmd))
                    {
                        clientStreamReader.Close();
                        clientStream.Close();
                        sslStream.Close();
                        return;
                    }
                    splitBuffer = httpCmd.Split(spaceSplit, 3);
                    method = splitBuffer[0];
                    remoteUri = remoteUri + splitBuffer[1];
                }

                //construct the web request that we are going to issue on behalf of the client.
                webReq = (HttpWebRequest)HttpWebRequest.Create(remoteUri);
                webReq.Method = method;
                webReq.ProtocolVersion = version;

                //read the request headers from the client and copy them to our request
                int contentLen = ReadRequestHeaders(clientStreamReader, webReq);
                
                webReq.Proxy = null;
                webReq.KeepAlive = false;
                webReq.AllowAutoRedirect = true;
                webReq.ProtocolVersion = HttpVersion.Version10;
                webReq.AutomaticDecompression = DecompressionMethods.None;


                if(Server.DumpHeaders)
                {
                    Console.WriteLine(String.Format("{0} {1} HTTP/{2}",webReq.Method,webReq.RequestUri.AbsoluteUri, webReq.ProtocolVersion));
                    DumpHeaderCollectionToConsole(webReq.Headers);
                }

                //using the completed request, check our cache
                if (method.ToUpper() == "GET")
                    cacheEntry = ProxyCache.GetData(webReq);
                else if (method.ToUpper() == "POST")
                {
                    char[] postBuffer = new char[contentLen];
                    int bytesRead;
                    int totalBytesRead = 0;
                    StreamWriter sw = new StreamWriter(webReq.GetRequestStream());
                    while (totalBytesRead < contentLen && (bytesRead = clientStreamReader.ReadBlock(postBuffer, 0, contentLen)) > 0)
                    {
                        totalBytesRead += bytesRead;
                        sw.Write(postBuffer, 0, bytesRead);
                        if (ProxyServer.Server.DumpPostData)
                            Console.Write(postBuffer, 0, bytesRead);
                    }
                    if (Server.DumpPostData)
                    {
                        Console.WriteLine();
                        Console.WriteLine();
                    }

                    sw.Close();
                }

                if (cacheEntry == null)
                {
                    //Console.WriteLine(String.Format("ThreadID: {2} Requesting {0} on behalf of client {1}", webReq.RequestUri, client.Client.RemoteEndPoint.ToString(), Thread.CurrentThread.ManagedThreadId));
                    webReq.Timeout = 15000;

                    try
                    {
                        response = (HttpWebResponse)webReq.GetResponse();
                    }
                    catch (WebException webEx)
                    {
                        response = webEx.Response as HttpWebResponse;
                    }
                    if (response != null)
                    {
                        
                        // If TB Streamtags are requested
                        bool isTB = false;
                        string contentStr = "";
                        if (webReq.RequestUri.OriginalString.Contains("ChannelID=884") && webReq.RequestUri.OriginalString.Contains("Channel=TechnoBase"))
                        {
                            isTB = true; // yep
                            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] TechnoBase StreamTags requested"); // report to user

                            Random rnd = new Random(DateTime.Now.Millisecond);
                            TBParser tbp = new TBParser();
                            List<Track> trackList = tbp.getTracks();

                            string currentTimeStr = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff");
                            StringBuilder sb = new StringBuilder(); // Build StreamTag Response
                            sb.Append("count=" + trackList.Count);
                            sb.Append("<br>");
                            sb.Append("vintern=1452");
                            sb.Append("<br>");
                            sb.Append("clistvintern=2160");
                            sb.Append("<br>");
                            sb.Append("time-ref=" + currentTimeStr);
                            sb.Append("<br>");
                            sb.Append("offset=01.01.1900 00:00:00");
                            sb.Append("<br>");
                            sb.Append("serverTime=" + currentTimeStr);
                            sb.Append("<br>");
                            sb.Append("FSListVIntern=100");
                            sb.Append("<br>");
                            sb.Append("ChargedListVIntern=103");
                            sb.Append("<br>");
                            sb.Append("rankingvintern=0");
                            sb.Append("<br>");
                            sb.Append("ResCode=0");
                            sb.Append("<br>");
                            sb.Append("ComTimeStamp=0");
                            sb.Append("<br>");
                            sb.Append("PeerFlag=0");
                            sb.Append("<br>");

                            foreach (Track trk in trackList)
                            {
                                if (trk.ID > 0)
                                {
                                    string songMD5 = GetMD5Hash(trk.ToString()); // Generate unique Track ID
                                    string songID9 = songMD5.Substring(0, 9);
                                    string songID6 = songMD5.Substring(0, 6);
                                    string songID4 = songMD5.Substring(0, 4);
                                    sb.Append("49127-12110 ");
                                    sb.Append(DateTime.Now.ToString("dd.MM.yyyy") + " "); // To be fixed (Incorrect date, might cause problems at 23:59 - 00:00)
                                    sb.Append(trk.TimeStart.ToString("HH:mm:ss.fff") + " "); // Time Start
                                    sb.Append(DateTime.Now.ToString("dd.MM.yyyy") + " "); // To be fixed (Incorrect date, might cause problems at 23:59 - 00:00)
                                    sb.Append(trk.TimeEnd.ToString("HH:mm:ss.fff") + " "); // Time End
                                    sb.Append("\"" + trk.Title + "\" "); // Title
                                    sb.Append("8 "); // 8 seems to work...
                                    sb.Append(songID9 + " "); // Song ID
                                    sb.Append("\"" + trk.Artist + "\" "); // Artist
                                    sb.Append("\"\" "); // Album
                                    sb.Append("\"\" "); // URL
                                    sb.Append("\"0\" "); // ?
                                    sb.Append("\"2000\" "); // Year
                                    sb.Append("\"\" "); // ?
                                    sb.Append("\"\" "); // ?
                                    sb.Append("\"\" "); // Shop URL
                                    sb.Append("5 "); // ?
                                    sb.Append(songID6 + " "); // Song ID
                                    sb.Append(songID4 + " "); // Song ID
                                    sb.Append("0 ");
                                    sb.Append("0 ");
                                    sb.Append("\"" + trk.CoverURL + "\" "); // Cover Image URL
                                    sb.Append(songID9 + " "); // Song ID
                                    sb.Append("<br>");

                                    // Example Response:
                                    // 49127-12110 01.11.2012 17:24:33.393 01.11.2012 17:29:34.307 "Everything" 5 331039793 "Safri Duo" "Episode II" "http://www.safriduo.dk/" "151" "2001" "" "http://www.amazon.de/Episode-II-Safri-Duo/dp/B00005KKA5/ref=sr_1_2?ie=UTF8&s=music&qid=1216825657&sr=8-2" "http://www.amazon.de/Episode-II-Safri-Duo/dp/B00005KKA5/ref=sr_1_2?ie=UTF8&s=music&qid=1216825657&sr=8-2" 4 1083354 5870,5 0 0 "http://ecx.images-amazon.com/images/I/41W4SZVXERL._SX240_.jpg" "112545908757223"
                                }
                            }

                            contentStr = sb.ToString();
                        }
                        
                        List<Tuple<String, String>> responseHeaders = ProcessResponse(response, contentStr.Length);
                        StreamWriter myResponseWriter = new StreamWriter(outStream);
                        Stream responseStream = response.GetResponseStream();

                        try
                        {
                            //send the response status and response headers   
                            WriteResponseStatus(response.StatusCode, response.StatusDescription, myResponseWriter);
                            WriteResponseHeaders(myResponseWriter, responseHeaders);

                            DateTime? expires = null;
                            CacheEntry entry = null;
                            Boolean canCache = (sslStream == null && ProxyCache.CanCache(response.Headers, ref expires));
                            if (canCache)
                            {
                                entry = ProxyCache.MakeEntry(webReq, response,responseHeaders, expires);
                                if (response.ContentLength > 0)
                                    cacheStream = new MemoryStream(entry.ResponseBytes);
                            }


                            Byte[] buffer;
                            if (isTB)
                            {
                                buffer = Encoding.Convert(Encoding.UTF8, Encoding.GetEncoding("iso-8859-1"), Encoding.UTF8.GetBytes(contentStr));
                            }
                            else
                            {
                                if (response.ContentLength > 0)
                                    buffer = new Byte[response.ContentLength];
                                else
                                    buffer = new Byte[BUFFER_SIZE];
                            }

                            int bytesRead;

                            if (isTB)
                            {
                                //myResponseWriter.WriteLine(contentStr);
                                outStream.Write(buffer, 0, buffer.Length);
                            }
                            else{
                                while ((bytesRead = responseStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    if (cacheStream != null)
                                        cacheStream.Write(buffer, 0, bytesRead);
                                    outStream.Write(buffer, 0, bytesRead);
                                    //Console.WriteLine(bytesRead);
                                    if (Server.DumpResponseData)
                                        Console.Write(UTF8Encoding.UTF8.GetString(buffer, 0, bytesRead));
                                }
                            }

                            if (Server.DumpResponseData)
                            {
                                Console.WriteLine();
                                Console.WriteLine();
                            }

                            responseStream.Close();
                            if (cacheStream != null)
                            {
                                cacheStream.Flush();
                                cacheStream.Close();
                            }

                            outStream.Flush();
                            if (canCache)
                                ProxyCache.AddData(entry);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                        finally
                        {
                            responseStream.Close();
                            response.Close();
                            myResponseWriter.Close();
                        }
                    }
                }
                else
                {
                    //serve from cache
                    StreamWriter myResponseWriter = new StreamWriter(outStream);
                    try
                    {
                        WriteResponseStatus(cacheEntry.StatusCode, cacheEntry.StatusDescription, myResponseWriter);
                        WriteResponseHeaders(myResponseWriter, cacheEntry.Headers);
                        if (cacheEntry.ResponseBytes != null)
                        {
                            outStream.Write(cacheEntry.ResponseBytes, 0, cacheEntry.ResponseBytes.Length);
                            if (ProxyServer.Server.DumpResponseData)
                                Console.Write(UTF8Encoding.UTF8.GetString(cacheEntry.ResponseBytes));
                        }
                        myResponseWriter.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    finally
                    {
                        myResponseWriter.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                if (Server.DumpHeaders || Server.DumpPostData || Server.DumpResponseData)
                {
                    //release the lock
                    Monitor.Exit(_outputLockObj);
                }

                clientStreamReader.Close();
                clientStream.Close();
                if (sslStream != null)
                    sslStream.Close();
                outStream.Close();
                if (cacheStream != null)
                    cacheStream.Close();
            }

        }

        private static List<Tuple<String,String>> ProcessResponse(HttpWebResponse response, int tblength)
        {
            String value=null;
            String header=null;
            List<Tuple<String, String>> returnHeaders = new List<Tuple<String, String>>();
            foreach (String s in response.Headers.Keys)
            {
                if (s.ToLower() == "set-cookie")
                {
                    header = s;
                    value = response.Headers[s];
                }
                else if (tblength > 0 && s.ToLower() == "content-length")
                {
                    header = s;
                    value = tblength.ToString();
                }
                else
                {
                    returnHeaders.Add(new Tuple<String, String>(s, response.Headers[s]));
                }
            }
            
            if (!String.IsNullOrWhiteSpace(value))
            {
                response.Headers.Remove(header);
                String[] cookies = cookieSplitRegEx.Split(value);
                foreach (String cookie in cookies)
                    returnHeaders.Add(new Tuple<String, String>("Set-Cookie", cookie));

            }
            returnHeaders.Add(new Tuple<String, String>("X-Proxied-By", "matt-dot-net proxy"));
            return returnHeaders;
        }

        private static void WriteResponseStatus(HttpStatusCode code, String description, StreamWriter myResponseWriter)
        {
            String s = String.Format("HTTP/1.0 {0} {1}", (Int32)code, description);
            myResponseWriter.WriteLine(s);
            if(ProxyServer.Server.DumpHeaders)
                Console.WriteLine(s);
        }

        private static void WriteResponseHeaders(StreamWriter myResponseWriter, List<Tuple<String,String>> headers)
        {
            if (headers != null)
            {
                foreach (Tuple<String,String> header in headers)
                    myResponseWriter.WriteLine(String.Format("{0}: {1}", header.Item1,header.Item2));
            }
            myResponseWriter.WriteLine();
            myResponseWriter.Flush();

            if (Server.DumpHeaders)
                DumpHeaderCollectionToConsole(headers);
        }

        private static void DumpHeaderCollectionToConsole(WebHeaderCollection headers)
        {
            foreach (String s in headers.AllKeys)
                Console.WriteLine(String.Format("{0}: {1}", s,headers[s]));
            Console.WriteLine();
        }

        private static void DumpHeaderCollectionToConsole(List<Tuple<String,String>> headers)
        {
            foreach (Tuple<String,String> header in headers)
                Console.WriteLine(String.Format("{0}: {1}", header.Item1,header.Item2));
            Console.WriteLine();
        }

        private static int ReadRequestHeaders(StreamReader sr, HttpWebRequest webReq)
        {
            String httpCmd;
            int contentLen = 0;
            do
            {
                httpCmd = sr.ReadLine();
                if (String.IsNullOrEmpty(httpCmd))
                    return contentLen;
                String[] header = httpCmd.Split(colonSpaceSplit, 2, StringSplitOptions.None);
                switch (header[0].ToLower())
                {
                    case "host":
                        webReq.Host = header[1];
                        break;
                    case "user-agent":
                        webReq.UserAgent = header[1];
                        break;
                    case "accept":
                        webReq.Accept = header[1];
                        break; 
                    case "referer":
                        webReq.Referer = header[1];
                        break;
                    case "cookie":
                        webReq.Headers["Cookie"] = header[1];
                        break;
                    case "proxy-connection":
                    case "connection":
                    case "keep-alive":
                        //ignore these
                        break;
                    case "content-length":
                        int.TryParse(header[1], out contentLen);
                        break;
                    case "content-type":
                        webReq.ContentType = header[1];
                        break;
                    case "if-modified-since":
                        String[] sb = header[1].Trim().Split(semiSplit);
                        DateTime d;
                        if (DateTime.TryParse(sb[0], out d))
                            webReq.IfModifiedSince = d;
                        break;
                    default:
                        try
                        {
                            webReq.Headers.Add(header[0], header[1]);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(String.Format("Could not add header {0}.  Exception message:{1}", header[0], ex.Message));
                        }
                        break;
                }
            } while (!String.IsNullOrWhiteSpace(httpCmd));
            return contentLen;
        }


        public static string GetMD5Hash(string TextToHash)
        {
            if ((TextToHash == null) || (TextToHash.Length == 0))
            {
                return string.Empty;
            }

            //MD5 Hash aus dem String berechnen. Dazu muss der string in ein Byte[]
            //zerlegt werden. Danach muss das Resultat wieder zurück in ein string.
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] textToHash = Encoding.Default.GetBytes(TextToHash);
            byte[] result = md5.ComputeHash(textToHash);
            
            //return System.BitConverter.ToString(result);
            return Math.Abs(System.BitConverter.ToInt64(result, 0)).ToString();
        } 
    }
}
