﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using System.Windows;
using Newtonsoft.Json;
using System.Diagnostics;

namespace WinUI
{
    

    public class APIHandler
    {
        private string authtoken;

        private string url = null;

        private static volatile APIHandler instance;
        private static object syncRoot = new Object();

        public delegate void NetworkListCallback(List<ZeroTierNetwork> networks);
        public delegate void StatusCallback(ZeroTierStatus status);


        private NetworkListCallback _networkCallbacks;
        private StatusCallback _statusCallbacks;

        public static APIHandler Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (syncRoot)
                    {
                        if (instance == null)
                        {
                            if (!initHandler())
                            {
                                return null;
                            }
                        }
                    }
                }

                return instance;
            }
        }

        private static bool initHandler()
        {
            String localZtDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\ZeroTier\\One";
            String globalZtDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\ZeroTier\\One";

            String authToken = "";
            Int32 port = 9993;

            if (!File.Exists(localZtDir + "\\authtoken.secret") || !File.Exists(localZtDir + "\\zerotier-one.port"))
            {
                // launch external process to copy file into place
                String curPath = System.Reflection.Assembly.GetEntryAssembly().Location;
                int index = curPath.LastIndexOf("\\");
                curPath = curPath.Substring(0, index);
                ProcessStartInfo startInfo = new ProcessStartInfo(curPath + "\\copyutil.exe", globalZtDir + " " + localZtDir);
                startInfo.Verb = "runas";


                var process = Process.Start(startInfo);
                process.WaitForExit();
            }

            authToken = readAuthToken(localZtDir + "\\authtoken.secret");

            if ((authToken == null) || (authToken.Length <= 0))
            {
                MessageBox.Show("Unable to read ZeroTier One authtoken", "ZeroTier One");
                return false;
            }

            port = readPort(localZtDir + "\\zerotier-one.port");
            instance = new APIHandler(port, authToken);
            return true;
        }

        private static String readAuthToken(String path)
        {
            String authToken = "";

            if (File.Exists(path))
            {
                try
                {
                    byte[] tmp = File.ReadAllBytes(path);
                    authToken = System.Text.Encoding.UTF8.GetString(tmp).Trim();
                }
                catch
                {
                    MessageBox.Show("Unable to read ZeroTier One Auth Token from:\r\n" + path, "ZeroTier One");
                }
            }

            return authToken;
        }

        private static Int32 readPort(String path)
        {
            Int32 port = 9993;

            try
            {
                byte[] tmp = File.ReadAllBytes(path);
                port = Int32.Parse(System.Text.Encoding.ASCII.GetString(tmp).Trim());
                if ((port <= 0) || (port > 65535))
                    port = 9993;
            }
            catch
            {
            }

            return port;
        }

        private APIHandler()
        {
            url = "http://127.0.0.1:9993";
        }

        public APIHandler(int port, string authtoken)
        {
            url = "http://localhost:" + port;
            this.authtoken = authtoken;
        }

        

        public void GetStatus(StatusCallback cb)
        {
            var request = WebRequest.Create(url + "/status" + "?auth=" + authtoken) as HttpWebRequest;
            if (request != null)
            {
                request.Method = "GET";
                request.ContentType = "application/json";
            }

            try
            {
                var httpResponse = (HttpWebResponse)request.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var responseText = streamReader.ReadToEnd();

                    ZeroTierStatus status = null;
                    try
                    {
                        status = JsonConvert.DeserializeObject<ZeroTierStatus>(responseText);
                    }
                    catch (JsonReaderException e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                    cb(status);
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                cb(null);
            }
            catch (System.Net.WebException)
            {
                cb(null);
            }
        }

        

        public void GetNetworks(NetworkListCallback cb)
        {
            var request = WebRequest.Create(url + "/network" + "?auth=" + authtoken) as HttpWebRequest;
            if (request == null)
            {
                cb(null);
            }

            request.Method = "GET";
            request.ContentType = "application/json";

            try
            {
                var httpResponse = (HttpWebResponse)request.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var responseText = streamReader.ReadToEnd();

                    List<ZeroTierNetwork> networkList = null;
                    try
                    {
                        networkList = JsonConvert.DeserializeObject<List<ZeroTierNetwork>>(responseText);
                        foreach (ZeroTierNetwork n in networkList)
                        {
                            // all networks received via JSON are connected by definition
                            n.IsConnected = true;
                        }
                    }
                    catch (JsonReaderException e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                    cb(networkList);
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                cb(null);
            }
            catch (System.Net.WebException)
            {
                cb(null);
            }
        }

        public void JoinNetwork(string nwid, bool allowManaged = true, bool allowGlobal = false, bool allowDefault = false)
        {
            var request = WebRequest.Create(url + "/network/" + nwid + "?auth=" + authtoken) as HttpWebRequest;
            if (request == null)
            {
                return;
            }

            request.Method = "POST";
            request.ContentType = "applicaiton/json";

            using (var streamWriter = new StreamWriter(((HttpWebRequest)request).GetRequestStream()))
            {
                string json = "{\"allowManaged\":" + (allowManaged ? "true" : "false") + "," +
                    "\"allowGlobal\":" + (allowGlobal ? "true" : "false") + "," +
                    "\"allowDefault\":" + (allowDefault ? "true" : "false") + "}";
                streamWriter.Write(json);
                streamWriter.Flush();
                streamWriter.Close();
            }

            try
            {
                var httpResponse = (HttpWebResponse)request.GetResponse();

                if (httpResponse.StatusCode != HttpStatusCode.OK)
                {
                    Console.WriteLine("Error sending join network message");
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                MessageBox.Show("Error Joining Network: Cannot connect to ZeroTier service.");
            }
            catch (System.Net.WebException)
            {
                MessageBox.Show("Error Joining Network: Cannot connect to ZeroTier service.");
            }
        }

        public void LeaveNetwork(string nwid)
        {
            var request = WebRequest.Create(url + "/network/" + nwid + "?auth=" + authtoken) as HttpWebRequest;
            if (request == null)
            {
                return;
            }

            request.Method = "DELETE";

            try
            {
                var httpResponse = (HttpWebResponse)request.GetResponse();

                if (httpResponse.StatusCode != HttpStatusCode.OK)
                {
                    Console.WriteLine("Error sending leave network message");
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                MessageBox.Show("Error Leaving Network: Cannot connect to ZeroTier service.");
            }
            catch (System.Net.WebException)
            {
                MessageBox.Show("Error Leaving Network: Cannot connect to ZeroTier service.");
            }
        }

        public delegate void PeersCallback(List<ZeroTierPeer> peers);

        public void GetPeers(PeersCallback cb)
        {
            var request = WebRequest.Create(url + "/peer" + "?auth=" + authtoken) as HttpWebRequest;
            if (request == null)
            {
                cb(null);
            }

            request.Method = "GET";
            request.ContentType = "application/json";

            try
            {
                var httpResponse = (HttpWebResponse)request.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var responseText = streamReader.ReadToEnd();
                    //Console.WriteLine(responseText);
                    List<ZeroTierPeer> peerList = null;
                    try
                    {
                        peerList = JsonConvert.DeserializeObject<List<ZeroTierPeer>>(responseText);
                    }
                    catch (JsonReaderException e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                    cb(peerList);
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                cb(null);
            }
            catch (System.Net.WebException)
            {
                cb(null);
            }
        }
    }
}
