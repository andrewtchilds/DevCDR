﻿// ************************************************************************************
//          jaindb (c) Copyright 2018 by Roger Zander
// ************************************************************************************


using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.Caching;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using static jaindb.BlockChain;

namespace jaindb
{
    public static class jDB
    {
        public enum hashType { MD5, SHA2_256 } //Implemented Hash types
        private static readonly object locker = new object();
        private static HttpClient oClient = new HttpClient();

        public static bool UseFileStore = true;
        public static string FilePath = "";

        internal static ObjectCache _cache;

        public static hashType HashType = hashType.MD5;

        public static string BlockType = "INV";
        public static int PoWComplexitity = 0; //Proof of Work complexity; 0 = no PoW; 8 = 8 trailing bits of the block hash must be '0'
        public static bool ReadOnly = false;

        internal static List<string> RethinkTables = new List<string>();

        public static string CalculateHash(string input)
        {
            switch (HashType)
            {
                case hashType.MD5:
                    return Hash.CalculateMD5HashString(input);
                case hashType.SHA2_256:
                    return Hash.CalculateSHA2_256HashString(input);
                default:
                    return Hash.CalculateMD5HashString(input); ;
            }
        }

        /// <summary>
        /// Lookup Key ID's to search for Objects based on their Key
        /// </summary>
        /// <param name="name">Key name to search. E.g. "name"</param>
        /// <param name="value">Value to search. E.g. "computer01"</param>
        /// <returns>Hash ID of the Object</returns>
        public static string LookupID(string name, string value)
        {
            string sResult = "";
            try
            {
                if (_cache == null)
                    _cache = System.Runtime.Caching.MemoryCache.Default;
                //Check in MemoryCache
                sResult = _cache["ID-" + name.ToLower() + value.ToLower()] as string;
                if (sResult != null)
                {
                    return sResult;
                }
            }
            catch { }
            try
            {
                sResult = File.ReadAllText(Path.Combine(FilePath, "_Key", name.TrimStart('#', '@'), value + ".json"));

                //Cache result in Memory
                if (!string.IsNullOrEmpty(sResult))
                {
                    CacheItemPolicy policy = new CacheItemPolicy();
                    policy.AbsoluteExpiration =
                    DateTimeOffset.Now.AddSeconds(300.0); //cache for 300s

                    _cache.Set("ID-" + name.ToLower() + value.ToLower(), sResult, policy);
                }
                return sResult;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error LookupID_1: " + ex.Message.ToString());
            }

            return sResult;
        }

        public static void WriteHash(ref JToken oRoot, ref JObject oStatic, string Collection)
        {
            if (ReadOnly)
                return;
            try
            {
                //JSort(oStatic);
                string sHash = CalculateHash(oRoot.ToString(Newtonsoft.Json.Formatting.None));
                if (string.IsNullOrEmpty(sHash))
                    return;
                string sPath = oRoot.Path;

                var oClass = oStatic.SelectToken(sPath);// as JObject;

                if (oClass != null)
                {
                    if (oClass.Type == JTokenType.Object)
                    {
                        ((JObject)oClass).Add("##hash", sHash);

                        WriteHashAsync(sHash, oRoot.ToString(Formatting.None), Collection).ConfigureAwait(false);

                        oRoot = oClass;
                    }
                }

            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }
        }

        public static bool WriteHash(string Hash, string Data, string Collection)
        {
            if (ReadOnly)
                return false;

            try
            {
                //Cache result in Memory
                if (!string.IsNullOrEmpty(Data))
                {
                    CacheItemPolicy policy = new CacheItemPolicy();
                    policy.AbsoluteExpiration =
                    DateTimeOffset.Now.AddSeconds(60.0); 

                    _cache.Set("RH-" + Collection + "-" + Hash, Data, policy);
                }

                if (string.IsNullOrEmpty(Data) || Data == "null")
                    return true;

                if (UseFileStore)
                {
                    //Remove invalid Characters in Path and Hash
                    foreach (var sChar in Path.GetInvalidPathChars())
                    {
                        Collection = Collection.Replace(sChar.ToString(), "");
                        Hash = Hash.Replace(sChar.ToString(), "");
                    }

                    if (!Directory.Exists(Path.Combine(FilePath, Collection)))
                        Directory.CreateDirectory(Path.Combine(FilePath, Collection));

                    switch (Collection.ToLower())
                    {
                        case "_full":
                            //DB 0 = Full Inv
                            var jObj = JObject.Parse(Data);
                            JSort(jObj);

                            string sID = jObj["#id"].ToString();

                            if (!Directory.Exists(Path.Combine(FilePath , "Key")))
                                Directory.CreateDirectory(Path.Combine(FilePath ,"_Key"));

                            //Store KeyNames
                            foreach (JProperty oSub in jObj.Properties())
                            {
                                if (oSub.Name.StartsWith("#"))
                                {
                                    if (oSub.Value.Type == JTokenType.Array)
                                    {
                                        foreach (var oSubSub in oSub.Values())
                                        {
                                            try
                                            {
                                                if (oSubSub.ToString() != sID)
                                                {
                                                    string sDir = Path.Combine(FilePath , "_Key" , oSub.Name.ToLower().TrimStart('#'));

                                                    //Remove invalid Characters in Path
                                                    foreach (var sChar in Path.GetInvalidPathChars())
                                                    {
                                                        sDir = sDir.Replace(sChar.ToString(), "");
                                                    }

                                                    if (!Directory.Exists(sDir))
                                                        Directory.CreateDirectory(sDir);

                                                    File.WriteAllText(Path.Combine(sDir , oSubSub.ToString() + ".json"), sID);
                                                }
                                            }
                                            catch { }
                                        }

                                    }
                                    else
                                    {
                                        if (!string.IsNullOrEmpty((string)oSub.Value))
                                        {
                                            if (oSub.Value.ToString() != sID)
                                            {
                                                try
                                                {
                                                    string sDir = Path.Combine(FilePath , "_Key" , oSub.Name.ToLower().TrimStart('#'));

                                                    //Remove invalid Characters in Path
                                                    foreach (var sChar in Path.GetInvalidPathChars())
                                                    {
                                                        sDir = sDir.Replace(sChar.ToString(), "");
                                                    }

                                                    if (!Directory.Exists(sDir))
                                                        Directory.CreateDirectory(sDir);

                                                    File.WriteAllText(Path.Combine(sDir, (string)oSub.Value + ".json"), sID);
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                }
                            }

                            lock (locker) //only one write operation
                            {
                                File.WriteAllText(Path.Combine(FilePath , Collection , Hash + ".json"), Data);
                            }
                            break;

                        case "_chain":
                            lock (locker) //only one write operation
                            {
                                File.WriteAllText(Path.Combine(FilePath , Collection , Hash + ".json"), Data);
                            }
                            break;

                        default:
                            if (!File.Exists(Path.Combine(FilePath , Collection , Hash + ".json"))) //We do not have to create the same hash file twice...
                            {
                                lock (locker) //only one write operation
                                {
                                    File.WriteAllText(Path.Combine(FilePath, Collection, Hash + ".json"), Data);
                                }
                            }
                            break;
                    }

                    return true;
                }

            }
            catch (Exception ex)
            {
                if (!Directory.Exists(Path.Combine(FilePath, Collection)))
                    Directory.CreateDirectory(Path.Combine(FilePath, Collection));

                if (!File.Exists(Path.Combine(FilePath, Collection, Hash + ".json"))) //We do not have to create the same hash file twice...
                {
                    lock (locker) //only one write operation
                    {
                        File.WriteAllText(Path.Combine(FilePath, Collection, Hash + ".json"), Data);
                    }
                }

                return true;
            }

            return false;
        }

        public static async Task<bool> WriteHashAsync(string Hash, string Data, string Collection)
        {
            if (ReadOnly)
                return false;

            //return WriteHash(Hash, Data, Collection);
            //write async
            return await Task.Run(() =>
            {
                return WriteHash(Hash, Data, Collection);
            });

        }
        public static string ReadHash(string Hash, string Collection)
        {
            string sResult = "";
            try
            {
                //Check if MemoryCache is initialized
                if (_cache == null)
                {
                    _cache = System.Runtime.Caching.MemoryCache.Default;
                }

                sResult = _cache["RH-" + Collection + "-" + Hash] as string;
                //Try to get value from Memory
                if (sResult != null)
                {
                    return sResult;
                }
                else
                {
                    if (UseFileStore)
                    {
                        string Coll2 = Collection;
                        //Remove invalid Characters in Path anf File
                        foreach (var sChar in Path.GetInvalidPathChars())
                        {
                            Coll2 = Coll2.Replace(sChar.ToString(), "");
                            Hash = Hash.Replace(sChar.ToString(), "");
                        }

                        sResult = File.ReadAllText(Path.Combine(FilePath , Coll2 , Hash + ".json"));

#if DEBUG
                        //Check if hashes are valid...
                        if (Collection.ToLower() != "_full" && Collection.ToLower() != "_chain")
                        {
                            var jData = JObject.Parse(sResult);
                            /*if (jData["#id"] != null)
                                jData.Remove("#id");*/
                            if (jData["_date"] != null)
                                jData.Remove("_date");
                            if (jData["_index"] != null)
                                jData.Remove("_index");

                            string s1 = CalculateHash(jData.ToString(Formatting.None));
                            if (Hash != s1)
                            {
                                s1.ToString();
                                return "";
                            }
                        }
#endif


                        //Cache result in Memory
                        if (!string.IsNullOrEmpty(sResult))
                        {
                            CacheItemPolicy policy = new CacheItemPolicy();
                            policy.AbsoluteExpiration =
                            DateTimeOffset.Now.AddSeconds(60.0);
                            _cache.Set("RH-" + Collection + "-" + Hash, sResult, policy);
                        }

                        return sResult;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error ReadHash_1: " + ex.Message.ToString());
            }

            return sResult;
        }

        public static Blockchain GetChain(string DeviceID)
        {
            Blockchain oChain;
            string sData = ReadHash(DeviceID, "_Chain");
            if (string.IsNullOrEmpty(sData))
            {
                oChain = new Blockchain("", "root", 0);
            }
            else
            {
                JsonSerializerSettings oSettings = new JsonSerializerSettings();
                var oC = JsonConvert.DeserializeObject(sData, typeof(Blockchain), oSettings);
                oChain = oC as Blockchain;
            }

            return oChain;
        }

        public static string UploadFull(string JSON, string DeviceID, string blockType = "")
        {
            if (ReadOnly)
                return "";

            if (string.IsNullOrEmpty(blockType))
                blockType = BlockType;

            try
            {
                JObject oObj = JObject.Parse(JSON);

                //Remove NULL values
                foreach (var oTok in oObj.Descendants().Where(t => t.Parent.Type == (JTokenType.Property) && t.Type == JTokenType.Null).ToList())
                {
                    try
                    {
                        oTok.Parent.Remove();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error UploadFull_1: " + ex.Message.ToString());
                    }
                }

                //Remove empty values
                foreach (var oTok in oObj.Descendants().Where(t => t.Type == (JTokenType.Object) && !t.HasValues).ToList())
                {
                    try
                    {
                        if (oTok.Parent.Type == JTokenType.Property)
                        {
                            oTok.Parent.Remove();
                            continue;
                        }

                        if (oTok.Parent.Type == JTokenType.Array)
                        {
                            if (oTok.Parent.Count == 1) //Parent is array with one empty child
                            {
                                if (oTok.Parent.Parent.Type == JTokenType.Property)
                                    oTok.Parent.Parent.Remove(); //remove parent
                            }
                            else
                                oTok.Remove(); //remove empty array item
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error UploadFull_1: " + ex.Message.ToString());
                    }
                }


                JSort(oObj, true); //Enforce full sort

                JObject oStatic = oObj.ToObject<JObject>();
                JObject jTemp = oObj.ToObject<JObject>();

                //Load BlockChain
                Blockchain oChain = GetChain(DeviceID);

                JSort(oObj);
                JSort(oStatic);

                var jObj = oObj;


                //Loop through all ChildObjects
                foreach (var oChild in jObj.Descendants().Where(t => t.Type == JTokenType.Object).Reverse())
                {
                    try
                    {
                        JToken tRef = oObj.SelectToken(oChild.Path, false);

                        //check if tRfe is valid..
                        if (tRef == null)
                            continue;


                        string sName = "misc";
                        if (oChild.Parent.Type == JTokenType.Property)
                            sName = ((Newtonsoft.Json.Linq.JProperty)oChild.Parent).Name;
                        else
                            sName = ((Newtonsoft.Json.Linq.JProperty)oChild.Parent.Parent).Name; //it's an array

                        if (sName.StartsWith("@"))
                            continue;

                        foreach (JProperty jProp in oStatic.SelectToken(oChild.Path).Children().Where(t => t.Type == JTokenType.Property).ToList())
                        {
                            try
                            {
                                if (!jProp.Name.StartsWith("#"))
                                {
                                    if (jProp.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("#")).Count() == 0)
                                    {
                                        jProp.Remove();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine("Error UploadFull_2: " + ex.Message.ToString());
                            }
                        }


                        //remove all # and @ attributes
                        foreach (var oKey in tRef.Parent.Descendants().Where(t => t.Type == JTokenType.Property && (((JProperty)t).Name.StartsWith("#") || ((JProperty)t).Name.StartsWith("@"))).ToList())
                        {
                            try
                            {
                                oKey.Remove();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine("Error UploadFull_3: " + ex.Message.ToString());
                            }
                        }

                        WriteHash(ref tRef, ref oStatic, sName);
                        oObj.SelectToken(oChild.Path).Replace(tRef);

                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error UploadFull_4: " + ex.Message.ToString());
                    }
                }

                //remove all # and @ objects
                foreach (var oKey in oStatic.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("@")).ToList())
                {
                    try
                    {
                        oKey.Remove();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error UploadFull_5: " + ex.Message.ToString());
                    }
                }


                JSort(oStatic);
                //JSort(oStatic, true);

                string sResult = CalculateHash(oStatic.ToString(Newtonsoft.Json.Formatting.None));

                var oBlock = oChain.GetLastBlock(blockType);
                if (oBlock.data != sResult) //Compare hash
                {
                    var oNew = oChain.MineNewBlock(oBlock, blockType);
                    oChain.UseBlock(sResult, oNew);

                    if (oChain.ValidateChain())
                    {
                        //Console.WriteLine(JsonConvert.SerializeObject(tChain));
                        if (oNew.index == 1)
                        {
                            Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - new " + DeviceID);
                        }
                        else
                        {
                            Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - update " + DeviceID);
                        }


                        WriteHashAsync(DeviceID, JsonConvert.SerializeObject(oChain), "_Chain").ConfigureAwait(false);



                        //Add missing attributes
                        if (oStatic["_date"] == null)
                            oStatic.AddFirst(new JProperty("_date", new DateTime(oNew.timestamp).ToUniversalTime()));
                        if (oStatic["_index"] == null)
                            oStatic.AddFirst(new JProperty("_index", oNew.index));
                        if (oStatic["_type"] == null)
                            oStatic.AddFirst(new JProperty("_type", blockType));
                        if (oStatic["#id"] == null)
                            oStatic.AddFirst(new JProperty("#id", DeviceID));

                        if (jTemp["_index"] == null)
                            jTemp.AddFirst(new JProperty("_index", oNew.index));
                        if (jTemp["_hash"] == null)
                            jTemp.AddFirst(new JProperty("_hash", oNew.data));
                        if (jTemp["_date"] == null)
                            jTemp.AddFirst(new JProperty("_date", new DateTime(oNew.timestamp).ToUniversalTime()));
                        if (jTemp["_type"] == null)
                            jTemp.AddFirst(new JProperty("_type", blockType));
                        if (jTemp["#id"] == null)
                            jTemp.AddFirst(new JProperty("#id", DeviceID));

                        //JSort(jTemp);

                        //Only store Full data for default BlockType
                        if (blockType == BlockType)
                            WriteHashAsync(DeviceID, jTemp.ToString(Formatting.None), "_Full").ConfigureAwait(false);
                        else
                            WriteHashAsync(DeviceID + "_" + blockType, jTemp.ToString(Formatting.None), "_Full").ConfigureAwait(false);

                    }
                    else
                    {
                        Console.WriteLine("Blockchain is NOT valid... " + DeviceID);
                    }
                }
                else
                {
                    //Do not touch Blockchain, but store the Full JSON for reporting
                    if (jTemp["_index"] == null)
                        jTemp.AddFirst(new JProperty("_index", oBlock.index));
                    if (jTemp["_hash"] == null)
                        jTemp.AddFirst(new JProperty("_hash", oBlock.data));
                    if (jTemp["_date"] == null)
                        jTemp.AddFirst(new JProperty("_date", DateTime.UtcNow));
                    if (jTemp["_type"] == null)
                        jTemp.AddFirst(new JProperty("_type", blockType));
                    if (jTemp["#id"] == null)
                        jTemp.AddFirst(new JProperty("#id", DeviceID));

                    //JSort(jTemp);

                    //Only store Full data for default BlockType
                    if (blockType == BlockType)
                        WriteHashAsync(DeviceID, jTemp.ToString(Formatting.None), "_Full").ConfigureAwait(false);
                }


                if (blockType == BlockType)
                    WriteHashAsync(sResult, oStatic.ToString(Newtonsoft.Json.Formatting.None), "_Assets").ConfigureAwait(false);
                else
                    WriteHashAsync(sResult + "_" + blockType, oStatic.ToString(Newtonsoft.Json.Formatting.None), "_Assets").ConfigureAwait(false);


                return sResult;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error UploadFull_6: " + ex.Message.ToString());
            }

            return "";
        }

        public static JObject GetFull(string DeviceID, int Index = -1, string blockType = "")
        {
            try
            {
                JObject oInv = new JObject();

                if (string.IsNullOrEmpty(blockType))
                    blockType = BlockType;

                if (Index == -1)
                {
                    string sFull = "";
                    if (blockType == BlockType)
                        sFull = ReadHash(DeviceID, "_Full");
                    else
                        sFull = ReadHash(DeviceID + "_" + blockType, "_Full");

                    if (!string.IsNullOrEmpty(sFull))
                    {
                        return JObject.Parse(sFull);
                    }
                }

                JObject oRaw = GetRawId(DeviceID, Index, blockType);

                string sData = "";
                if (blockType == BlockType)
                    sData = ReadHash(oRaw["_hash"].ToString(), "_Assets");
                else
                    sData = ReadHash(oRaw["_hash"].ToString() + "_" + blockType, "_Assets");

                if (!string.IsNullOrEmpty(sData))
                {
                    oInv = JObject.Parse(sData);
                    try
                    {
                        if (oInv["_index"] == null)
                            oInv.Add(new JProperty("_index", oRaw["_index"]));
                        if (oInv["_date"] == null)
                            oInv.Add(new JProperty("_date", oRaw["_date"]));
                        if (oInv["_hash"] == null)
                            oInv.Add(new JProperty("_hash", oRaw["_hash"]));

                        //Set index and date from blockchain as the index and hash can be from a previous block
                        oInv["_index"] = oRaw["_index"];
                        oInv["_date"] = oRaw["_inventoryDate"];
                    }
                    catch { }

                    List<string> lHashes = new List<string>();

                    //Load hashed values
                    foreach (JProperty oTok in oInv.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("##hash")).ToList())
                    {
                        lHashes.Add(oTok.Path);
                    }

                    //Remove merge ##hash with hasehd value
                    foreach (string sHash in lHashes)
                    {
                        try
                        {
                            JProperty oTok = oInv.SelectToken(sHash).Parent as JProperty;
                            string sH = oTok.Value.ToString();

                            List<string> aPathItems = oTok.Path.Split('.').ToList();
                            aPathItems.Reverse();
                            string sRoot = "";
                            if (aPathItems.Count > 1)
                                sRoot = aPathItems[1].Split('[')[0];

                            string sObj = ReadHash(sH, sRoot);
                            if (!string.IsNullOrEmpty(sObj))
                            {
                                var jStatic = JObject.Parse(sObj);
                                oTok.Parent.Merge(jStatic);
                                bool bLoop = true;
                                int i = 0;
                                //Remove NULL values as a result from merge
                                while (bLoop)
                                {
                                    bLoop = false;
                                    foreach (var jObj in (oTok.Parent.Descendants().Where(t => (t.Type == (JTokenType.Object) || t.Type == (JTokenType.Array)) && t.HasValues == false).Reverse().ToList()))
                                    {
                                        try
                                        {
                                            if ((jObj.Type == JTokenType.Object || jObj.Type == JTokenType.Array) && jObj.Parent.Type == JTokenType.Property)
                                            {
                                                jObj.Parent.Remove();
                                                bLoop = true;
                                                continue;
                                            }

                                            jObj.Remove();
                                            bLoop = true;
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine("Error GetFull_1: " + ex.Message.ToString());
                                            if (i <= 100)
                                                bLoop = true;
                                            i++;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                sObj.ToString();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Error GetFull_2: " + ex.Message.ToString());
                        }
                    }

                    //Remove ##hash
                    foreach (var oTok in oInv.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("##hash")).ToList())
                    {
                        try
                        {
                            if (oInv.SelectToken(oTok.Path).Parent.Parent.Children().Count() == 1)
                                oInv.SelectToken(oTok.Path).Parent.Parent.Remove();
                            else
                                oInv.SelectToken(oTok.Path).Parent.Remove();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Error GetFull_3: " + ex.Message.ToString());
                        }
                    }

                    try
                    {
                        //Remove NULL values
                        foreach (var oTok in (oInv.Descendants().Where(t => t.Type == (JTokenType.Object) && t.HasValues == false).Reverse().ToList()))
                        {
                            try
                            {
                                if (oTok.Parent.Count == 1)
                                {
                                    oTok.Parent.Remove();
                                }

                                if (oTok.HasValues)
                                    oTok.Remove();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine("Error GetFull_4: " + ex.Message.ToString());
                            }
                        }
                    }
                    catch { }
                    JSort(oInv, true);

                    if (Index == -1)
                    {
                        //Cache Full
                        if (!File.Exists(Path.Combine(FilePath , "_full" , DeviceID + ".json")))
                        {
                            WriteHashAsync(DeviceID, oInv.ToString(), "_full").ConfigureAwait(false);
                        }
                        else
                        {
                            try
                            {
                                //Cache in Memory
                                CacheItemPolicy policy = new CacheItemPolicy();
                                policy.AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(60.0);

                                _cache.Set("RH-" + "_full" + "-" + DeviceID, oInv.ToString(), policy);
                            }
                            catch { }
                        }
                    }

                    return oInv;
                }


            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error GetFull_5: " + ex.Message.ToString());
            }

            return new JObject();
        }

        public static JObject GetRawId(string DeviceID, int Index = -1, string blockType = "")
        {
            JObject jResult = new JObject();

            if (string.IsNullOrEmpty(blockType))
                blockType = BlockType;

            try
            {
                Blockchain oChain;

                block lBlock = null;

                if (Index == -1)
                {
                    oChain = GetChain(DeviceID);
                    lBlock = oChain.GetLastBlock(blockType);

                }
                else
                {
                    oChain = GetChain(DeviceID);
                    lBlock = oChain.GetBlock(Index, blockType);
                }


                int index = lBlock.index;
                DateTime dInvDate = new DateTime(lBlock.timestamp);
                string sRawId = lBlock.data;

                jResult.Add(new JProperty("_index", index));
                jResult.Add(new JProperty("_inventoryDate", dInvDate));
                jResult.Add(new JProperty("_hash", sRawId));
            }
            catch { }

            return jResult;
        }

        public static JObject GetHistory(string DeviceID, string blockType = "")
        {
            JObject jResult = new JObject();

            if (string.IsNullOrEmpty(blockType))
                blockType = BlockType;

            try
            {

                string sChain = ReadHash(DeviceID, "_Chain");
                var oChain = JsonConvert.DeserializeObject<Blockchain>(sChain);
                foreach (block oBlock in oChain.Chain.Where(t => t.blocktype == blockType))
                {
                    try
                    {
                        JArray jArr = new JArray();
                        JObject jTemp = new JObject();
                        jTemp.Add("index", oBlock.index);
                        jTemp.Add("timestamp", new DateTime(oBlock.timestamp).ToUniversalTime());
                        jTemp.Add("data", oBlock.data);
                        jArr.Add(jTemp);
                        jResult.Add(oBlock.index.ToString(), jArr);
                    }
                    catch { }
                }

            }
            catch { }

            JSort(jResult);
            return jResult;
        }

        public static JArray GetJHistory(string DeviceID, string blockType = "")
        {
            string sResult = "";

            try
            {
                //Check if MemoryCache is initialized
                if (_cache == null)
                {
                    _cache = System.Runtime.Caching.MemoryCache.Default;
                }

                sResult = _cache["HIST-" + DeviceID + "-" + blockType] as string;
                //Try to get value from Memory
                if (!string.IsNullOrEmpty(sResult))
                {
                    return JArray.Parse(sResult);
                }
            }
            catch { }

            JArray jResult = new JArray();

            if (string.IsNullOrEmpty(blockType))
                blockType = BlockType;

            try
            {
                string sChain = ReadHash(DeviceID, "_Chain");
                var oChain = JsonConvert.DeserializeObject<Blockchain>(sChain);
                foreach (block oBlock in oChain.Chain.Where(t => t.blocktype == blockType))
                {
                    try
                    {
                        JObject jTemp = new JObject();
                        jTemp.Add("index", oBlock.index);
                        jTemp.Add("timestamp", new DateTime(oBlock.timestamp).ToUniversalTime());
                        jTemp.Add("type", "");
                        jResult.Add(jTemp);
                    }
                    catch { }
                }

                //Cache result in Memory
                if (!string.IsNullOrEmpty(sResult))
                {
                    CacheItemPolicy policy = new CacheItemPolicy();
                    policy.AbsoluteExpiration =
                    DateTimeOffset.Now.AddSeconds(60.0);
                    _cache.Set("HIST-" + DeviceID + "-" + blockType, jResult.ToString(Formatting.None), policy);
                }
            }
            catch { }

            return jResult;
        }

        public static JObject GetRaw(string RawID, string path = "")
        {
            JObject jResult = new JObject();
            try
            {
                JObject oInv = JObject.Parse(RawID);

                if (!path.Contains("*") && !path.Contains("..")) //Skip if using wildcards; we have to get the full content to filter
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        foreach (string spath in path.Split(';'))
                        {
                            string sLookupPath = spath.Replace(".##hash", "");
                            var aPath = spath.Split('.');
                            if (aPath.Length > 1) //at least 2 segments...
                            {
                                sLookupPath = sLookupPath.Substring(0, sLookupPath.LastIndexOf('.'));
                            }
                            //foreach (JProperty oTok in oInv.Descendants().Where(t => t.Path.StartsWith(spath.Split('.')[0]) && t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("##hash")).ToList())
                            foreach (JProperty oTok in oInv.Descendants().Where(t => t.Path.StartsWith(sLookupPath) && t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("##hash")).ToList())
                            {

                                string sH = oTok.Value.ToString();
                                string sRoot = oTok.Path.Split('.').Reverse().ToList()[1]; //second last as last is ##hash
                                                                                           //string sRoot = oTok.Path.Split('.')[0].Split('[')[0]; //AppMgmtDigest.Application.DisplayInfo.Info.##hash
                                string sObj = ReadHash(sH, sRoot);
                                if (!string.IsNullOrEmpty(sObj))
                                {
                                    var jStatic = JObject.Parse(sObj);
                                    oTok.Parent.Merge(jStatic);
                                    oTok.Remove();
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (JProperty oTok in oInv.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("##hash")).ToList())
                        {
                            string sH = oTok.Value.ToString();
                            string sRoot = oTok.Path.Split('.')[0].Split('[')[0];
                            string sObj = ReadHash(sH, sRoot);
                            if (!string.IsNullOrEmpty(sObj))
                            {
                                var jStatic = JObject.Parse(sObj);
                                oTok.Parent.Merge(jStatic);
                                oTok.Remove();
                            }
                        }
                    }

                    JSort(oInv);
                }

                return oInv;
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }

            return jResult;
        }

        public static JObject GetDiff(string DeviceId, int IndexLeft, int mode = -1, int IndexRight = -1, string blockType = "")
        {
            if (string.IsNullOrEmpty(blockType))
                blockType = BlockType;

            try
            {
                var right = GetFull(DeviceId, IndexRight);

                if (IndexLeft == 0)
                {
                    IndexLeft = ((int)right["_index"]) - 1;
                }
                var left = GetFull(DeviceId, IndexLeft);

                foreach (var oTok in right.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("@")).ToList())
                {
                    oTok.Remove();
                }
                foreach (var oTok in left.Descendants().Where(t => t.Type == JTokenType.Property && ((JProperty)t).Name.StartsWith("@")).ToList())
                {
                    oTok.Remove();
                }

                //Remove NULL values
                foreach (var oTok in left.Descendants().Where(t => t.Parent.Type == (JTokenType.Property) && t.Type == JTokenType.Null).ToList())
                {
                    oTok.Parent.Remove();
                }
                foreach (var oTok in right.Descendants().Where(t => t.Parent.Type == (JTokenType.Property) && t.Type == JTokenType.Null).ToList())
                {
                    oTok.Parent.Remove();
                }

                JSort(right);
                JSort(left);

                if (mode <= 1)
                {
                    var optipons = new JsonDiffPatchDotNet.Options();
                    if (mode == 0)
                    {
                        optipons.ArrayDiff = JsonDiffPatchDotNet.ArrayDiffMode.Simple;
                        optipons.TextDiff = JsonDiffPatchDotNet.TextDiffMode.Simple;
                    }
                    if (mode == 1)
                    {
                        optipons.ArrayDiff = JsonDiffPatchDotNet.ArrayDiffMode.Efficient;
                        optipons.TextDiff = JsonDiffPatchDotNet.TextDiffMode.Efficient;
                    }

                    var jpf = new JsonDiffPatchDotNet.JsonDiffPatch(optipons);

                    var oDiff = jpf.Diff(left, right);

                    if (oDiff == null)
                        return new JObject();

                    GC.Collect();
                    return JObject.Parse(oDiff.ToString());
                }
            }
            catch { }

            return new JObject();
        }

        /*      /// <summary>
                /// Convert Topic /Key/Key/[0]/val to JSON Path format Key.Key[0].val
                /// </summary>
                /// <param name="Topic"></param>
                /// <returns></returns>
                public static string Topic2JPath(string Topic)
                {
                    try
                    {
                        string sPath = "";
                        List<string> lItems = Topic.Split('/').ToList();
                        for (int i = 0; i < lItems.Count(); i++)
                        {
                            bool bArray = false;
                            int iVal = -1;
                            if (i + 1 < lItems.Count())
                            {
                                if (lItems[i + 1].Contains("[") && int.TryParse(lItems[i + 1].TrimStart('[').TrimEnd(']'), out iVal))
                                    bArray = true;
                                else
                                    bArray = false;
                            }
                            if (!bArray)
                                sPath += lItems[i] + ".";
                            else
                            {
                                sPath += lItems[i] + "[" + iVal.ToString() + "].";
                                i++;
                            }
                        }

                        return sPath.TrimEnd('.');
                    }
                    catch { }

                    return "";
                }
                */

        class TopicComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                if (x == y)
                    return 0;

                List<string> lx = x.Split('/').ToList();
                List<string> ly = y.Split('/').ToList();

                if (lx.Count > ly.Count)
                    return 1; //y is smaller
                if (lx.Count < ly.Count)
                    return -1; //x is smaller

                int i = 0;
                foreach (string s in lx)
                {
                    if (s == ly[i])
                    {
                        i++;
                        continue;
                    }

                    if (s.StartsWith("[") && s.EndsWith("]") && ly[i].StartsWith("[") && ly[i].EndsWith("]"))
                    {
                        try
                        {
                            int ix = int.Parse(s.TrimStart('[').TrimEnd(']'));
                            int iy = int.Parse(ly[i].TrimStart('[').TrimEnd(']'));
                            if (ix == iy)
                                return 0;
                            if (ix < iy)
                                return -1;
                            else
                                return 1;
                        }
                        catch { }
                    }

                    int iRes = string.Compare(s, ly[i]);

                    return iRes;
                }

                return string.Compare(x, y);
            }
        }

        /// <summary>
        /// Get a list of KeyID that contains a searchkey (freeText)
        /// </summary>
        /// <param name="searchkey"></param>
        /// <param name="KeyID"></param>
        /// <returns></returns>
        public static List<string> Search(string searchkey, string KeyID = "#id")
        {
            if (string.IsNullOrEmpty(searchkey))
                return new List<string>();

            if (string.IsNullOrEmpty(KeyID))
                KeyID = "#id";

            if (KeyID.Contains(','))
                KeyID = KeyID.Split(',')[0];

            List<string> lNames = new List<string>();

            lNames = FindLatestAsync(System.Net.WebUtility.UrlDecode(searchkey), "#" + KeyID.TrimStart('?').TrimStart('#')).Result;
            lNames.Sort();
            return lNames.Union(lNames).ToList();
        }

        public static async Task<JArray> QueryAsync(string paths, string select, string exclude, string where)
        {
            paths = System.Net.WebUtility.UrlDecode(paths);
            select = System.Net.WebUtility.UrlDecode(select);
            exclude = System.Net.WebUtility.UrlDecode(exclude);
            where = System.Net.WebUtility.UrlDecode(where);

            List<string> lExclude = new List<string>();
            List<string> lWhere = new List<string>();

            if (!string.IsNullOrEmpty(exclude))
            {
                lExclude = exclude.Split(';').ToList();
            }

            if (!string.IsNullOrEmpty(where))
            {
                lWhere = where.Split(';').ToList();
            }

            if (string.IsNullOrEmpty(select))
                select = "#id"; //,#Name,_inventoryDate

            //int i = 0;
            DateTime dStart = DateTime.Now;
            //JObject lRes = new JObject();
            JArray aRes = new JArray();
            List<string> lLatestHash = await GetAllChainsAsync();
            foreach (string sHash in lLatestHash)
            {
                bool foundData = false;
                try
                {
                    var jObj = GetFull(sHash);

                    //Where filter..
                    if (lWhere.Count > 0)
                    {
                        bool bWhere = false;
                        foreach (string sWhere in lWhere)
                        {
                            try
                            {
                                string sPath = sWhere;
                                string sVal = "";
                                string sOp = "";
                                if (sWhere.Contains("=="))
                                {
                                    int i = sWhere.IndexOf("==");
                                    sVal = sWhere.Substring(i + 2);
                                    sOp = "eq";
                                    sPath = sWhere.Substring(0, i);
                                }
                                if (sWhere.Contains("!="))
                                {
                                    int i = sWhere.IndexOf("!=");
                                    sVal = sWhere.Substring(i + 2);
                                    sOp = "ne";
                                    sPath = sWhere.Substring(0, i);
                                }

                                var jRes = jObj.SelectToken(sPath.Trim());
                                if (jRes == null)
                                {
                                    bWhere = true;
                                    continue;
                                }
                                else
                                {
                                    switch (sOp)
                                    {
                                        case "eq":
                                            if (sVal.Trim() != jRes.ToString())
                                            {
                                                bWhere = true;
                                                continue;
                                            }
                                            break;
                                        case "ne":
                                            if (sVal.Trim() == jRes.ToString())
                                            {
                                                bWhere = true;
                                                continue;
                                            }
                                            break;
                                        default:
                                            bWhere = true;
                                            continue;
                                    }
                                }
                            }
                            catch { }
                        }

                        if (bWhere)
                        {
                            continue;
                        }
                    }

                    JObject oRes = new JObject();
                    foreach (string sAttrib in select.Split(';'))
                    {
                        //var jVal = jObj[sAttrib];
                        var jVal = jObj.SelectToken(sAttrib);

                        if (jVal != null)
                        {
                            oRes.Add(sAttrib.Trim(), jVal);
                        }
                    }
                    if (!string.IsNullOrEmpty(paths)) //only return defined objects, if empty all object will return
                    {
                        //Generate list of excluded paths
                        List<string> sExclPath = new List<string>();
                        foreach (string sExclude in lExclude)
                        {
                            foreach (var oRem in jObj.SelectTokens(sExclude, false).ToList())
                            {
                                sExclPath.Add(oRem.Path);
                            }
                        }

                        foreach (string path in paths.Split(';'))
                        {
                            try
                            {
                                var oToks = jObj.SelectTokens(path.Trim(), false);

                                if (oToks.Count() == 0)
                                {
                                    if (!foundData)
                                    {
                                        oRes = new JObject(); //remove selected attributes as we do not have any vresults from jsonpath
                                        continue;
                                    }
                                }

                                foreach (JToken oTok in oToks)
                                {
                                    try
                                    {
                                        if (oTok.Type == JTokenType.Object)
                                        {
                                            oRes.Merge(oTok);
                                            //oRes.Add(jObj[select.Split(',')[0]].ToString(), oTok);
                                            continue;
                                        }
                                        if (oTok.Type == JTokenType.Array)
                                        {
                                            oRes.Add(new JProperty(path, oTok));
                                        }
                                        if (oTok.Type == JTokenType.Property)
                                            oRes.Add(oTok.Parent);

                                        if (oTok.Type == JTokenType.String ||
                                            oTok.Type == JTokenType.Integer ||
                                            oTok.Type == JTokenType.Date ||
                                            oTok.Type == JTokenType.Boolean ||
                                            oTok.Type == JTokenType.Float ||
                                            oTok.Type == JTokenType.Guid ||
                                            oTok.Type == JTokenType.TimeSpan)
                                        {
                                            //check if path is excluded
                                            if (!sExclPath.Contains(oTok.Path))
                                                oRes.Add(oTok.Path, oTok);
                                        }

                                        if (oTok.Type == JTokenType.Date)
                                            oRes.Add(oTok.Parent);

                                        foundData = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine("Error Query_5: " + ex.Message.ToString());
                                    }

                                }

                                /*if (oToks.Count() == 0)
                                    oRes = new JObject(); */
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine("Error Query_5: " + ex.Message.ToString());
                            }
                        }
                    }

                    if (oRes.HasValues)
                    {
                        //Remove excluded Properties
                        foreach (string sExclude in lExclude)
                        {
                            foreach (var oRem in oRes.SelectTokens(sExclude, false).ToList())
                            {
                                oRem.Parent.Remove();
                            }
                        }

                        aRes.Add(oRes);
                        //lRes.Add(i.ToString(), oRes);
                        //i++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error Query_5: " + ex.Message.ToString());
                }
            }

            GC.Collect();
            return aRes;

        }

        public static JArray QueryAll(string paths, string select, string exclude)
        {
            paths = System.Net.WebUtility.UrlDecode(paths);
            select = System.Net.WebUtility.UrlDecode(select);
            exclude = System.Net.WebUtility.UrlDecode(exclude);
            List<string> lExclude = new List<string>();

            if (!string.IsNullOrEmpty(exclude))
            {
                lExclude = exclude.Split(';').ToList();
            }

            if (string.IsNullOrEmpty(select))
                select = "#id"; //,#Name,_inventoryDate

            //JObject lRes = new JObject();
            JArray aRes = new JArray();
            List<string> lHashes = new List<string>();
            try
            {
                if (UseFileStore)
                {
                    foreach (var oFile in new DirectoryInfo(FilePath + "\\_Assets").GetFiles("*.json"))
                    {
                        bool foundData = false;
                        JObject jObj = GetRaw(File.ReadAllText(oFile.FullName), paths);

                        if (paths.Contains("*") || paths.Contains(".."))
                        {
                            try
                            {
                                jObj = GetFull(jObj["#id"].Value<string>(), jObj["_index"].Value<int>());
                            }
                            catch { }
                        }

                        JObject oRes = new JObject();

                        foreach (string sAttrib in select.Split(';'))
                        {
                            //var jVal = jObj[sAttrib];
                            var jVal = jObj.SelectToken(sAttrib);

                            if (jVal != null)
                            {
                                oRes.Add(sAttrib.Trim(), jVal);
                            }
                        }

                        if (!string.IsNullOrEmpty(paths)) //only return defined objects, if empty all object will return
                        {
                            //Generate list of excluded paths
                            List<string> sExclPath = new List<string>();
                            foreach (string sExclude in lExclude)
                            {
                                foreach (var oRem in jObj.SelectTokens(sExclude, false).ToList())
                                {
                                    sExclPath.Add(oRem.Path);
                                }
                            }

                            foreach (string path in paths.Split(';'))
                            {
                                try
                                {
                                    var oToks = jObj.SelectTokens(path.Trim(), false);

                                    if (oToks.Count() == 0)
                                    {
                                        if (!foundData)
                                        {
                                            oRes = new JObject(); //remove selected attributes as we do not have any vresults from jsonpath
                                            continue;
                                        }
                                    }

                                    foreach (JToken oTok in oToks)
                                    {
                                        try
                                        {
                                            if (oTok.Type == JTokenType.Object)
                                            {
                                                oRes.Merge(oTok);
                                                //oRes.Add(jObj[select.Split(',')[0]].ToString(), oTok);
                                                continue;
                                            }
                                            if (oTok.Type == JTokenType.Array)
                                            {
                                                oRes.Add(new JProperty(path, oTok));
                                            }
                                            if (oTok.Type == JTokenType.Property)
                                                oRes.Add(oTok.Parent);

                                            if (oTok.Type == JTokenType.String)
                                            {
                                                //check if path is excluded
                                                if (!sExclPath.Contains(oTok.Path))
                                                    oRes.Add(oTok.Path, oTok.ToString());
                                            }

                                            if (oTok.Type == JTokenType.Date)
                                                oRes.Add(oTok.Parent);

                                            foundData = true;
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine("Error Query_5: " + ex.Message.ToString());
                                        }

                                    }

                                    /*if (oToks.Count() == 0)
                                        oRes = new JObject(); */
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine("Error Query_5: " + ex.Message.ToString());
                                }
                            }
                        }

                        if (oRes.HasValues)
                        {
                            string sHa = CalculateHash(oRes.ToString(Formatting.None));
                            if (!lHashes.Contains(sHa))
                            {
                                aRes.Add(oRes);
                                lHashes.Add(sHa);
                            }

                            //Remove excluded Properties
                            foreach (string sExclude in lExclude)
                            {
                                foreach (var oRem in oRes.SelectTokens(sExclude, false).ToList())
                                {
                                    oRem.Parent.Remove();
                                    //oRes.Remove(oRem.Path);
                                }
                            }
                        }
                    }
                    GC.Collect();
                    return aRes;
                }
            }
            catch { }

            return new JArray();
        }

        public enum ChangeType { New, Update };

        public class Change
        {
            public ChangeType changeType;
            public DateTime lastChange;
            public int index;
            public string id;
        }

        public static JArray GetChanges(TimeSpan age, int changeType = -1)
        {
            age.ToString();
            List<Change> lRes = new List<Change>();

             foreach (var sID in GetAllChainsAsync().Result)
            {
                Change oRes = new Change();
                oRes.id = sID;
                var jObj = JObject.Parse(ReadHash(sID, "_Chain"));
                oRes.lastChange = new DateTime(jObj["Chain"].Last["timestamp"].Value<long>());
                if (DateTime.Now.Subtract(oRes.lastChange) > age)
                {
                    continue;
                }
                oRes.index = jObj["Chain"].Last["index"].Value<int>();
                if (oRes.index > 1)
                    oRes.changeType = ChangeType.Update;
                else
                    oRes.changeType = ChangeType.New;

                if (changeType >= 0)
                {
                    if (((int)oRes.changeType) != changeType)
                        continue;
                }

                lRes.Add(oRes);
            }
            return JArray.Parse(JsonConvert.SerializeObject(lRes.OrderBy(t => t.id).ToList(), Formatting.None));
        }

        /// <summary>
        /// Get all BlockChains
        /// </summary>
        /// <returns>List of BlockChain (Device) ID's</returns>
        public static async Task<List<string>> GetAllChainsAsync()
        {
            return await Task.Run(() =>
            {
                List<string> lResult = new List<string>();
                try
                {
                    if (UseFileStore)
                    {
                        foreach (var oFile in new DirectoryInfo(FilePath + "\\_Chain").GetFiles("*.json"))
                        {
                            lResult.Add(System.IO.Path.GetFileNameWithoutExtension(oFile.Name));
                        }
                        return lResult;
                    }
                }
                catch { }

                return lResult;
            });

        }

        /// <summary>
        /// Get List of latest Blocks of each Chain
        /// </summary>
        /// <returns></returns>
        public static async Task<List<string>> GetLatestBlocksAsync()
        {
            return await Task.Run(() =>
            {
                List<string> lResult = new List<string>();
                try
                {
                }
                catch { }
                return lResult;
            });
        }

        public static async Task<List<string>> FindHashOnContentAsync(string searchstring)
        {
            return await Task.Run(() =>
            {
                List<string> lResult = new List<string>();
                try
                {
                }
                catch { }

                return lResult;
            });
        }

        public static List<string> FindLatestRawWithHash(List<string> HashList, string KeyID = "#id")
        {
            List<string> lResult = new List<string>();
            try
            {
                List<string> latestBlocks = GetLatestBlocksAsync().Result;
            }
            catch { }

            return lResult;
        }

        public static async Task<List<string>> FindLatestAsync(string searchstring, string KeyID = "#id")
        {
            List<string> lResult = new List<string>();
            try
            {
            }
            catch { }

            return lResult;
        }

        public static bool Export(string URL, string RemoveObjects)
        {
            int iCount = 0;
            bool bResult = true;

            try
            {
                oClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (UseFileStore)
                {
                    foreach (var sID in GetAllChainsAsync().Result)
                    {
                        try
                        {
                            var jObj = JObject.Parse(ReadHash(sID, "_Chain"));
                            foreach (var sBlock in jObj.SelectTokens("Chain[*].data"))
                            {
                                try
                                {
                                    string sBlockID = sBlock.Value<string>();
                                    if (!string.IsNullOrEmpty(sBlockID))
                                    {
                                        var jBlock = GetRaw(ReadHash(sBlockID, "_Assets"));
                                        jBlock.Remove("#id"); //old Version of jainDB 
                                        //jBlock.Remove("_date");
                                        jBlock.Remove("_index");
                                        //jBlock.Add("#id", sID);

                                        //Remove Objects from Chain
                                        foreach (string sRemObj in RemoveObjects.Split(';'))
                                        {
                                            try
                                            {
                                                foreach (var oTok in jBlock.Descendants().Where(t => t.Path == sRemObj).ToList())
                                                {
                                                    oTok.Remove();
                                                    break;
                                                }
                                            }
                                            catch { }
                                        }

                                        string sResult = UploadToREST(URL + "/upload/" + sID, jBlock.ToString(Formatting.None));
                                        //System.Threading.Thread.Sleep(50);
                                        if (!string.IsNullOrEmpty(sResult.Trim('"')))
                                        {
                                            Console.WriteLine("Exported: " + sResult);
                                            iCount++;
                                        }
                                        else
                                        {
                                            jBlock.ToString();
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Error: " + ex.Message);
                                    bResult = false;
                                }
                            }
                            System.Threading.Thread.Sleep(100);
                        }
                        catch { bResult = false; }
                    }
                }
            }
            catch { bResult = false; }
            Console.WriteLine("Done... " + iCount.ToString() + " Blocks exported");
            return bResult;
        }

        public static string UploadToREST(string URL, string content)
        {
            try
            {
                //oClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpContent oCont = new StringContent(content);

                var response = oClient.PostAsync(URL, oCont);
                response.Wait(15000);
                if (response.IsCompleted)
                {
                    return response.Result.Content.ReadAsStringAsync().Result.ToString();
                }


            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }

            return "";

        }

        public static void JSort(JObject jObj, bool deep = false)
        {
            var props = jObj.Properties().ToList();
            foreach (var prop in props)
            {
                prop.Remove();
            }

            foreach (var prop in props.OrderBy(p => p.Name))
            {
                jObj.Add(prop);

                if (deep) //Deep Sort
                {
                    var child = prop.Descendants().Where(t => t.Type == (JTokenType.Object)).ToList();

                    foreach (JObject cChild in child)
                    {
                        JSort(cChild, false);
                    }
                }

                if (prop.Value is JObject)
                    JSort((JObject)prop.Value);
            }
        }
    }

    public class BlockChain
    {
        public class Blockchain
        {
            private static int _complexity = 0;

            public Blockchain(string Data, string Blocktype = "root", int complexity = 0)
            {
                _complexity = complexity;
                block._complexity = complexity;

                this.Chain = new List<block>()
            {
                new block
                {
                    index = 0,
                    timestamp = DateTime.Now.Ticks,
                    previous_hash = new byte[0], //block.GetHash("genesis"),
                    data = Data, //!! Data is not stored !!
                    blocktype = Blocktype
                }
            };


                block oGenesis = Chain.First();
                if (string.IsNullOrEmpty(Data))
                    oGenesis.data = "";
                oGenesis.nonce = Mine(0, Blocktype, new byte[0]);
                oGenesis.calc_hash();

                //File.WriteAllText(@"c:\temp\data\" + Base32Encoding.ToString(oGenesis.data) + ".json", Data);
            }

            /// <summary>
            /// Check all hashes from bottom to top
            /// </summary>
            /// <returns>true = all fine; false = something is wrong</returns>
            public bool ValidateChain()
            {
                foreach (block bCheck in Chain.OrderByDescending(t => t.timestamp))
                {
                    long Previous_nonce = 0;

                    var oParent = Chain.FirstOrDefault(t => t.hash == bCheck.previous_hash);
                    if (oParent != null)
                        Previous_nonce = oParent.nonce;

                    if (!bCheck.validate(Previous_nonce))
                        return false;
                }
                return true;
            }

            /// <summary>
            /// Compare another chain with the current and replace the current if necessary
            /// </summary>
            /// <param name="otherChain"></param>
            /// <returns>true = Chain replaced ; false = keep current Chain</returns>
            public bool resolve_conflicts(Blockchain otherChain)
            {
                if (otherChain.ValidateChain()) //Check if Chain is valid
                {
                    if (otherChain.Chain.Count() > this.Chain.Count()) //Check if Chain is longer than the current
                    {
                        this.Chain = otherChain.Chain;
                        return true;
                    }
                }
                return false;
            }

            public List<block> Chain { get; set; }

            /*public block NewBlock(string Data, block ParentBlock, string BlockType = "")
            {
                var oNew = new block()
                {
                    index = ParentBlock.index + 1,
                    timestamp = DateTime.Now.Ticks,
                    previous_hash = ParentBlock.hash,
                    data = block.GetHash(Data),
                    blocktype = BlockType
                };
                //oNew.hash = block.GetHash(oNew.index.ToString() + oNew.timestamp.ToString() + oNew.proof.ToString() + oNew.previous_hash.ToString() + oNew.data.ToString());
                oNew.calc_hash();
                Chain.Add(oNew);

                return oNew;
            }*/

            public block UseBlock(string Data, block FreeBlock)
            {
                /*if(!validateChain()) //Chain not Valid
                    return FreeBlock;*/

                //Check if FreeBlock is valid..
                var oParent = Chain.FirstOrDefault(t => t.hash == FreeBlock.previous_hash);
                if (oParent != null)
                {
                    /*if(!FreeBlock.validate(oParent.nonce))
                    {
                        Console.WriteLine("Invalid Block: \n" + JsonConvert.SerializeObject(FreeBlock));
                        return FreeBlock;
                    }*/
                }
                else
                {
                    Console.WriteLine("Invalid Block: \n" + JsonConvert.SerializeObject(FreeBlock));
                    return FreeBlock;
                }


                if (FreeBlock.data != null | FreeBlock.hash != null | FreeBlock.signature != null) //it's not a free Block
                    return FreeBlock;

                //FreeBlock.index = oParent.index + 1;
                FreeBlock.data = Data;
                FreeBlock.timestamp = DateTime.Now.Ticks;
                FreeBlock.calc_hash();

                return FreeBlock;
            }

            public block MineNewBlock(block ParentBlock, string Blocktype = "")
            {
                if (string.IsNullOrEmpty(Blocktype)) //Use ParentBlock.blocktype if Blocktype is empty
                    Blocktype = ParentBlock.blocktype;

                if (Chain.Count(t => t.previous_hash == ParentBlock.hash & t.blocktype == Blocktype) == 0) //only on Blocktype tree allowed
                {

                    int iIndex = Chain.Max(t => t.index) + 1;

                    var oNew = new block()
                    {
                        index = ParentBlock.index + 1, //iIndex,
                        timestamp = DateTime.Now.Ticks,
                        previous_hash = ParentBlock.hash,
                        blocktype = Blocktype,
                        nonce = Mine(ParentBlock.nonce, Blocktype, ParentBlock.hash)
                    };

                    Chain.Add(oNew);

                    return oNew;
                }

                return new block();
            }

            private long Mine(long Previos_noonce, string Blocktype, byte[] Previous_Hash)
            {
                byte[] bHash = new byte[0];

                long nonce = Previos_noonce;

                //current implementation allows to mine new a block without proof of work...
                bool DoWork = false;

                if (_complexity > 0 && nonce != 0 && DoWork) //Skip nonce generation if complexity is 0
                {
                    string sGoal = new string('0', _complexity); //generate a string with leading '0'
                    //string sRes = "";

                    do
                    {
                        if (nonce >= 9223372036854775807) //check overflow
                        {
                            nonce = 0; //reset nonce
                        }

                        nonce++;

                        bHash = block.GetHash((Previos_noonce + nonce).ToString() + Blocktype + Convert.ToBase64String(Previous_Hash));

                    } while (!Hash.checkTrailingZero(bHash, _complexity, sGoal));

                }
                else
                {
                    nonce++;
                }

                return nonce;
            }

            public static string ByteArrayToString(byte[] input)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < input.Length; i++)
                {
                    sb.Append(input[i].ToString("X2"));
                }
                return sb.ToString();
            }

            public block GetLastBlock(string blockType = "")
            {
                if (string.IsNullOrEmpty(blockType))
                    return Chain.FirstOrDefault(t => Chain.Count(q => ByteArrayToString(q.previous_hash) == ByteArrayToString(t.hash)) == 0);
                else
                {
                    var oBlock = Chain.FirstOrDefault(t => Chain.Count(q => ByteArrayToString(q.previous_hash) == ByteArrayToString(t.hash)) == 0 && (t.blocktype == blockType));

                    //return genesis block if no other block was found
                    if (oBlock == null)
                        return GetBlock(0, "root");
                    else
                        return oBlock;
                }
            }

            public block GetBlock(int index, string blockType = "")
            {
                if (string.IsNullOrEmpty(blockType))
                    return Chain.FirstOrDefault(t => t.index == index);
                else
                    return Chain.FirstOrDefault(t => t.index == index && t.blocktype == blockType);
            }
        }

        public class block
        {
            internal static int _complexity = 0;

            public int index { get; set; }

            public long timestamp { get; set; }

            public byte[] previous_hash { get; set; }

            public byte[] hash { get; set; }

            public long nonce { get; set; }

            public string data { get; set; }

            public byte[] signature { get; set; }

            public string blocktype { get; set; }

            public static byte[] GetHash(string input)
            {
                if (!string.IsNullOrEmpty(input))
                {
                    return Hash.CalculateSHA2_256Hash(input);
                }

                return null;
            }

            public void calc_hash()
            {
                try
                {
                    string sData = index.ToString() + timestamp.ToString() + previous_hash.ToString() + data.ToString() + nonce.ToString() + blocktype;

                    byte[] bHash = GetHash(sData);

                    //Do a ProofOfWork if complexity is > 0
                    bool DoWork = true;
                    DateTime dStart = DateTime.Now;

                    if (_complexity > 0 && nonce != 0 && DoWork)
                    {
                        string sGoal = new string('0', _complexity); //generate a string with leading '0'

                        if (!Hash.checkTrailingZero(bHash, _complexity, sGoal)) //only calc nonce if it's not valid
                        {
                            do
                            {
                                if (nonce >= 9223372036854775807) //check overflow
                                {
                                    nonce = 0; //reset nonce
                                    timestamp = DateTime.Now.Ticks; //reset timestamp
                                }
                                nonce++;

                                bHash = block.GetHash(index.ToString() + timestamp.ToString() + previous_hash.ToString() + data.ToString() + nonce.ToString() + blocktype);

                            } while (!Hash.checkTrailingZero(bHash, _complexity, sGoal));
                        }
                    }
                    TimeSpan tDur = DateTime.Now - dStart;
                    tDur.TotalMilliseconds.ToString();

                    hash = bHash;

                    signature = Sign(hash, ""); //Add CertSubject as Parameter
                }
                catch { }
            }

            static byte[] Sign(byte[] hash, string certSubject)
            {
                if (!string.IsNullOrEmpty(certSubject))
                {
                    // Access Personal (MY) certificate store of current user
                    X509Store my = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                    my.Open(OpenFlags.ReadOnly);

                    // Find the certificate we'll use to sign
                    foreach (X509Certificate2 cert in my.Certificates)
                    {
                        if (cert.Subject.Contains(certSubject) & cert.HasPrivateKey)
                        {
                            // We found it.
                            // Get its associated CSP and private key
                            using (var key = cert.GetRSAPrivateKey())
                            {
                                return key.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                            }
                        }
                    }
                }

                return new byte[0];
            }

            public bool validate(long Previous_nonce = 0)
            {
                if (data != null)
                {
                    /*if (signature == null)
                        return false;*/

                    if (hash == null)
                        return false;

                    //Check hash
                    byte[] bHash = GetHash(index.ToString() + timestamp.ToString() + previous_hash.ToString() + data.ToString() + nonce.ToString() + blocktype);
                    if (Convert.ToBase64String(bHash) != Convert.ToBase64String(hash))
                        return false;

                    //Check signature
                    /*X509Store my = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                    my.Open(OpenFlags.ReadOnly);
                    foreach (X509Certificate2 cert in my.Certificates)
                    {
                        if (cert.Subject.Contains("xxx"))
                        {
                            using (var key = cert.GetRSAPublicKey())
                            {
                                if (!key.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                                {
                                    return false;
                                }
                            }
                        }
                    }*/
                }

                if (index > 0)
                {
                    //Validate nonce...
                    string sGoal = new string('0', _complexity); //generate a string with leading '0'
                    if (!Hash.checkTrailingZero(hash, _complexity, sGoal))
                        return false;
                }

                return true;
            }
        }

        public class Base32Encoding
        {
            public static byte[] ToBytes(string input)
            {
                if (string.IsNullOrEmpty(input))
                {
                    throw new ArgumentNullException("input");
                }

                input = input.TrimEnd('='); //remove padding characters
                int byteCount = input.Length * 5 / 8; //this must be TRUNCATED
                byte[] returnArray = new byte[byteCount];

                byte curByte = 0, bitsRemaining = 8;
                int mask = 0, arrayIndex = 0;

                foreach (char c in input)
                {
                    int cValue = CharToValue(c);

                    if (bitsRemaining > 5)
                    {
                        mask = cValue << (bitsRemaining - 5);
                        curByte = (byte)(curByte | mask);
                        bitsRemaining -= 5;
                    }
                    else
                    {
                        mask = cValue >> (5 - bitsRemaining);
                        curByte = (byte)(curByte | mask);
                        returnArray[arrayIndex++] = curByte;
                        curByte = (byte)(cValue << (3 + bitsRemaining));
                        bitsRemaining += 3;
                    }
                }

                //if we didn't end with a full byte
                if (arrayIndex != byteCount)
                {
                    returnArray[arrayIndex] = curByte;
                }

                return returnArray;
            }

            public static string ToString(byte[] input)
            {
                if (input == null || input.Length == 0)
                {
                    throw new ArgumentNullException("input");
                }

                int charCount = (int)Math.Ceiling(input.Length / 5d) * 8;
                char[] returnArray = new char[charCount];

                byte nextChar = 0, bitsRemaining = 5;
                int arrayIndex = 0;

                foreach (byte b in input)
                {
                    nextChar = (byte)(nextChar | (b >> (8 - bitsRemaining)));
                    returnArray[arrayIndex++] = ValueToChar(nextChar);

                    if (bitsRemaining < 4)
                    {
                        nextChar = (byte)((b >> (3 - bitsRemaining)) & 31);
                        returnArray[arrayIndex++] = ValueToChar(nextChar);
                        bitsRemaining += 5;
                    }

                    bitsRemaining -= 3;
                    nextChar = (byte)((b << bitsRemaining) & 31);
                }

                //if we didn't end with a full char
                if (arrayIndex != charCount)
                {
                    returnArray[arrayIndex++] = ValueToChar(nextChar);
                    while (arrayIndex != charCount) returnArray[arrayIndex++] = '='; //padding
                }

                return new string(returnArray);
            }

            private static int CharToValue(char c)
            {
                int value = (int)c;

                //65-90 == uppercase letters
                if (value < 91 && value > 64)
                {
                    return value - 65;
                }
                //50-55 == numbers 2-7
                if (value < 56 && value > 49)
                {
                    return value - 24;
                }
                //97-122 == lowercase letters
                if (value < 123 && value > 96)
                {
                    return value - 97;
                }

                throw new ArgumentException("Character is not a Base32 character.", "c");
            }

            private static char ValueToChar(byte b)
            {
                if (b < 26)
                {
                    return (char)(b + 65);
                }

                if (b < 32)
                {
                    return (char)(b + 24);
                }

                throw new ArgumentException("Byte is not a value Base32 value.", "b");
            }

        }
    }

    public class Hash
    {
        //Base58 Digits
        private const string Digits = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        public static byte[] CalculateSHA2_256Hash(string input)
        {
            SHA256 sha = SHA256.Create();
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hash = sha.ComputeHash(inputBytes);
            byte[] mhash = new byte[hash.Length + 2]; //we need two additional bytes

            //Add Multihash identifier
            hash.CopyTo(mhash, 2);
            mhash[0] = 0x12; //SHA256
            mhash[1] = Convert.ToByte(hash.Length); //Hash length

            return mhash;
        }

        public static string CalculateSHA2_256HashString(string input)
        {
            return Encode58(CalculateSHA2_256Hash(input));
        }

        public static byte[] CalculateMD5Hash(string input)
        {
            MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hash = md5.ComputeHash(inputBytes);
            byte[] mhash = new byte[hash.Length + 2];
            hash.CopyTo(mhash, 2);
            //Add Multihash identifier
            mhash[0] = 0xD5; //MD5
            mhash[1] = Convert.ToByte(hash.Length); //Hash legth
            return mhash;
        }

        public static string CalculateMD5HashString(string input)
        {
            return Encode58(CalculateMD5Hash(input));
        }

        public static string Encode58(byte[] data)
        {
            // Decode byte[] to BigInteger
            BigInteger intData = 0;
            for (int i = 0; i < data.Length; i++)
            {
                intData = intData * 256 + data[i];
            }

            // Encode BigInteger to Base58 string
            string result = "";
            while (intData > 0)
            {
                int remainder = (int)(intData % 58);
                intData /= 58;
                result = Digits[remainder] + result;
            }

            // Append `1` for each leading 0 byte
            for (int i = 0; i < data.Length && data[i] == 0; i++)
            {
                result = '1' + result;
            }

            return result;
        }

        public static bool checkTrailingZero(byte[] bHash, int complexity, string sGoal = "")
        {
            bool bRes = false;
            try
            {
                if (complexity > 0)
                {
                    if (string.IsNullOrEmpty(sGoal)) //create TrailingZero string if it does not exists
                        sGoal = new string('0', complexity);

                    //Check the last n Bits of the hash if they are 0, where n is the complexity
                    int iBytes = 1 + (complexity / 8); //Nr of bytes we have toc get
                    var aLast = bHash.Skip(bHash.Length - iBytes); //Get the last n Bytes
                    string sRes = string.Join("", aLast.Select(x => Convert.ToString(x, 2).PadLeft(8, '0'))); //Convert to bit string

                    if (sRes.Substring(sRes.Length - complexity) == sGoal) //do we have a match ?
                        return true;
                }
                else
                    return true;
            }
            catch { }

            return bRes;
        }
    }
}
