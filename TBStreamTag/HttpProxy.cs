using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

namespace TBStreamTag
{
    class HttpProxy
    {
        // From http://stackoverflow.com/questions/11958350/troubleshooting-a-simple-proxy-server
        // Modified by Stefan Oechslein

        private bool stopFlag = false;
        private bool isTechnobase = false;

        public void Start(IPAddress ip, int port)
        {
            try
            {
                TcpListener listener = new TcpListener(ip, port);
                listener.Start(100);
                while (!stopFlag)
                {
                    Socket client = listener.AcceptSocket();
                    IPEndPoint rep = (IPEndPoint)client.RemoteEndPoint;
                    Thread th = new Thread(ThreadHandleClient);
                    th.Start(client);
                }

                listener.Stop();
            }
            catch (Exception ex)
            {
                Debug.Print("START: " + ex.Message);
            }
        }

        public void Stop()
        {
            stopFlag = true;
        }

        public void ThreadHandleClient(object o)
        {
            try
            {
                Socket client = (Socket)o;
                Debug.Print("lingerstate=" + client.LingerState.Enabled.ToString() + " timeout=" + client.LingerState.LingerTime.ToString());
                NetworkStream ns = new NetworkStream(client);
                //RECEIVE CLIENT DATA
                byte[] buffer = new byte[2048];
                int rec = 0, sent = 0, transferred = 0, rport = 0;
                string data = "";
                do
                {
                    rec = ns.Read(buffer, 0, buffer.Length);
                    data += Encoding.ASCII.GetString(buffer, 0, rec);
                } while (rec == buffer.Length);

                //PARSE DESTINATION AND SEND REQUEST
                string line = data.Replace("\r\n", "\n").Split(new string[] { "\n" }, StringSplitOptions.None)[0];
                Uri uri = new Uri(line.Split(new string[] { " " }, StringSplitOptions.None)[1]);
                Debug.Print("CLIENT REQUEST RECEIVED: " + uri.OriginalString);
                if (uri.Scheme == "https")
                {
                    rport = 443;
                    Debug.Print("HTTPS - 443");
                }
                else
                {
                    rport = 80;
                    Debug.Print("HTTP - 443");
                }

                string tbResponse = "";

                if (uri.OriginalString.Contains("ChannelID=884") && uri.OriginalString.Contains("Channel=TechnoBase"))
                {
                    isTechnobase = true;
                    Random rnd = new Random(DateTime.Now.Millisecond);
                    TBParser tbp = new TBParser();
                    List<Track> trackList = tbp.getTracks();

                    string currentTimeStr = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff");
                    StringBuilder sb = new StringBuilder();
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
                            sb.Append("49127-12110 ");
                            sb.Append(DateTime.Now.ToString("dd.MM.yyyy") + " "); // To be fixed (Incorrect date)
                            sb.Append(trk.TimeStart.ToString("HH:mm:ss.fff") + " "); // Time Start
                            sb.Append(DateTime.Now.ToString("dd.MM.yyyy") + " "); // To be fixed (Incorrect date)
                            sb.Append(trk.TimeEnd.ToString("HH:mm:ss.fff") + " "); // Time End
                            sb.Append("\"" + trk.Title + "\" "); // Title
                            sb.Append(rnd.Next(1, 10) + " "); // ?
                            sb.Append(rnd.Next(100000000, 999999999) + " "); // ?
                            sb.Append("\"" + trk.Artist + "\" "); // Artist
                            sb.Append("\"\" "); // Album
                            sb.Append("\"\" "); // URL
                            sb.Append("\"0\" "); // ?
                            sb.Append("\"2000\" "); // Year
                            sb.Append("\"\" "); // ?
                            sb.Append("\"\" "); // ?
                            sb.Append("\"\" "); // Shop URL
                            sb.Append(rnd.Next(1, 10) + " ");
                            sb.Append(rnd.Next(100000, 999999) + " ");
                            sb.Append(rnd.Next(1000, 9999) + " ");
                            sb.Append("0 ");
                            sb.Append("0 ");
                            sb.Append("\"" + trk.CoverURL + "\" "); // Cover Image URL
                            sb.Append("\"" + rnd.Next(100000000, 999999999) + "\""); //195048320234
                            sb.Append("<br>");
                            //49127-12110 01.11.2012 17:24:33.393 01.11.2012 17:29:34.307 "Everything" 5 331039793 "Safri Duo" "Episode II" "http://www.safriduo.dk/" "151" "2001" "" "http://www.amazon.de/Episode-II-Safri-Duo/dp/B00005KKA5/ref=sr_1_2?ie=UTF8&s=music&qid=1216825657&sr=8-2" "http://www.amazon.de/Episode-II-Safri-Duo/dp/B00005KKA5/ref=sr_1_2?ie=UTF8&s=music&qid=1216825657&sr=8-2" 4 1083354 5870,5 0 0 "http://ecx.images-amazon.com/images/I/41W4SZVXERL._SX240_.jpg" "112545908757223"
                        }
                    }

                    string contentStr = sb.ToString();
                    StringBuilder hsb = new StringBuilder();

                    hsb.AppendLine("HTTP/1.1 200 OK");
                    hsb.AppendLine("Cache-Control: private");
                    hsb.AppendLine("Content-Type: text/html; charset=Windows-1252");
                    hsb.AppendLine("Server: Microsoft-IIS/7.5");
                    hsb.AppendLine("X-AspNet-Version: 4.0.30319");
                    hsb.AppendLine("X-Powered-By: ASP.NET");
                    hsb.AppendLine("Date: Thu, 01 Nov 2012 20:11:43 GMT");
                    hsb.AppendLine("Content-Length: " + contentStr.Length);
                    hsb.AppendLine("");
                    hsb.AppendLine(contentStr);

                    tbResponse = hsb.ToString();
                    Debug.WriteLine(tbResponse);
                }


                IPHostEntry rh = Dns.GetHostEntry(uri.Host);
                Socket webserver = new Socket(rh.AddressList[0].AddressFamily, SocketType.Stream, ProtocolType.IP);
                webserver.Connect(new IPEndPoint(rh.AddressList[0], rport));
                byte[] databytes = Encoding.ASCII.GetBytes(data);
                webserver.Send(databytes, databytes.Length, SocketFlags.None);
                Debug.Print("SENT TO SERVER. WILL NOW RELAY: " + data);

                //START RELAY
                buffer = new byte[2048];
                bool firstTime = true;
                rec = 0;
                data = "";
                do
                {
                    transferred = 0;
                    if (isTechnobase)
                    {
                        sent = client.Send(StrToByteArray(tbResponse), SocketFlags.None);
                    }
                    else
                    {
                        do
                        {
                            if (webserver.Poll((firstTime ? 9000 : 2000) * 1000, SelectMode.SelectRead))
                            {
                                rec = webserver.Receive(buffer, buffer.Length, SocketFlags.None);
                                //Debug.Print("RECEIVED FROM WEBSERVER[" + rec.ToString() + "]: " + Encoding.ASCII.GetString(buffer, 0, rec));
                                firstTime = false;
                                sent = client.Send(buffer, rec, SocketFlags.None);
                                //Debug.Print("SENT TO CLIENT[" + sent.ToString() + "]: " + Encoding.ASCII.GetString(buffer, 0, rec));
                                transferred += rec;
                            }
                            else
                            {
                                Debug.Print("No data polled from webserver");
                            }
                        } while (rec == buffer.Length);

                        Debug.Print("loop-1 finished");
                    }
                    //if (transferred == 0)
                    //     break;

                    //transferred = 0;
                    //rec = 0;
                    //do
                    //{
                    //    if (client.Poll(1000 * 1000, SelectMode.SelectRead))
                    //    {
                    //        rec = client.Receive(buffer, buffer.Length, SocketFlags.None);
                    //        Debug.Print("RECEIVED FROM CLIENT: " + Encoding.ASCII.GetString(buffer, 0, rec));

                    //        sent = webserver.Send(buffer, rec, SocketFlags.None);
                    //        Debug.Print("SENT TO WEBSERVER[" + sent.ToString() + "]: " + Encoding.ASCII.GetString(buffer, 0, rec));
                    //        transferred += rec;
                    //    }
                    //    else
                    //    {
                    //        Debug.Print("No data polled from client");
                    //    }
                    //} while (rec == buffer.Length);
                    //Debug.Print("loop-2 finished");

                } while (transferred > 0);
                Debug.Print("LOOP ENDS. EXITING THREAD");
                client.Close();
                webserver.Close();
            }
            catch (Exception ex)
            {
                Debug.Print("Error occured: " + ex.Message);
            }
            finally
            {
                Debug.Print("Client thread closed");
            }
        }

        public static byte[] StrToByteArray(string str)
        {
            System.Text.UTF8Encoding encoding = new System.Text.UTF8Encoding();
            return encoding.GetBytes(str);
        }
    }
}


