﻿using Microsoft.AspNet.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace DevCDRServer.Controllers
{

    [System.Web.Mvc.Authorize]

    public class DevCDRController : Controller
    {
        [AllowAnonymous]
        public ActionResult Demo()
        {
            ViewBag.Title = "Demo Environment";
            ViewBag.Instance = "Default";
            ViewBag.Route = "/Chat";
            return View();
        }

        [System.Web.Mvc.Authorize]
        public ActionResult Default()
        {
            ViewBag.Title = "Default Environment";
            ViewBag.Instance = "Default";
            ViewBag.Route = "/Chat";
            return View();
        }

        //[AllowAnonymous]
        [System.Web.Mvc.Authorize]
        public ActionResult XLab()
        {
            ViewBag.Title = "itnetX - Lab";
            ViewBag.Instance = "xLab";
            ViewBag.Route = "/Chat";
            return View();
        }

        //[AllowAnonymous]
        [System.Web.Mvc.Authorize(Users = "live.com#roger@zander.ch,roger@zander.ch,roger.zander@itnetx.ch")]
        public ActionResult Zander()
        {
            ViewBag.Title = "Zander Devices";
            ViewBag.Instance = "Zander";
            ViewBag.Route = "/Chat";
            return View();
        }

        [AllowAnonymous]
        public ActionResult About()
        {
            ViewBag.Message = "Device Commander details...";

            return View();
        }

        [AllowAnonymous]
        public ActionResult Contact()
        {
            ViewBag.Message = "Device Commander Contact....";

            return View();
        }

        [AllowAnonymous]
        public ActionResult GetData(string Instance)
        {
            JArray jData = new JArray();
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("jData", BindingFlags.Public | BindingFlags.Static);

                jData = ((FieldInfo)memberInfos[0]).GetValue(new JArray()) as JArray;
            }
            catch { }

            JObject oObj = new JObject
            {
                { "data", jData }
            };

            return new ContentResult
            {
                Content = oObj.ToString(Newtonsoft.Json.Formatting.None),
                ContentType = "application/json",
                ContentEncoding = Encoding.UTF8
            };
        }

        [AllowAnonymous]
        public ActionResult Groups(string Instance)
        {
            List<string> lGroups = new List<string>();
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("lGroups", BindingFlags.Public | BindingFlags.Static);

                lGroups = ((FieldInfo)memberInfos[0]).GetValue(new List<string>()) as List<string>;
            }
            catch { }

            lGroups.Remove("web");
            lGroups.Remove("Devices");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(lGroups, Formatting.None),
                ContentType = "application/json",
                ContentEncoding = Encoding.UTF8
            };
        }

        internal ActionResult SetResult(string Instance, string Hostname, string Result)
        {
            JArray jData = new JArray();
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("jData", BindingFlags.Public | BindingFlags.Static);

                jData = ((FieldInfo)memberInfos[0]).GetValue(new JArray()) as JArray;

                var tok = jData.SelectToken("[?(@.Hostname == '" + Hostname + "')].ScriptResult");
                tok = Result;
                jData.SelectToken("[?(@.Hostname == '" + Hostname + "')].ScriptResult").Replace(tok);

                ((FieldInfo)memberInfos[0]).SetValue(new JArray(), jData);
            }
            catch { }


            return new ContentResult();
        }

        internal string GetID(string Instance, string Host)
        {
            string sID = "";
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MethodInfo methodInfo = xType.GetMethod(
                                            "GetID",
                                            BindingFlags.Public | BindingFlags.Static
                                        );
                sID = methodInfo.Invoke(new object(), new object[] { Host }) as string;
            }
            catch { }

            return sID;
        }

        internal void Reload(string Instance)
        {
            string sID = "";
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MethodInfo methodInfo = xType.GetMethod(
                                            "Reload",
                                            BindingFlags.Public | BindingFlags.Static
                                        );
                sID = methodInfo.Invoke(new object(), new object[] { Instance }) as string;
            }
            catch { }
        }

        [System.Web.Mvc.Authorize]
        [HttpPost]
        public object Command()
        {
            string sParams = "";
            using (StreamReader reader = new StreamReader(Request.InputStream, Encoding.UTF8))
                sParams = reader.ReadToEnd();
            JObject oParams = JObject.Parse(sParams);

            string sCommand = oParams.SelectToken(@"$.command").Value<string>(); //get command name
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sArgs = oParams.SelectToken(@"$.args").Value<string>(); //get parameters

            if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                return new ContentResult();

            List<string> lHostnames = new List<string>();
            foreach (var oRow in oParams["rows"])
            {
                try
                {
                    lHostnames.Add(oRow.Value<string>("Hostname"));
                }
                catch { }
            }

            switch(sCommand)
            {
                case "AgentVersion":
                    RunCommand(lHostnames, "[System.Diagnostics.FileVersionInfo]::GetVersionInfo(\"C:\\Program Files\\xMgmt\\xMgmt.exe\").FileVersion", sInstance, sCommand);
                    break;
                case "Inv":
                    RunCommand(lHostnames, "Invoke-RestMethod -Uri 'https://jaindb.azurewebsites.net/getps' | IEX;'Inventory complete..'", sInstance, sCommand);
                    break;
                case "Restart":
                    RunCommand(lHostnames, "restart-computer -force", sInstance, sCommand);
                    break;
                case "Shutdown":
                    RunCommand(lHostnames, "stop-computer -force", sInstance, sCommand);
                    break;
                case "Logoff":
                    RunCommand(lHostnames, "(gwmi win32_operatingsystem).Win32Shutdown(4);'Logoff enforced..'", sInstance, sCommand);
                    break;
                case "Init":
                    Reload(sInstance);
                    break;
                case "GetRZUpdates":
                    RunCommand(lHostnames, "(Find-Package -ProviderName RuckZuck -Updates).Name | convertto-json", sInstance, sCommand);
                    break;
                case "GetGroups":
                    GetGroups(lHostnames, sInstance);
                    break;
                case "SetGroups":
                    SetGroups(lHostnames, sInstance, sArgs);
                    break;
                case "GetUpdates":
                    RunCommand(lHostnames, "(Get-WUList -MicrosoftUpdate) | select Title | ConvertTo-Json", sInstance, sCommand);
                    break;
                case "InstallUpdates":
                    RunCommand(lHostnames, "Install-WindowsUpdate -MicrosoftUpdate -IgnoreReboot -AcceptAll -Install;installing Updates...", sInstance, sCommand);
                    break;
            }

            return new ContentResult();
        }

        internal void RunCommand(List<string> Hostnames, string sCommand, string sInstance, string CmdName)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + CmdName); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", sCommand); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).returnPS(sCommand, "Host");
                }
            }
        }

        internal void GetGroups(List<string> Hostnames, string sInstance)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "get Groups"); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "get Groups"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).getgroups("Host");
                }
            }
        }

        internal void SetGroups(List<string> Hostnames, string sInstance, string Args)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "set Groups"); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "set Groups"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).setgroups(Args);
                }
            }
        }

        [System.Web.Mvc.Authorize]
        [HttpPost]
        public object RunPS()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.InputStream, Encoding.UTF8))
                sParams = reader.ReadToEnd();

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;
            
            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sCommand = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.psscript").Value<string>()); //get command
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sTitle = oParams.SelectToken(@"$.title").Value<string>(); //get title

            if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                return new ContentResult();

            List<string> lHostnames = new List<string>();
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (var oRow in oParams["rows"])
            {
                string sHost = oRow.Value<string>("Hostname");
                SetResult(sInstance, sHost, "triggered:" + sTitle); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", sCommand); //Enforce PageUpdate

            foreach (var oRow in oParams["rows"])
            {
                try
                {
                    //Get Hostname from Row
                    string sHost = oRow.Value<string>("Hostname");

                    if (string.IsNullOrEmpty(sHost))
                        continue;

                    //Get ConnectionID from HostName
                    string sID = GetID(sInstance, sHost);

                    if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                    {
                        
                        hubContext.Clients.Client(sID).returnPS(sCommand, "Host");
                    }
                }
                catch { }
            }

            return new ContentResult();
        }

    }
}