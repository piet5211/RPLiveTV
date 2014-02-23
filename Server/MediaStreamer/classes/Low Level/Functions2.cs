﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;
using System.Runtime.Serialization.Formatters.Binary;

//this is a reduced version of functions.cs in RPSERVER

namespace FatAttitude.MediaStreamer
{
    public static class Functions2
    {

        static Functions2()
        {
            StoredLogEntries = new List<string>();
        }

        // Errors
        static List<string> StoredLogEntries;
        public static string DebugLogFileFN
        {
            get
            {
                string strPath = AppDataFolder;
                return Path.Combine(strPath, "RPServer.log");
            }
        }
        static object writeLogLock = new object();
        public static void WriteLineToLogFileIfSetting(bool setting, string txtLine)
        {
            if (setting)
                WriteLineToLogFile(txtLine);
        }
        public static void WriteLineToLogFile(string txtLine)
        {
            Monitor.Enter(writeLogLock);
            string logLine = System.String.Format("{0:O}: {1}.", System.DateTime.Now, txtLine);

            System.IO.StreamWriter sw;
            try
            {
                sw = System.IO.File.AppendText(DebugLogFileFN);
            }
            catch
            {
                // Store the log entry for later
                if (StoredLogEntries.Count < 150)  // limit
                    StoredLogEntries.Add(logLine);

                Monitor.Exit(writeLogLock);
                return;
            }

            try
            {
                // Write any pending log entries
                if (StoredLogEntries.Count > 0)
                {
                    foreach (string s in StoredLogEntries)
                    {
                        sw.WriteLine(s);
                    }
                    StoredLogEntries.Clear();
                }

                sw.WriteLine(logLine);
            }
            finally
            {
                sw.Close();
            }

            Monitor.Exit(writeLogLock);
        }
        public static void WriteExceptionToLogFileIfSetting(bool setting, Exception e)
        {
            if (setting)
                WriteExceptionToLogFile(e);
        }
        public static void WriteExceptionToLogFile(Exception e)
        {
            string txtException = "EXCEPTION DETAILS: " + e.Message + Environment.NewLine + e.Source + Environment.NewLine + e.StackTrace + Environment.NewLine;
            
            WriteLineToLogFile(txtException);

            if (e.InnerException != null)
            {
                WriteLineToLogFile(Environment.NewLine + "INNER:");
                WriteExceptionToLogFile(e.InnerException);
            }
        }
        // Flag for recordings
        public static bool isVideoStreamingObjectActive = false;

        // Dates
        public static bool partOfEventOccursInsideTimeWindow(DateTime eventStart, DateTime eventStop, DateTime windowStart, DateTime windowStop)
        {
            return (
                (eventStop > windowStart) & (eventStart < windowStop)
                );
        }
        public static string japDateFormat(DateTime theDate)
        {
            return theDate.Year.ToString() + "-" + theDate.Month.ToString("D2") + "-" + theDate.Day.ToString("D2");
        }

        // Encryption
        public static string EncodePassword(string originalPassword)
        {
            //Declarations
            Byte[] originalBytes;
            Byte[] encodedBytes;
            MD5 md5;

            //Instantiate MD5CryptoServiceProvider, get bytes for original password and compute hash (encoded password)
            md5 = new MD5CryptoServiceProvider();
            originalBytes = Encoding.Unicode.GetBytes(originalPassword);
            encodedBytes = md5.ComputeHash(originalBytes);

            //Convert encoded bytes back to a 'readable' string
            return BitConverter.ToString(encodedBytes);
        }
        public static string ConvertUTF16ToUTF8(string strUtf16)
        {
            return Encoding.UTF8.GetString(Encoding.Unicode.GetBytes(strUtf16));
        }
        /**
        * This method ensures that the output String has only
        * valid XML unicode characters as specified by the
        * XML 1.0 standard. For reference, please see
        * <a href="http://www.w3.org/TR/2000/REC-xml-20001006#NT-Char">the
        * standard</a>. This method will return an empty
        * String if the input is null or empty.
        *
        * @param in The String whose non-valid characters we want to remove.
        * @return The in String, stripped of non-valid characters.
        */
      
        public static string StripIllegalXmlCharacters(string text)
        {
            const string illegalXmlChars = @"[\u0000-\u0008]|[\u000B-\u000C]|[\u000E-\u0019]|[\u007F-\u009F]";

            var regex = new Regex(illegalXmlChars, RegexOptions.IgnoreCase);

            if (regex.IsMatch(text))
            {
                text = regex.Replace(text, " ");
            }

            return text;
        }

        // System / IO
        public static bool OSSupportsExplorerLibraries
        {
            get
            {
                return (Environment.OSVersion.Version >= new Version(6, 1));
            }
        }
        public static bool OSSupportsAdvancedFirewallInNetSH
        {
            get
            {
                return (Environment.OSVersion.Version >= new Version(6, 0)); // At least VISTA
            }
        }
        public static bool OSSupportsMediaDurationInShell
        {
            get
            {
                return (Environment.OSVersion.Version >= new Version(6, 1));
            }
        }
        public static bool isXP
        {
            get
            {
                return (OSName.Equals("XP"));
            }
        }
        static string OSName
        {
            get
            {
                //Get Operating system information.
                OperatingSystem os = Environment.OSVersion;
                //Get version information about the os.
                Version vs = os.Version;

                //Variable to hold our return value
                string operatingSystem = "?";

                if (os.Platform == PlatformID.Win32Windows)
                {
                    //This is a pre-NT version of Windows
                    switch (vs.Minor)
                    {
                        case 0:
                            operatingSystem = "95";
                            break;
                        case 10:
                            if (vs.Revision.ToString() == "2222A")
                                operatingSystem = "98SE";
                            else
                                operatingSystem = "98";
                            break;
                        case 90:
                            operatingSystem = "Me";
                            break;
                        default:
                            break;
                    }
                }
                else if (os.Platform == PlatformID.Win32NT)
                {
                    switch (vs.Major)
                    {
                        case 3:
                            operatingSystem = "NT 3.51";
                            break;
                        case 4:
                            operatingSystem = "NT 4.0";
                            break;
                        case 5:
                            if (vs.Minor == 0)
                                operatingSystem = "2000";
                            else
                                operatingSystem = "XP";
                            break;
                        case 6:
                            if (vs.Minor == 0)
                                operatingSystem = "Vista";
                            else
                                operatingSystem = "7";
                            break;
                        default:
                            break;
                    }
                }

                return operatingSystem;
            }
        }
        public static string GetRecordPath()
        {                   
           // Start with windows?
            try
            {
                RegistryKey rkRecPath = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Media Center\\Service\\Recording", false);
                return (string)rkRecPath.GetValue("RecordPath", "C:\\");
            }
            catch { }
            return "";
        }
        public static bool isRunningIn32BitMode
        {
            get
            {
                return (IntPtr.Size == 4);
            }
        }
        public static bool isLegacyAppRunning()
        {
            return isProcessRunning("RemotePotato.exe");
        }
        public static bool isProcessRunning(string name)
        {
            return (findProcessOrNull(name) != null);
        }
        private static Process findProcessOrNull(string name)
        {
            foreach (Process clsProcess in Process.GetProcesses())
            {
                string strProcessName = clsProcess.ProcessName;
                //WriteLineToLogFile(strProcessName);
                if (clsProcess.ProcessName.ToUpperInvariant().Equals(name.ToUpperInvariant()))
                {
                    return clsProcess;
                }
            }
            return null;
        }
        public static string BitnessString
        {
            get
            {
                return (isRunningIn32BitMode) ? "X86" : "X64";
            }
        }

        // Cloning
        public static object DeepClone(object obj)
        {
            object objResult = null;
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, obj);

                ms.Position = 0;
                objResult = bf.Deserialize(ms);
            }
            return objResult;
        }

        public static Version ServerVersion
        {
            get
            {
                Version v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return v;
            }
        }



        #region Folder Names
        public static string AppDataFolder
        {
            get
            {
                string dirPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\" + "RemotePotato";
                if (!Directory.Exists(dirPath))
                {
                    try
                    {
                        Directory.CreateDirectory(dirPath);
                    }
                    catch 
                    {
                        // Can't log anywhere!  Eek.
                        File.Create(@"C:\RemotePotatoPanicCouldNotCreateDataDirectory.txt");
                    }
                }
                return dirPath;
            }            
        }
        public static string SkinFolder
        {
            get
            {
                string ADF = AppDataFolder;
                return Path.Combine(ADF, "static\\skins");
            }
        }
        public static string StreamBaseFolder
        {
            get
            {
                string ADF = AppDataFolder;
                return Path.Combine(ADF, "static\\mediastreams\\");
            }
        }
        public static string AppInstallFolder
        {
            get
            {
                string loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                return System.IO.Path.GetDirectoryName(loc);
//                    System.Reflection.Assembly.GetExecutingAssembly.GetModules()[0].FullyQualifiedName    
            }
        }
        public static string ToolkitFolder
        {
            get
            {
                return Path.Combine(AppInstallFolder, "toolkit");
            }
        }
        public static string ZipTempFolder
        {
            get
            {
                string ADF = AppDataFolder;
                return Path.Combine(ADF, "ziptemp");
            }
        }
        #endregion


        // Mime Type
        public static string MimeTypeForFileName(string localFilePath)
        {
            if (localFilePath.ToLower().EndsWith("jpg"))
                return "image/jpeg";
            else if (localFilePath.ToLower().EndsWith("png"))
                return "image/png";
            else if (localFilePath.ToLower().EndsWith("gif"))
                return "image/gif";
            else if (localFilePath.ToLower().EndsWith("bmp"))
                return "image/bmp";
            else if (localFilePath.ToLower().EndsWith("css"))
                return "text/css";
            else if (localFilePath.ToLower().EndsWith("htm"))
                return "text/html";
            else if (localFilePath.ToLower().EndsWith("html"))
                return "text/html";
            else if (localFilePath.ToLower().EndsWith("mp3"))
                return "audio/mpeg";
            else if (localFilePath.ToLower().EndsWith("wma"))
                return "audio/x-ms-wma";
            else if (localFilePath.ToLower().EndsWith("xap"))
                return "application/x-silverlight-2";
            else if (localFilePath.ToLower().EndsWith("zip"))
                return "application/zip";
            else if (localFilePath.ToLower().EndsWith("xml"))
                return "text/xml";
            else
                return "image/unknown";
        }

        // Streaming pack
        public static bool IsStreamingPackInstalled
        {
            get
            {
       
            try
            {
                RegistryKey rkRecPath = Registry.CurrentUser.OpenSubKey("SOFTWARE\\FatAttitude\\RemotePotatoStreamingPack\\", false);
                string sValue = (string)rkRecPath.GetValue("Installed", "False");
                return (sValue.ToLowerInvariant().Equals("true"));
            }
            catch { }
            return false;
            }
        }
        public static int StreamingPackBuild
        {
            get
            {

                try
                {
                    RegistryKey rkRecPath = Registry.CurrentUser.OpenSubKey("SOFTWARE\\FatAttitude\\RemotePotatoStreamingPack\\", false);
                    return (int)rkRecPath.GetValue("Build", false);
                }
                catch { }
                return 0;
            }
        }

        // Object Copy
        public static void Copy<T, U>(ref T source, ref U target)
        {
            Type sourceType = source.GetType();
            Type targetType = target.GetType();
            PropertyInfo[] sourceProperties = sourceType.GetProperties();
            PropertyInfo[] targetProperties = targetType.GetProperties();

            foreach (PropertyInfo tp in targetProperties)
            {
                foreach (PropertyInfo sp in sourceProperties)
                {
                    if (tp.Name == sp.Name)
                        tp.SetValue(target, sp.GetValue(source, null), null);
                }
            }
        }

        // Strings
        #region Base64
        #endregion

        // HTML
       public static string DivTag(string ofClass)
       {
           return "<div class=\"" + ofClass + "\">";
       }
       public static string LinkTagOpen(string href)
       {
            return LinkTagOpen(href, null);
       }
       public static string LinkTagOpen(string href, string target)
       {
           string targetString = (target != null) ? " target=\"" + target + "\"" : "";
           return "<a href=\"" + href + "\"" + targetString + ">";
       }
       public static string LinkConfirmClick(string txtMessage)
        {
            return " onclick=\"javascript:return confirm('" + txtMessage + "')\" ";
        }
       public static string imgTag(string src, string ofClass)
        {
            return "<img src=\"" + src + "\" class=\"" + ofClass + "\" />";
        }
        // IO
       public static void ShowExplorerFolder(string folder)
        {
            return;
        }

       /// <summary>
        /// Decrypt a crypted string.
        /// </summary>
        /// <param name="cryptedString">The crypted string.</param>
        /// <returns>The decrypted string.</returns>
        /// <exception cref="ArgumentNullException">This exception will be thrown 
        /// when the crypted string is null or empty.</exception>
       public static string DecryptString(string cryptedString)
        {
            if (String.IsNullOrEmpty(cryptedString))
            {
                throw new ArgumentNullException
                   ("The string which needs to be decrypted can not be null.");
            }

            byte[] theKey = Convert.FromBase64String("4yELBlvHTII=;");

            DESCryptoServiceProvider cryptoProvider = new DESCryptoServiceProvider();
            MemoryStream memoryStream = new MemoryStream
                    (Convert.FromBase64String(cryptedString));
            CryptoStream cryptoStream = new CryptoStream(memoryStream,
                cryptoProvider.CreateDecryptor(theKey, theKey), CryptoStreamMode.Read);
            StreamReader reader = new StreamReader(cryptoStream);
            return reader.ReadToEnd();
        }
       public static string DecryptBinaryFile(string fileName)
        {
            if (!File.Exists(fileName))
            {
                throw new ArgumentNullException
                   ("The string which needs to be decrypted can not be null.");
            }
            
            FileStream fs = File.OpenRead(fileName);
            BinaryReader br = new BinaryReader(fs); 

            byte[] theKey = Convert.FromBase64String("4yELBlvHTII=;"); // was a MS copyright str

            DESCryptoServiceProvider cryptoProvider = new DESCryptoServiceProvider();
           // MemoryStream memoryStream = new MemoryStream(Convert.FromBase64String(cryptedString));
            CryptoStream cryptoStream = new CryptoStream(fs, cryptoProvider.CreateDecryptor(theKey, theKey), CryptoStreamMode.Read);
            StreamReader reader = new StreamReader(cryptoStream);
            return reader.ReadToEnd();
        }


        public static double ProbeDuration(string video, string workdir)
        {
           // return DurationOfMediaFile_OSSpecific(video, workdir);
            double durationRead=0;
                        Process fprobe = new Process();
            fprobe.StartInfo.CreateNoWindow = true;
            fprobe.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            fprobe.StartInfo.RedirectStandardOutput = true;
            fprobe.StartInfo.RedirectStandardError = false;
            string shortProbeFN = Functions.FileWriter.GetShortPathName(Path.Combine(ToolkitFolder, "ffprobelatest.exe"));
            fprobe.StartInfo.FileName = "\"" + shortProbeFN + "\"";
            fprobe.StartInfo.WorkingDirectory = workdir;
            fprobe.StartInfo.UseShellExecute = false;
            //                fprobe.StartInfo.Arguments = "-show_format -loglevel quiet \"" + VideoToMeasureDurationNextPart + ".wtv\"";
            fprobe.StartInfo.Arguments = "-show_format -loglevel quiet " + " " + video;

            fprobe.Start();
            fprobe.WaitForExit();
            string str = fprobe.StandardOutput.ReadToEnd();

            List<string> outputBuffer = splitByDelimiter(str, Environment.NewLine);
            string dur = "";
            foreach (string s in outputBuffer)
            {
                string txtOutput = s.ToLowerInvariant();
                if (txtOutput.StartsWith("duration"))
                {
                    dur = txtOutput.Replace("duration=", "");
                    if (!dur.StartsWith("n/a"))
                        durationRead = Double.Parse(dur, CultureInfo.InvariantCulture);
                }
            }

            return durationRead;
        }

        public static double DurationOfMediaFile_OSSpecific(string FN, string workdir)
        {
            //if (parameters.LiveTV) return new TimeSpan(1, 0, 0, 0);

            CreateShellHelperIfNull();

            if (Functions2.OSSupportsMediaDurationInShell)
            {
                TimeSpan tryGetTime = sHelper.DurationOfMediaFile(FN); // Use shell
                if (tryGetTime.Ticks > 0)
                    return tryGetTime.TotalSeconds;
            }

            // ELSE use FFMPEG
            return GetMediaDuration(FN, workdir);  // Use FFMPEGlatest
        }
        static FatAttitude.ShellHelper sHelper;
        static void CreateShellHelperIfNull()
        {
            if (sHelper == null)
                sHelper = new FatAttitude.ShellHelper();
        }
        public static double GetMediaDuration(string fileName, string workdir)
        {
            MediaInfoGrabber grabber = new MediaInfoGrabber(Functions2.ToolkitFolder, Path.Combine(Functions2.StreamBaseFolder, "probe_results"), fileName);
            grabber.GetInfo("ffmpeglatest.exe", workdir);//handles m3u8 as well

            return grabber.Info.NewLiveTVPartDuration;

        }

        /// <summary>
        /// Split a string by a delimiter, remove blank lines and trim remaining lines
        /// </summary>
        /// <param name="strText"></param>
        /// <param name="strDelimiter"></param>
        /// <returns></returns>
        public static List<String> splitByDelimiter(string strText, string strDelimiter)
        {
            string[] strList = strText.Split(strDelimiter.ToCharArray());
            List<string> strOutput = new List<string>();
            foreach (string s in strList)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                strOutput.Add(s.Trim());
            }

            return strOutput;
        }



    }

    public static class SegmentTabel
    {
        //private static Dictionary<Int64, string> dictionary = new Dictionary<Int64, string>(); 
        private static Dictionary<string, Int64> reversedictionary = new Dictionary<string,Int64>(); //performance is an issue, memory hopefully not so use 2 dictionarie and keep them in sync

        public static int getSegment(int index, int segment)
        {
//            Int64 key = index * 43200 + segment; // 43200 seconds is 7 hours, won't get a segment greater than 7 hours so this seems safe
 //           List<string> consecutivesegmentnrs= Functions2.splitByDelimiter(dictionary[key],":"); //not interested in ID
            //return Int32.Parse(consecutivesegmentnrs[1]);
            return segment;
        }
    
        public static int getIndex(string ID, int consecutivesegmentnr)
        {
            if (consecutivesegmentnr < 0) return -1;
            try
            {
                return (int)reversedictionary[ID + ":" + consecutivesegmentnr];
            }
            catch (Exception) //key not available in dictionary
            {
                return -1;
            }
        }

        public static void setSegment (string ID, int consecutivesegnr, int index) //key should have format ID:segmentnr
        {
            string reeks = ID + ":" + consecutivesegnr;
            try
            {
                //dictionary.Add(index * 43200 + segment, reeks);
                reversedictionary.Add(reeks, index);
            }
            catch (Exception e)
            {
                Console.WriteLine("probably the runner has restarted for the same ID generating segments anew starting from 0-1, 1-1 etc. "+e);
            }
        }

        public static void clearAll()
        {
            //dictionary.Clear();
            reversedictionary.Clear();
        }

        public static void clear(string ID)
        {
            for (int i = 1; i < reversedictionary.Count; i++)
            {
                reversedictionary.Remove(ID+":"+i);
            }
            //foreach (KeyValuePair<Int64, string> entry in dictionary)
            //{
            //    if (Functions2.splitByDelimiter(entry.Value, ":")[0].Equals(ID))
            //        dictionary.Remove(entry.Key);
            //}
        }
    }
}

