﻿using jaindb;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Extensions;
using System.Text;

namespace DevCDRServer.Controllers
{
    [Route("jaindb")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class JainDBController : Controller
    {
        private readonly IHostingEnvironment _env;
        private IMemoryCache _cache;

        public JainDBController(IHostingEnvironment env, IMemoryCache memoryCache)
        {
            _env = env;
            _cache = memoryCache;
            jDB._cache = memoryCache;
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("")]
        [Route("About")]
        public ActionResult About()
        {
            ViewBag.appVersion = typeof(JainDBController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            ViewBag.Message = "JainDB running on Device Commander";
            return View("About");
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("upload/{param}")]
        public string Upload(string param, string blockType = "INV")
        {
            jDB.FilePath = Path.Combine(_env.WebRootPath, "JainDB");

            string sParams = "";
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                sParams = reader.ReadToEnd();

            //param = Request.Path.Substring(Request.Path.LastIndexOf('/') + 1);
            return jDB.UploadFull(sParams, param, blockType);
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("GetPS")]
        [Route("GetPS/{filename}")]
        public string GetPS(string filename = "")
        {
            string sResult = "";
            string spath = Path.Combine(_env.WebRootPath, "JainDB");
            jaindb.jDB.FilePath = spath;
            if (string.IsNullOrEmpty(filename))
            {
                Request.ToString();
                string sLocalURL = Request.GetEncodedUrl().Replace("/getps", "");
                if (System.IO.File.Exists(spath + "/inventory.ps1"))
                {
                    
                    string sFile = System.IO.File.ReadAllText(spath + "/inventory.ps1");
                    sResult = sFile.Replace("%LocalURL%", sLocalURL).Replace(":%WebPort%", "");

                    return sResult;
                }
            }
            else
            {
                string sLocalURL = Request.GetDisplayUrl().Substring(0, Request.GetDisplayUrl().IndexOf("/getps"));
                if (System.IO.File.Exists(spath + "/" + filename))
                {
                    string sFile = System.IO.File.ReadAllText(spath + "/" + filename);
                    sResult = sFile.Replace("%LocalURL%", sLocalURL).Replace(":%WebPort%", "");

                    return sResult;
                }
            }

            return sResult;

        }

        [HttpGet]
        [BasicAuthenticationAttribute()]
        [Route("full")]
        public JObject Full(string blockType = "INV")
        {
            //string sPath = this.Request.Path;
            string spath = Path.Combine(_env.WebRootPath, "JainDB");
            jDB.FilePath = spath;

            string sQuery = this.Request.QueryString.ToString();

            var query = System.Web.HttpUtility.ParseQueryString(sQuery);
            string sKey = query["id"];

            if (string.IsNullOrEmpty(sKey))
                sKey = jDB.LookupID(query.Keys[0], query.GetValues(0)[0]);
            //int index = -1;
            if (!int.TryParse(query["index"], out int index))
                index = -1;
            if (!string.IsNullOrEmpty(sKey))
                return jDB.GetFull(sKey, index, blockType);
            else
                return null;
        }

        [HttpGet]
        [BasicAuthenticationAttribute()]
        [Route("query")]
        public async System.Threading.Tasks.Task<JArray> Query()
        {
            DateTime dStart = DateTime.Now;

            string sPath = Path.Combine(_env.WebRootPath, "JainDB");
            jDB.FilePath = sPath;

            string sQuery = this.Request.QueryString.ToString();
            if (sPath != "/favicon.ico")
            {
                //string sUri = Microsoft.AspNetCore.Http.Extensions.UriHelper.GetDisplayUrl(Request);
                var query = System.Web.HttpUtility.ParseQueryString(sQuery);
                string qpath = (query[null] ?? "").Replace(',',';');
                string qsel = (query["$select"] ?? "").Replace(',', ';');
                string qexc = (query["$exclude"] ?? "").Replace(',', ';');
                string qwhe = (query["$where"] ?? "").Replace(',', ';');
                return await jDB.QueryAsync(qpath, qsel, qexc, qwhe);
            }
            return null;
        }

        public int totalDeviceCount(string sPath = "")
        {
            int iCount = 0;
            try
            {
                //Check in MemoryCache
                if (_cache.TryGetValue("totalDeviceCount", out iCount))
                {
                    return iCount;
                }

                if (string.IsNullOrEmpty(sPath))
                    sPath = Path.Combine(_env.WebRootPath, "JainDB\\_Chain");

                if (Directory.Exists(sPath))
                    iCount = Directory.GetFiles(sPath).Count(); //count Blockchain Files

                var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromSeconds(60)); //cache ID for 60s
                _cache.Set("totalDeviceCount", iCount, cacheEntryOptions);
            }
            catch { }

            return iCount;
        }

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        [Route("inv")]
        [HttpGet]
        public ActionResult Inv(string id, string name = "", int index = -1, string blockType = "INV")
        {
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            string spath = Path.Combine(_env.WebRootPath, "JainDB");
            jaindb.jDB.FilePath = spath;

            if (!string.IsNullOrEmpty(name))
                id = jDB.LookupID("name", name);

            if (string.IsNullOrEmpty(id))
                return Redirect("../DevCdr/Dashboard");

            var oInv = jDB.GetFull(id, index, blockType);

            if (oInv != new JObject())
            {
                ViewBag.Id = id;

                try
                {
                    string sInstance = "Default"; // oInv["DevCDRInstance"].ToString();
                    try
                    {
                        TimeSpan tDiff = DateTime.Now.ToUniversalTime() - (DateTime)oInv["_date"];
                        if (tDiff.TotalDays >= 2)
                            ViewBag.LastInv = ((int)tDiff.TotalDays).ToString() + " days"; 
                        else
                        {
                            if((tDiff.TotalHours >= 1))
                                ViewBag.LastInv = ((int)tDiff.TotalHours).ToString() + " hours";
                            else
                                ViewBag.LastInv = ((int)tDiff.TotalMinutes).ToString() + " minutes";
                        }
                    }
                    catch
                    {
                        ViewBag.LastInv = oInv["_date"];
                    }

                    ViewBag.Id = id;
                    ViewBag.Index = oInv["_index"];
                    ViewBag.idx = index;
                    ViewBag.Type = oInv["_type"] ?? "INV";
                    ViewBag.OS = oInv["OS"]["Caption"];
                    ViewBag.Name = oInv["Computer"]["#Name"];
                    ViewBag.Title = oInv["Computer"]["#Name"];
                    ViewBag.UserName = oInv["Computer"]["@UserName"] ?? "";
                    ViewBag.Vendor = oInv["Computer"]["Manufacturer"] ?? oInv["BIOS"]["Manufacturer"];
                    ViewBag.Serial = oInv["Computer"]["#SerialNumber"] ?? oInv["BIOS"]["#SerialNumber"] ?? "unknown";
                    ViewBag.Version = oInv["OS"]["Version"];
                    ViewBag.InstDate = oInv["OS"]["#InstallDate"];
                    ViewBag.LastBoot = oInv["OS"]["@LastBootUpTime"];
                    ViewBag.Model = oInv["Computer"]["Model"] ?? "unknown";
                    ViewBag.Language = oInv["OS"]["OSLanguage"].ToString();
                    ViewBag.Arch = oInv["OS"]["OSArchitecture"];
                    ViewBag.CPU = oInv["Processor"]["Name"] ?? oInv["Processor"][0]["Name"];
                    ViewBag.Memory = Convert.ToInt32((long)oInv["Computer"]["TotalPhysicalMemory"] / 1000 / 1000 / 1000);
                    switch (ViewBag.Memory)
                    {
                        case 17:
                            ViewBag.Memory = 16;
                            break;
                        case 34:
                            ViewBag.Memory = 32;
                            break;
                        case 35:
                            ViewBag.Memory = 32;
                            break;
                        case 68:
                            ViewBag.Memory = 64;
                            break;
                        case 69:
                            ViewBag.Memory = 64;
                            break;
                    }
                    int chassis = int.Parse(oInv["Computer"]["ChassisTypes"][0].ToString() ?? "2");

                    var oSW = oInv["Software"];
                    var oIndSW = oInv.DeepClone()["Software"];

                    List<string> lCoreApps = new List<string>();
                    List<string> lIndSW = new List<string>();
                    List<string> lUnknSW = new List<string>();

                    //Check if device has all required SW installed
#region CoreApplications 
                    ViewBag.CoreStyle = "alert-danger";
                    try
                    {
                        var aCoreApps = System.IO.File.ReadAllLines(Path.Combine(spath, "CoreApps_" + sInstance + ".txt"));
                        ViewBag.CoreStyle = "alert-success";
                        foreach (string sCoreApp in aCoreApps)
                        {
                            try
                            {
                                var oRes = oInv.SelectTokens(sCoreApp);
                                if (oRes.Count() > 0)
                                {
                                    lCoreApps.Add("<p class=\"col-xs-offset-1 alert-success\">" + oRes.First()["DisplayName"].ToString() + "</p>");

                                    foreach(var oItem in oRes.ToList())
                                    {
                                        oItem.Remove();
                                    }
                                }
                                else
                                {
                                    ViewBag.CoreStyle = "alert-warning";
                                    lCoreApps.Add("<p class=\"col-xs-offset-1 alert-warning\">" + sCoreApp + "</p>");
                                }
                            }
                            catch
                            {
                                ViewBag.CoreStyle = "alert-warning";
                                lCoreApps.Add("<p class=\"col-xs-offset-1 alert-warning\">" + sCoreApp + "</p>");
                            }
                        }
                    }
                    catch { }
#endregion

#region IndividualSW 
                    try
                    {
                        var aIndApps = System.IO.File.ReadAllLines(Path.Combine(spath, "IndSW_" + sInstance + ".txt")).ToList();
                        foreach(var xSW in oInv["Software"])
                        {
                            if(aIndApps.Contains(xSW["DisplayName"].ToString().TrimEnd()))
                            {
                                lIndSW.Add("<p class=\"col-xs-offset-1 alert-info\">" + xSW["DisplayName"].ToString().TrimEnd() + " ; " + (xSW["DisplayVersion"] ?? "").ToString().Trim() + "</p>");
                            }
                            else
                            {
                                lUnknSW.Add("<p class=\"col-xs-offset-1 alert-danger\">" + xSW["DisplayName"].ToString().TrimEnd() + " ; " + (xSW["DisplayVersion"] ?? "").ToString().Trim() + "</p>");
                            }
                        }
                    }
                    catch { }


#endregion

                    ViewBag.CoreSW = lCoreApps.Distinct().OrderBy(t => t);
                    ViewBag.IndSW = lIndSW.Distinct().OrderBy(t=>t);
                    ViewBag.UnknSW = lUnknSW.Distinct().OrderBy(t => t);
                    ViewBag.IndSWc = lIndSW.Distinct().OrderBy(t => t).Count();
                    ViewBag.UnknSWc = lUnknSW.Distinct().OrderBy(t => t).Count();

                    //https://docs.microsoft.com/en-us/previous-versions/tn-archive/ee156537(v=technet.10)
                    switch (chassis)
                    {
                        case 1:
                            ViewBag.Type = "Other";
                            break;
                        case 2:
                            ViewBag.Type = "Unknwon";
                            break;
                        case 3:
                            ViewBag.Type = "Dekstop";
                            break;
                        case 4:
                            ViewBag.Type = "Low Profile Desktop";
                            break;
                        case 6:
                            ViewBag.Type = "Mini Tower";
                            break;
                        case 7:
                            ViewBag.Type = "Tower";
                            break;
                        case 9:
                            ViewBag.Type = "Laptop";
                            break;
                        case 10:
                            ViewBag.Type = "Notebook";
                            break;
                        case 13:
                            ViewBag.Type = "All in One";
                            break;
                        case 14:
                            ViewBag.Type = "Sub Notebook";
                            break;
                        case 30:
                            ViewBag.Type = "Tablet";
                            break;
                        case 31:
                            ViewBag.Type = "Convertible";
                            break;
                        default:
                            ViewBag.Type = "Other";
                            break;
                    }

                    switch (ViewBag.Language)
                    {
                        case "1033":
                            ViewBag.Language = "English - United States";
                            break;
                        case "2057":
                            ViewBag.Language = "English - Great Britain";
                            break;
                        case "1031":
                            ViewBag.Language = "German";
                            break;
                        case "1036":
                            ViewBag.Language = "French";
                            break;
                        case "1040":
                            ViewBag.Language = "Italian";
                            break;
                        case "1034":
                            ViewBag.Language = "Spanish";
                            break;
                    }

                    if (ViewBag.Model == "Virtual Machine")
                        ViewBag.Type = "VM";

                    if (((string)ViewBag.Vendor).ToLower() == "lenovo")
                        ViewBag.Model = oInv["Computer"]["SystemFamily"] ?? "unknown";
                }
                catch { }

                return View();
            }

            return Redirect("../DevCdr/Dashboard");
        }

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        [Route("diff")]
        [HttpGet]
        public ActionResult Diff(string id, int l =-1, int r = -1)
        {
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            string spath = Path.Combine(_env.WebRootPath, "JainDB");
            jaindb.jDB.FilePath = spath;

            if (!string.IsNullOrEmpty(id))
            {

                var oL = jDB.GetFull(id, l);
                if (r == -1)
                {
                    try
                    {
                        r = (int)oL["_index"] - 1;
                    }
                    catch { }
                }
                var oR = jDB.GetFull(id, r);

                //remove all @ attributes
                foreach (var oKey in oL.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("@")).ToList())
                {
                    try
                    {
                        oKey.Remove();
                    }
                    catch (Exception ex)
                    {
                    }
                }

                //remove all @ attributes
                foreach (var oKey in oR.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("@")).ToList())
                {
                    try
                    {
                        oKey.Remove();
                    }
                    catch (Exception ex)
                    {
                    }
                }

                ViewBag.jsonR = oR.ToString(Formatting.Indented);
                ViewBag.jsonL = oL.ToString(Formatting.Indented);
                ViewBag.History = GetHistory(id).ToString(Formatting.Indented);
                ViewBag.Id = id;
            }
            return View("Diff");
        }

#if DEBUG
        [AllowAnonymous]
#endif
        [Authorize]
        [Route("invjson")]
        [HttpGet]
        public ActionResult InvJson(string id, int l = -1, string blockType = "INV")
        {
            ViewBag.appVersion = typeof(DevCDRController).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            string spath = Path.Combine(_env.WebRootPath, "JainDB");
            jaindb.jDB.FilePath = spath;

            if (!string.IsNullOrEmpty(id))
            {

                var oL = jDB.GetFull(id, l, blockType);

                ViewBag.jsonL = oL.ToString(Formatting.None);
            }
            return View("InvJson");
        }

        [Authorize]
        [HttpGet]
        [Route("GetHistory/{id}")]
        public JArray GetHistory(string id, string blockType = "INV")
        {
            string spath = Path.Combine(_env.WebRootPath, "JainDB");
            jaindb.jDB.FilePath = spath;

            string sQuery = this.Request.QueryString.ToString();

            var query = System.Web.HttpUtility.ParseQueryString(sQuery);
            string sKey = query["id"];

            if (!string.IsNullOrEmpty(sKey))
                return jDB.GetJHistory(sKey, blockType);
            else
                return null;
        }
    }


    public class BasicAuthenticationAttribute : ActionFilterAttribute
    {
        public string BasicRealm { get; set; }
        protected string Username { get; set; }
        protected string Password { get; set; }

        public BasicAuthenticationAttribute()
        {
            this.Username = Environment.GetEnvironmentVariable("REPORTUSER") ?? "DEMO";
            this.Password = Environment.GetEnvironmentVariable("REPORTPASSWORD") ?? "password"; ;
        }

        public BasicAuthenticationAttribute(string username, string password)
        {
            this.Username = username;
            this.Password = password;
        }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
#if DEBUG
                return;
#endif
            var req = filterContext.HttpContext.Request;
            string auth = req.Headers["Authorization"];
            if (!String.IsNullOrEmpty(auth))
            {
                var cred = System.Text.ASCIIEncoding.ASCII.GetString(Convert.FromBase64String(auth.Substring(6))).Split(':');
                var user = new { Name = cred[0], Pass = cred[1] };

                if (user.Name == Username && user.Pass == Password) return;
            }
            filterContext.HttpContext.Response.Headers.Add("WWW-Authenticate", String.Format("Basic realm=\"{0}\"", BasicRealm ?? "devcdr"));
            /// thanks to eismanpat for this line: http://www.ryadel.com/en/http-basic-authentication-asp-net-mvc-using-custom-actionfilter/#comment-2507605761
            filterContext.Result = new UnauthorizedResult();
        }
    }
}