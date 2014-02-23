using System;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Web;
using System.Linq;
using System.Timers;
using FatAttitude;
using FatAttitude.MediaStreamer;
using FatAttitude.Functions;
using RemotePotatoServer.Properties;
using System.Threading;
using System.Collections;
using System.Data;
using System.Data.SqlClient;

namespace RemotePotatoServer
{
    public sealed class StreamingManager
    {
        Dictionary<int, MediaStreamer> mediaStreamers;
        System.Timers.Timer JanitorTimer;

        StreamingManager()
        {
            // Set up streamers
            mediaStreamers = new Dictionary<int, MediaStreamer>();

            // Delete all streaming files
            DeleteAllStreamingFiles();

            InitJanitor();
        }
        public void CleanUp()
        {
            Functions.WriteLineToLogFile("StreamingManager: Cleaning Up.");
            StopAllStreamers();
        }
        public void StopAllStreamers()
        {
            List<MediaStreamer> streamersToStop = new List<MediaStreamer>();
            foreach (MediaStreamer ms in mediaStreamers.Values)
            {
                streamersToStop.Add(ms);
            }

            foreach (MediaStreamer ms in streamersToStop)
            {
                StopStreamer(ms.ID, 1);
            }
        }

        #region Manage Streamers
        public MediaStreamer GetStreamerByID(int id)
        {
            if (mediaStreamers.ContainsKey(id))
                return mediaStreamers[id];
            else
                return null;
        }
        void AddNewStreamer(MediaStreamer newStreamer)
        {
            mediaStreamers.Add(newStreamer.ID, newStreamer);

            if (newStreamer.Request.OnlyTranscodeOnServer)
            {
                List<MediaStreamingRequest> msrlist = MediaStreamer.DeserializeTranscodedMSRFromXML();
                List<MediaStreamingRequest> msrlist2 = new List<MediaStreamingRequest>();
                for (int i = 0; i < msrlist.Count; i++)
                {
                    MediaStreamingRequest msr = msrlist[i];
                    if (msr.InputFile.Equals(newStreamer.Request.InputFile) && msr.UniekClientID.Equals(newStreamer.Request.UniekClientID))
                    {

                    }
                    else
                    {
                        msrlist2.Add(msr);
                    }
                }
                msrlist2.Add(newStreamer.Request);
                MediaStreamer.SerializeTranscodedMSRToXML(msrlist2);
            }

            // Power options
            SetPowerOptions();
        }
        public MediaStreamingRequest GetMediaStreamingRequest(int ID)
        {
            MediaStreamer ms = mediaStreamers[ID];
            return ms.Request;
        }

        public int newUniqueID(MediaStreamingRequest request, bool getIDOnly, bool dontstart, out MediaStreamingResult OldMsr)
        {
            if (dontstart)
            {
                OldMsr = new MediaStreamingResult();
                string databaseFile;
                List<MediaStreamingResult> msrs = FromFile(out databaseFile);
                return MediaStreamingResultsContains(msrs, request.InputFile, request.UniekClientID);//it must exist because this function only gets called when exist in deserialized MediaSteramRequest
            }
            else
            {
                return newUniqueID(request, getIDOnly, out OldMsr);
            }
        }

        object lockdatab = new object();

        public int newUniqueID(MediaStreamingRequest request, bool getIDOnly, out MediaStreamingResult OldMsr)
        {

            OldMsr = new MediaStreamingResult();
            int newId;
            lock (lockdatab)
            {
                string databaseFile;
                List<MediaStreamingResult> storedStreamingResults = FromFile(out databaseFile);

                int index =
                    storedStreamingResults.FindIndex(
                        item => (item.InputFile == request.InputFile && item.UniekClientID == request.UniekClientID));
                newId = (index == -1 ? 0 : storedStreamingResults[index].StreamerID);

                if (request.KeepSameIDForSameInputFile && newId != 0) // found as existing id that can be resumed
                {
                    var ms = GetStreamerByID(newId);
                    OldMsr = storedStreamingResults[index];
                    if (ms != null && !getIDOnly)
                    {
                        //mediaStreamers.Remove(newId);
                        Functions.WriteLineToLogFile("Streamer newId=" + newId + " about to stop (in background), mediaStreamers.ContainsKey(newId) : " +
                                                     mediaStreamers.ContainsKey(newId));
                        StopStreamer(newId, 2);
                        Functions.WriteLineToLogFile("Streamer newId=" + newId + " stopped (in background), mediaStreamers.ContainsKey(newId) : " +
                                                     mediaStreamers.ContainsKey(newId));
                    }

                    if (!getIDOnly) DeleteStreamingFiles(newId); //begin anew, s.t. if new quality, this will be used.

                    //bump up the id in the database, so it does not get cleaned up when janitor cleans half of the database
                    setMSRInFront(storedStreamingResults, index, databaseFile);

                    //RequestProcessor.segstarts.Remove(newId); //forget about seekpoints (at segment) where the stream should start, when new ID has been asked for
                }
                else
                {
                    do
                    {
                        var r = new Random();
                        //newId = (getIDOnly ? r.Next(100000, 999999) : r.Next(10000, 99999));
                        newId = r.Next(10000, 99999);
                    } while (mediaStreamers.ContainsKey(newId) || MediaStreamingResultsContains(storedStreamingResults, newId) != 0);

                    //if (MediaStreamingResultsContains(storedStreamingResults, request.InputFile, request.UniekClientID) == 0) //Not available in database yet
                    //    //BTW live tv gets a new iD all the time anyway, due to randowm nr in inputfile string
                    //{
                    MediaStreamingResult msr = new MediaStreamingResult();
                    msr.InputFile = request.InputFile;
                    msr.UniekClientID = request.UniekClientID; // this is the user for whom we wanna keep track the current reusumepoints etc.
                    msr.ResumePoint = 0;
                    msr.StreamerID = newId;
                    msr.AudioTrack = request.UseAudioStreamIndex;
                    msr.UseSubtitleStreamIndex = request.UseSubtitleStreamIndex;
                    storedStreamingResults.Add(msr);
                    //bump up the id in the database, so it does not get cleaned up when janitor process below cleans half of the database
                    MediaStreamingResult[] o = storedStreamingResults.ToArray();
                    MoveToFront<MediaStreamingResult>(o, storedStreamingResults.Count - 1);
                    storedStreamingResults = o.ToList();
                    storeFile(storedStreamingResults, databaseFile);
                    //}


                    //Since something has been added, look to clean up if database is large
                    const int maxFileToRememberResume = 100;
                    int count = 0;
                    if (storedStreamingResults.Count > maxFileToRememberResume)
                    {
                        //RequestProcessor.segstarts = new Dictionary<int, int>();//because this defined as static, it needs to be cleaned up as well
                        try
                        {
                            List<MediaStreamingResult> overview2 = new List<MediaStreamingResult>();
                            for (int i = 0; i <= maxFileToRememberResume / 2; i++)
                            {
                                overview2.Add(storedStreamingResults[i]);
                            }
                            //now reverse the order
                            //List<MediaStreamingResult> msrs = new List<MediaStreamingResult>();
                            //for (int i = overview2.Count-1; i >= 0; i--)
                            //{
                            //    msrs.Add(overview2[i]);
                            //}
                            System.Xml.Serialization.XmlSerializer writer = new System.Xml.Serialization.XmlSerializer(typeof(List<MediaStreamingResult>));
                            System.IO.StreamWriter fileOut = new System.IO.StreamWriter(databaseFile);
                            writer.Serialize(fileOut, overview2);
                            fileOut.Close();
                        }
                        catch (Exception fail)
                        {
                            String error = "The following error has occurred in cleaning up MediaStermingResults database \n";
                            error += fail.Message;
                            if (Settings.Default.DebugStreaming)
                                Functions.WriteLineToLogFile("StreamingManager: " + error);
                        }
                    }

                }
            }
            //RequestProcessor.segstarts.Remove(newId); //forget about seekpoints (at segment) where the stream should start, when new ID has been asked for, should this logic be removed???
            return newId;
        }

        public void store(MediaStreamingResult msr)
        {
            //MediaStreamer ms = GetStreamerByID(msr.StreamerID);
            string databaseFile;
            lock (lockdatab)
            {
                List<MediaStreamingResult> storedStreamingResults = FromFile(out databaseFile);
                MediaStreamingResult msr2 =
                    storedStreamingResults.FirstOrDefault(
                        item => (item.StreamerID == msr.StreamerID));  //a user (uniqeuandroidid) has a separate streamerID anyway

                msr2.ResumePoint = msr.ResumePoint;
                msr2.AudioTrack = msr.AudioTrack;
                msr2.InputFile = msr.InputFile;
                msr2.UseSubtitleStreamIndex = msr.UseSubtitleStreamIndex;


                MediaStreamingResult[] msrs = storedStreamingResults.ToArray();
                bool found = false;
                int index = 0;
                foreach (MediaStreamingResult m in msrs)
                {
                    if (msr.StreamerID == m.StreamerID)
                    {
                        found = true;
                        break;
                    }
                    index++;
                }
                if (found)
                {
                    msrs[index] = msr2;
                }
                else
                {
                    throw new ArgumentOutOfRangeException();
                }
                MoveToFront<MediaStreamingResult>(msrs, index);
                storeFile(msrs.ToList(), databaseFile);
            }
        }

        private List<MediaStreamingResult> FromFile(out string databaseFile)
        {
            System.Xml.Serialization.XmlSerializer reader =
    new System.Xml.Serialization.XmlSerializer(typeof(List<MediaStreamingResult>));
            string rppath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "remotepotato");
            databaseFile = Path.Combine(rppath, "settings\\MediaStreamingResults.xml");
            if (!File.Exists(databaseFile))
            {
                return new List<MediaStreamingResult>();
            }
            System.IO.StreamReader file = new System.IO.StreamReader(databaseFile);
            List<MediaStreamingResult> overview = new List<MediaStreamingResult>();
            overview = (List<MediaStreamingResult>)reader.Deserialize(file);
            file.Close();
            return overview;
        }
        private static bool MoveToFront<T>(T[] mos, int idx)
        {
            if (mos.Length == 0)
            {
                return false;
            }
            if (idx == -1)
            {
                return false;
            }
            var tmp = mos[idx];
            Array.Copy(mos, 0, mos, 1, idx);
            mos[0] = tmp;
            return true;
        }

        public void SetTimeForIndexFile(int StreamerID, int SegmentNr)
        {
            string databaseFile;
            lock (lockdatab)
            {
                List<MediaStreamingResult> msrs = FromFile(out databaseFile);
                MediaStreamingResult msr =
                    msrs.FirstOrDefault(
                        item => (item.StreamerID == StreamerID));
                if (msr == null)
                    return;
                else
                {
                    MediaStreamer ms = GetStreamerByID(StreamerID);
                    msr.ResumePoint = SegmentNr * 1000 * ms.Request.ActualSegmentDuration;
                }
                storeFile(msrs, databaseFile);
            }
        }

        public int GetTimeForIndexFile(int StreamerID)
        {
            string databaseFile;
            List<MediaStreamingResult> msrs = FromFile(out databaseFile);
            MediaStreamingResult msr =
                msrs.FirstOrDefault(
                    item => (item.StreamerID == StreamerID));
            if (msr == null)
                return 0;
            else
            {
                MediaStreamer ms = GetStreamerByID(StreamerID);
                return (int)(msr.ResumePoint / 1000 / ms.Request.ActualSegmentDuration);
            }
        }

        public int MediaStreamingResultsContains(string InputFile)
        {
            string databaseFile;
            List<MediaStreamingResult> msrs = FromFile(out databaseFile);
            int uit = 0;
            int index;
            try
            {
                index =
                    msrs.FindIndex(
                        item => (item.InputFile == InputFile));
                uit = (index == -1 ? 0 : msrs[index].StreamerID);
            }
            catch (ArgumentNullException e)
            {
                uit = 0;
            }
            return uit;
        }

        public bool LiveTVStarted(int ID)
        {
            string databaseFile;
            List<MediaStreamingResult> msrs = FromFile(out databaseFile);
            bool isStarted = false;
            int index = -1;
            try
            {
                index =
                    msrs.FindIndex(
                        item => (item.StreamerID == ID));
                if (index != -1)
                {
                    if (msrs[index].LiveTVStarted)
                    {
                        isStarted = true;
                    }
                    else
                    {
                        msrs[index].LiveTVStarted = true;
                        storeFile(msrs, databaseFile);
                    }
                }
            }
            catch (ArgumentNullException e)
            {
                isStarted = false;
            }
            return isStarted;
        }

        public int MediaStreamingResultsContains(List<MediaStreamingResult> MSRS, string InputFile, string UniekClientID)
        {
            int uit = 0;
            int index;
            try
            {
                index =
                    MSRS.FindIndex(
                        item => (item.InputFile == InputFile && item.UniekClientID == UniekClientID));
                uit = (index == -1 ? 0 : MSRS[index].StreamerID);
            }
            catch (ArgumentNullException e)
            {
                uit = 0;
            }
            return uit;
        }
        private void setMSRInFront(List<MediaStreamingResult> overview, int index, String databaseFile)
        {
            //bump up the id in the database, so it does not get cleaned up when janitor cleans half of the database
            MediaStreamingResult[] o = overview.ToArray();
            MoveToFront<MediaStreamingResult>(o, index);
            overview = o.ToList();
            System.Xml.Serialization.XmlSerializer writer = new System.Xml.Serialization.XmlSerializer(typeof(List<MediaStreamingResult>));
            System.IO.StreamWriter fileOut = new System.IO.StreamWriter(databaseFile);
            writer.Serialize(fileOut, overview);
            fileOut.Close();
        }
        private void storeFile(List<MediaStreamingResult> storedStreamingResults, string databaseFile)
        {
            System.Xml.Serialization.XmlSerializer writer = new System.Xml.Serialization.XmlSerializer(typeof(List<MediaStreamingResult>));
            System.IO.StreamWriter fileOut = new System.IO.StreamWriter(databaseFile);
            writer.Serialize(fileOut, storedStreamingResults);
            fileOut.Close();
        }

        private int MediaStreamingResultsContains(List<MediaStreamingResult> storedStreamingResults, int ID)
        {
            try
            {
                MediaStreamingResult msr = storedStreamingResults.First(
                        item => (item.StreamerID == ID));
                return msr.StreamerID;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        void RemoveStreamer(int id)
        {
            MediaStreamer ms = GetStreamerByID(id);
            if (ms != null)
                mediaStreamers.Remove(id);

            // Power options
            SetPowerOptions();

#if ! DEBUG
            // Delete the streaming files.  If there are no streamers left, delete all streaming files
            if (mediaStreamers.Count > 0)
                DeleteStreamingFiles(id);
            else
                DeleteAllStreamingFiles();
#endif
        }
        void SetPowerOptions()
        {
            if (Settings.Default.DebugStreaming)
                Functions.WriteLineToLogFile("StreamingManager: nr of mediastreamers=" + mediaStreamers.Count);
            if (mediaStreamers.Count > 0)
            {
                if (Settings.Default.DebugStreaming)
                    Functions.WriteLineToLogFile("StreamingManager: so preventing standby");
                PowerHelper.PreventStandby();
            }
            else
            {
                if (Settings.Default.DebugStreaming)
                    Functions.WriteLineToLogFile("StreamingManager: so allowing standby");
                PowerHelper.AllowStandby();
            }
        }
        public void DeleteAllBackgroundTranscodedStreamingFiles()
        {
            try
            {
                Directory.Delete(Functions.BackgroundStreamBaseFolder, true);
                string rppath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "remotepotato");
                string MSRFile = Path.Combine(rppath, "settings\\transcodedmsr.xml");
                File.Delete(MSRFile);
            }
            catch (Exception e)
            {
                if (Settings.Default.DebugStreaming)
                    Functions.WriteLineToLogFile("StreamingManager: " + e.ToString());
            }

        }
        void DeleteAllStreamingFiles()
        {
            try
            {
                Directory.Delete(Functions.StreamBaseFolder, true);
            }
            catch { }
            SegmentTabel.clearAll();
        }
        void DeleteStreamingFiles(int id)
        {
            try
            {
                string OutputBasePath = Path.Combine(Functions.StreamBaseFolder, id.ToString());
                Directory.Delete(OutputBasePath, true);
            }
            catch { }
            SegmentTabel.clear("" + id);
        }
        // Janitor, sweep up ancient streamers that may have failed
        void InitJanitor()
        {
            JanitorTimer = new System.Timers.Timer(3600000);//one hour
            JanitorTimer.AutoReset = true;
            JanitorTimer.Elapsed += new ElapsedEventHandler(JanitorTimer_Elapsed);
            JanitorTimer.Start();
        }

        void JanitorTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            JanitorSweepUp();
        }
        void JanitorSweepUp()
        {
            if (Settings.Default.DebugAdvanced)
                Functions.WriteLineToLogFile("MediaStream Janitor:  Checking for old streamers.");

            List<int> deletions = new List<int>();

            foreach (MediaStreamer ms in mediaStreamers.Values)
            {
                TimeSpan ts = DateTime.Now.Subtract(ms.CreationDate);

                if (ts.TotalHours > 12) // more than twelve hours old, cuz LiveTv has a maximum of 12 hours
                {
                    Functions.WriteLineToLogFile("MediaStream Janitor:  Sweeping up streamer " + ms.ID.ToString() + " which is " + ts.TotalHours.ToString() + " old.");
                    deletions.Add(ms.ID);
                }
            }

            // Prune old streamers
            foreach (int i in deletions)
            {
                StopStreamer(i, 3);  // This stops and also removes it
            }
        }
        #endregion



        /// <summary>
        /// Legacy for older iOS clients
        /// </summary>
        /// <param name="streamerID"></param>
        /// <returns></returns>
        public string KeepStreamerAliveAndReturnStatus(int streamerID)
        {
            MediaStreamer mediaStreamer = GetStreamerByID(streamerID);
            if (mediaStreamer == null) return "disposed";


            return "streamavailable";  // stream is always available now

        }
        /// <summary>
        /// Stop a streamer and remove it from the local list of streamers
        /// </summary>
        /// <param name="streamerID"></param>
        /// <returns></returns>
        public bool StopStreamer(int streamerID, int debugComingFrom)
        {
            if (Settings.Default.DebugStreaming)
                Functions.WriteLineToLogFile("StreamingManager: Received stop command for streamer " + streamerID.ToString() + " coming from " + debugComingFrom);
            try
            {

                MediaStreamer mediaStreamer = GetStreamerByID(streamerID);
                if (mediaStreamer == null) return false;

                // Abort streamer (on a different thread)
                AbortMediaStreamerAndDeleteFiles((object)mediaStreamer);
                /*
                System.Threading.ParameterizedThreadStart ts = new System.Threading.ParameterizedThreadStart(AbortMediaStreamerAndDeleteFiles);
                System.Threading.Thread t_abortStreamer = new System.Threading.Thread(ts);
                t_abortStreamer.Start(mediaStreamer); */

                // Remove from streamers
                RemoveStreamer(streamerID);

                return true;
            }
            catch (Exception ex)
            {
                Functions.WriteExceptionToLogFileIfAdvanced(ex);
            }
            return false;
        }
        void AbortMediaStreamerAndDeleteFiles(object obj)
        {
            try
            {
                MediaStreamer ms = (MediaStreamer)obj;
                ms.AbortStreaming(true);
            }
            catch (Exception ex)
            {
                // Must catch exceptions on other threads
                Functions.WriteExceptionToLogFileIfAdvanced(ex);
            }
        }
        public MediaStreamingResult StartStreamer(MediaStreamingRequest request, string HostName, bool dontstart, out bool alreadyexists)
        {
            MediaStreamingResult oldeMSR;
            int newStreamerID = newUniqueID(request, false, dontstart, out oldeMSR);

            // Universal workaround: can be removed once new iOS app introduced that sets the Client Device to 'iphone3g'
            // (desirable to remove it since this will also affect silverlive streaming)
            if (string.IsNullOrEmpty(request.ClientID))
            {
                request.ClientID = "ios";
                request.ClientDevice = "iphone3g";
            }

            try
            {


                // Legacy clients (e.g. iOS client) don't have any custom parameters - set them now based on 'Quality'
                if (!request.UseCustomParameters) // if there are no custom parameters
                {
                    // Create/update video encoding parameters (also transfers Aspect Ratio into child 'encoding parameters' object)
                    MediaStreamingRequest.AddVideoEncodingParametersUsingiOSQuality(ref request);
                }

                /* ************************************************************
                // Override any video encoding parameters from server settings
                 ************************************************************ */
                // 1. Audio Volume
                if (Settings.Default.StreamingVolumePercent != 100)
                    request.CustomParameters.AudioVolumePercent = Convert.ToInt32(Settings.Default.StreamingVolumePercent);

                // 2. Custom FFMPEG template
                if ((Settings.Default.UseCustomFFMpegTemplate) & (!string.IsNullOrWhiteSpace(Settings.Default.CustomFFMpegTemplate)))
                    request.CustomParameters.CustomFFMpegTemplate = Settings.Default.CustomFFMpegTemplate.Trim();

                // 3. iPhone 3G requires profile constraints
                if (request.ClientDevice.ToLowerInvariant() == "iphone3g")
                {
                    request.CustomParameters.X264Level = 30;
                    request.CustomParameters.X264Profile = "baseline";
                }

                // 4. Deinterlace obvious WMC video
                if (
                    (request.InputFile.ToUpper().EndsWith("WTV")) ||
                    (request.InputFile.ToUpper().EndsWith("DVR-MS"))
                    )
                {
                    request.CustomParameters.DeInterlace = true;
                }

                // for liveTV resolve AV sync issue:
                //LiveTVHelpers lth = new LiveTVHelpers();
                //request.CustomParameters.AVSyncDifference = lth.GetMediaSyncDifference(Functions.ToolkitFolder, Functions.StreamBaseFolder, request.InputFile); //for LiveTV
                // Create the streamer
                MediaStreamer mediaStreamer = new MediaStreamer(newStreamerID, request, Functions.ToolkitFolder, Settings.Default.MediaStreamerSecondsToKeepAlive, Settings.Default.DebugAdvancedStreaming);
                mediaStreamer.DebugMessage += new EventHandler<WasFatAttitude.GenericEventArgs<string>>(mediaStreamer_DebugMessage);
                mediaStreamer.DebugMessage2 += new EventHandler<WasFatAttitude.GenericEventArgs<string>>(mediaStreamer_DebugMessage2);
                mediaStreamer.DebugMessage3 += new EventHandler<WasFatAttitude.GenericEventArgs<string>>(mediaStreamer_DebugMessage3);
                mediaStreamer.DebugMessage4 += new EventHandler<WasFatAttitude.GenericEventArgs<string>>(mediaStreamer_DebugMessage4);

                mediaStreamer.AutoDied += new EventHandler(mediaStreamer_AutoDied);

                MediaStreamingResult result = new MediaStreamingResult();
                try
                {
                    AddNewStreamer(mediaStreamer);
                    Functions.WriteLineToLogFile("MediaStreamer: mediaStreamer object created.");
                    alreadyexists = false;
                    // Try streaming
                    result = mediaStreamer.Configure();  // this does actually begin transcoding
                }
                catch (Exception e)
                {
                    //apparently key already existed
                    alreadyexists = true;
                }


                if (request.NewLiveTV)
                {
                    result.LiveStreamingIndexPath = "/httplivestream/" + newStreamerID.ToString() + "/livetv.m3u8";
                }
                else if (request.UseNewerFFMPEG && !request.LiveTV)
                {
                    result.LiveStreamingIndexPath = "/httplivestream/" + newStreamerID.ToString() + "/index.m3u8"; //"/index2.m3u8";
                }
                else
                {
                    result.LiveStreamingIndexPath = "/httplivestream/" + newStreamerID.ToString() + "/index.m3u8";
                }

                // Add streamer ID to result
                result.StreamerID = newStreamerID;

                result.ResumePoint = oldeMSR.ResumePoint;
                result.UseSubtitleStreamIndex = oldeMSR.UseSubtitleStreamIndex;
                result.AudioTrack = oldeMSR.AudioTrack;

                // Return
                return result;
            }
            catch (Exception e)
            {
                Functions.WriteLineToLogFile("Exception setting up mediaStreaming object:");
                Functions.WriteExceptionToLogFile(e);
                alreadyexists = false;
                return new MediaStreamingResult(MediaStreamingResultCodes.NamedError, e.Message);
            }
        }

        /// <summary>
        /// Raised by a streamer after around 10 minutes of inactivity when it auto dies
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void mediaStreamer_AutoDied(object sender, EventArgs e)
        {

            MediaStreamer ms = (MediaStreamer)sender;

            if (Settings.Default.DebugStreaming)
                Functions.WriteLineToLogFile("StreamingManager: Received notification that streamer " + ms.ID.ToString() + " auto-died.");

            RemoveStreamer(ms.ID);


        }
        void mediaStreamer_DebugMessage(object sender, WasFatAttitude.GenericEventArgs<string> e)
        {
            if (Settings.Default.DebugStreaming)
            {
                Functions.WriteLineToLogFile("MediaStreamer: " + e.Value);
            }
        }
        void mediaStreamer_DebugMessage2(object sender, WasFatAttitude.GenericEventArgs<string> e)
        {
            if (Settings.Default.DebugStreaming)
            {
                Functions.WriteLineToLogFile2("MediaStreamer: " + e.Value);
            }
        }
        void mediaStreamer_DebugMessage3(object sender, WasFatAttitude.GenericEventArgs<string> e)
        {
            if (Settings.Default.DebugStreaming)
            {
                Functions.WriteLineToLogFile3("MediaStreamer: " + e.Value);
            }
        }
        void mediaStreamer_DebugMessage4(object sender, WasFatAttitude.GenericEventArgs<string> e)
        {
            if (Settings.Default.DebugStreaming)
            {
                Functions.WriteLineToLogFile4("MediaStreamer: " + e.Value);
            }
        }

        public void DeleteBackgroundSegmentData(int streamerID)
        {
            string rpPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RemotePotato");
            string destinationDirectory = Path.Combine(rpPath, "static\\BackgroundTranscodedMediastreams\\");
            destinationDirectory = Path.Combine(destinationDirectory, streamerID.ToString());
            if (Directory.Exists(destinationDirectory)) Directory.Delete(destinationDirectory, true);
        }

        public void MoveSegmentData(int streamerID)
        {
            string rpPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RemotePotato");
            string WorkingDirectory = Path.Combine(rpPath, "static\\mediastreams\\" + streamerID.ToString());
            string destinationDirectory = Path.Combine(rpPath, "static\\BackgroundTranscodedMediastreams\\");
            if (!Directory.Exists(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
            destinationDirectory = Path.Combine(destinationDirectory, streamerID.ToString());
            if (Directory.Exists(destinationDirectory)) Directory.Delete(destinationDirectory, true);
            while (true)
            {
                try
                {
                    Directory.Move(WorkingDirectory, destinationDirectory);
                    Functions.WriteLineToLogFile2("StreamingManager: moved streaminfo to " + destinationDirectory);
                    break;
                }
                catch (Exception e)
                {
                    Thread.Sleep(100);
                    Functions.WriteLineToLogFile(e.ToString() + "\n trying to move again");
                }
            }
        }

        #region Retrieve Segments from Streamer
        public bool SegmentFromStreamer(int index, int streamerID, int segmentNumber, ref byte[] Data, ref string txtError, bool doTranscode)
        {
            MediaStreamer ms = GetStreamerByID(streamerID);
            if (ms == null)
            {
                txtError = "No such streamer.";
                return false;
            }

            return (ms.GetSegment(index, segmentNumber, ref Data, ref txtError, doTranscode));
        }
        #endregion

        #region Index File

        public string IndexFileForStreamer(int StreamerID, bool background, int segmentstart)
        {
            MediaStreamer ms = GetStreamerByID(StreamerID);

            if (Settings.Default.DebugStreaming)
                Functions.WriteLineToLogFile("ms.Request:" + ms.Request.InputFile + "," + ms.Request.LiveTV + "," + ms.Request.ClientID);
            ms.Request.InputFile = HttpUtility.HtmlDecode(ms.Request.InputFile);
            if (Settings.Default.DebugStreaming)
                Functions.WriteLineToLogFile("ms.Request.InputFile htmldecoded:" + ms.Request.InputFile);
            TimeSpan mediaDuration = (!(ms.Request.LiveTV || ms.Request.NewLiveTV) ? FileBrowseExporter.DurationOfMediaFile_OSSpecific(ms.Request.InputFile) : new TimeSpan(0, ms.Request.DurationLiveTVBlocks, 0));
            int msSegmentDuration = ms.Request.ActualSegmentDuration;

            StringBuilder sbIndexFile = new StringBuilder(1000);
            string WorkingDirectory = "c:\\";
            if (background)
            {
                //                StopStreamer(StreamerID, 99);
            }
            if (ms.Request.NewLiveTV)
            {
                if (false || !true)//(Settings.Default.EfficientPiping) || //private bool usingFFMPEGsegmenter = false; change in liveTVParts as well
                {
                    int currentSegmentNr = 0;
                    string rpPath =
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                     "RemotePotato");
                    WorkingDirectory = Path.Combine(rpPath, "static\\mediastreams\\" + StreamerID.ToString());
                    if (!Directory.Exists(WorkingDirectory)) Directory.CreateDirectory(WorkingDirectory);
                    string m3u8File = "";
                    int nrSegments = (int)(mediaDuration.TotalSeconds / Convert.ToDouble(msSegmentDuration));
                    if (true)//(!true) //private bool usingFFMPEGsegmenter = false; change in liveTVParts as well
                    {
                        m3u8File += "#EXTM3U\n";
                        m3u8File += "#EXT-X-TARGETDURATION:" + (msSegmentDuration + 1) + "\n";  // maximum duration of any one file, in seconds
                        m3u8File += "#EXT-X-ALLOW-CACHE:YES\n"; // allow client to cache files
                        for (int j = 1; j < nrSegments; j++)
                        {
                            //if (Settings.Default.DebugStreaming)
                            //    Functions.WriteLineToLogFile("checking if exists:" + WorkingDirectory + "\\liveseg-" + j +
                            //                                 ".ts");
                            //if (!File.Exists(WorkingDirectory + "\\liveseg-" + j + ".ts")) break;
                            m3u8File += "#EXTINF:" + msSegmentDuration + ".0,\n";
                            m3u8File += "liveseg-" + j + ".ts\n";
                        }
                        m3u8File += "#EXT-X-ENDLIST";
                    }
                    else // in case of the old segmenter not the segmenterSV changed one
                    {
                        for (int j = 0; true; j++) //gets slower and slower, better make a filewatcher!!!
                        {
                            if (Settings.Default.DebugStreaming)
                                Functions.WriteLineToLogFile("checking if exists:" + WorkingDirectory + "\\livetvtemp" + j +
                                                             ".m3u8");
                            if (!File.Exists(WorkingDirectory + "\\livetvtemp" + j + ".m3u8")) break;
                            var fs = File.Open(WorkingDirectory + "\\livetvtemp" + j + ".m3u8", FileMode.OpenOrCreate,
                                               FileAccess.ReadWrite, FileShare.ReadWrite);
                            var sr2 = new StreamReader(fs);
                            string newm3u8FileToAdd = sr2.ReadToEnd();
                            if (j == 0) //!File.Exists(WorkingDirectory + "\\index.m3u8"))
                            {
                                //fs = File.Open(WorkingDirectory + "\\index.m3u8", FileMode.OpenOrCreate,
                                //               FileAccess.ReadWrite,
                                //               FileShare.ReadWrite);
                                //sr2 = new StreamReader(fs);
                                //m3u8File = sr2.ReadToEnd();
                                List<string> M3U8RegelsToAdd = splitByDelimiter(newm3u8FileToAdd, Environment.NewLine);
                                foreach (string s in M3U8RegelsToAdd)
                                {
                                    if (!s.Contains("ENDLIST"))
                                    {
                                        m3u8File += s + "\n";
                                        if (s.Contains("liveseg"))
                                        {
                                            if (Settings.Default.DebugStreaming)
                                                Functions.WriteLineToLogFile("---> added " + s);
                                            List<string> parts = s.Split('-').ToList();
                                            List<string> parts2 = parts[1].Split('.').ToList();
                                            currentSegmentNr = Int32.Parse(parts2[0]);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                List<string> M3U8RegelsToAdd = splitByDelimiter(newm3u8FileToAdd, Environment.NewLine);
                                foreach (string s in M3U8RegelsToAdd)
                                {
                                    if (s.Contains("EXTINF") || s.Contains("liveseg"))
                                    {
                                        string str = s;
                                        if (s.Contains("liveseg"))
                                        {
                                            if (Settings.Default.DebugStreaming)
                                                Functions.WriteLineToLogFile("---> added " + s);
                                            str = "liveseg-" + (++currentSegmentNr) + ".ts";
                                        }
                                        m3u8File += str + "\n";
                                    }
                                }
                            }
                        }
                    }
                    TextWriter tw2 = new StreamWriter(Functions.AppDataFolder + "\\static\\mediastreams\\" + StreamerID + "\\index.m3u8");
                    tw2.Write(m3u8File);
                    tw2.Close();

                    return m3u8File;
                }
                else
                {
                    //if (Settings.Default.DebugStreaming)
                    //    Functions.WriteLineToLogFile2("StreamingManager: client asks to generate new m3u8, resistance is futile");
                    string rpPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RemotePotato");
                    WorkingDirectory = Path.Combine(rpPath, "static\\mediastreams\\" + StreamerID.ToString());
                    if (!Directory.Exists(WorkingDirectory)) Directory.CreateDirectory(WorkingDirectory);
                    if (!File.Exists(WorkingDirectory + "\\livetvtemp0.m3u8")) return "";
                    var fs = File.Open(WorkingDirectory + "\\livetvtemp0.m3u8", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                    var sr2 = new StreamReader(fs);
                    string m3u8File = sr2.ReadToEnd();
                    return m3u8File;
                }
            }
            sbIndexFile.AppendLine("#EXTM3U");
            if (ms.Request.ClientUsesVLC)
            {
                sbIndexFile.AppendLine("#EXT-X-START:" + segmentstart); //TODO: using segmnetnr here, not exactly following latest specs of HLS!!!!!!
            }
            if (ms.Request.ClientUsesApple)
            {
                sbIndexFile.AppendLine("#EXT-X-START2:" + segmentstart); //TODO: using segmnetnr here, not exactly following latest specs of HLS!!!!!!
            }
            if (ms.Request.UseNewerFFMPEG && !ms.Request.LiveTV)
            {
                sbIndexFile.AppendLine("#EXT-X-TARGETDURATION:" + (msSegmentDuration + 1));  // maximum duration of any one file, in seconds
            }
            else
            {
                sbIndexFile.AppendLine("#EXT-X-TARGETDURATION:" + msSegmentDuration.ToString());  // maximum duration of any one file, in seconds
            }
            sbIndexFile.AppendLine("#EXT-X-ALLOW-CACHE:YES"); // allow client to cache files
            double dNumberSegments;
            if (ms.Request.LiveTV)// || ms.Request.NewLiveTV)
            {
                int q = ms.Request.InitialWaitTimeBeforeLiveTVStarts;
                int r = ms.Request.SegmentIncreasingStepsLiveTV;
                double StartAtSegmentWhereCalculatedSegmentDurationIsMax = (msSegmentDuration - q) / r;
                double StartAtSecondsWhereCalculatedSegmentDurationIsMax = (Math.Min(q + (int)StartAtSegmentWhereCalculatedSegmentDurationIsMax * r, msSegmentDuration)
                    - q) * (Math.Min(q + (int)StartAtSegmentWhereCalculatedSegmentDurationIsMax * r, msSegmentDuration) + q - r) / (2 * r);

                dNumberSegments = (mediaDuration.TotalSeconds - StartAtSecondsWhereCalculatedSegmentDurationIsMax) / msSegmentDuration + StartAtSegmentWhereCalculatedSegmentDurationIsMax;
            }
            else //never change a winning team:
            {
                dNumberSegments = mediaDuration.TotalSeconds / Convert.ToDouble(msSegmentDuration);
            }
            int WholeNumberSegments = Convert.ToInt32(Math.Floor(dNumberSegments));
            int i;
            //            int OldSegmentDuration = 2;
            int SegmentDuration;
            string strSegID = "";
            int from = (ms.Request.NewLiveTV ? 1 : 0);
            for (i = from; i < WholeNumberSegments; i++) // TODO: for newlivetv: have to cheang wholenumbersegments to differrent nr (higher) cuz now also segmnent<4sec
            {
                if (ms.Request.LiveTV)// || ms.Request.NewLiveTV)
                {
                    SegmentDuration = Math.Min(msSegmentDuration, ms.Request.InitialWaitTimeBeforeLiveTVStarts + i * ms.Request.SegmentIncreasingStepsLiveTV); //make segments q second bigger untill 60 seconds reached

                    // start with 4 seconds first then gradually increase up to 1 minute of segmentlength
                    sbIndexFile.AppendLine("#EXTINF:" + SegmentDuration.ToString() + ",");
                    //                    sbIndexFile.AppendLine("#EXTINF:4,");
                }
                else
                {
                    if (ms.Request.UseNewerFFMPEG && !ms.Request.LiveTV)
                    {
                        sbIndexFile.AppendLine("#EXTINF:" + msSegmentDuration.ToString() + ".0,");
                    }
                    else
                    {
                        sbIndexFile.AppendLine("#EXTINF:" + msSegmentDuration.ToString() + ",");
                    }
                }
                if (!ms.Request.NewLiveTV)
                {
                    if (background)
                    {
                        strSegID = "segbackground-" + i.ToString() + ".ts";
                    }
                    else
                    {
                        strSegID = "seg-" + i.ToString() + ".ts";
                    }
                }
                else
                {
                    strSegID = "liveseg-" + i + ".ts";
                }

                sbIndexFile.AppendLine(strSegID);
            }

            // Duration of final segment? TODO for NEWLIVETV
            double dFinalSegTime;
            if (ms.Request.LiveTV)//|| ms.Request.NewLiveTV)
            {
                dFinalSegTime = (dNumberSegments - WholeNumberSegments) * msSegmentDuration; // TODO: should also take inot account special case where totalduration smalller than when mssegmentduration segments appear
            }
            else
            {
                dFinalSegTime = mediaDuration.TotalSeconds % Convert.ToDouble(msSegmentDuration);
            }
            int iFinalSegTime = Convert.ToInt32(dFinalSegTime);
            if (iFinalSegTime > 0) // adding this prevents stream from freezing at end
            {
                sbIndexFile.AppendLine("#EXTINF:" + iFinalSegTime.ToString() + ",");
                string strFinalSegID = "";
                if (!ms.Request.NewLiveTV)
                {
                    if (background)
                    {
                        strFinalSegID = "segbackground-" + i.ToString() + ".ts";
                    }
                    else
                    {
                        strFinalSegID = "seg-" + i.ToString() + ".ts";
                    }
                }
                else
                {
                    strFinalSegID = "liveseg-" + i + ".ts";
                }
                sbIndexFile.AppendLine(strFinalSegID);
            }

            sbIndexFile.AppendLine("#EXT-X-ENDLIST");

            TextWriter tw;
            tw = new StreamWriter(Functions.AppDataFolder + "\\static\\mediastreams\\" + StreamerID + "\\index.m3u8");
            tw.Write(sbIndexFile.ToString());
            tw.Close();

            return sbIndexFile.ToString();
        }

        private StringBuilder RebuildM3U8FromStart(string m3u8, int newMaxDuration)
        {
            StringBuilder sbIndexFile = new StringBuilder(1000);
            List<string> OudeM3U8Regels = splitByDelimiter(m3u8, Environment.NewLine);
            foreach (string regel in OudeM3U8Regels)
            {
                if (regel.Contains("#EXT-X-TARGETDURATION:"))
                {
                    sbIndexFile.AppendLine("#EXT-X-TARGETDURATION:" + newMaxDuration);
                }
                else
                {
                    sbIndexFile.AppendLine(regel);
                }
            }
            return sbIndexFile;
        }

        private int getSegmentNr(string path)
        {
            int iIndexNumber = 0;
            string strSegNumber;
            path = path.Replace(".ts", "");
            path = path.Replace(".ts", "");
            List<string> parts = path.Split('-').ToList();
            if (parts.Count > 1)
            {
                strSegNumber = parts[parts.Count - 1];
                if (int.TryParse(strSegNumber, out iIndexNumber))
                {
                    return iIndexNumber;
                }
            }
            else
            {
                return -1;
            }

            return -1;
        }




        //http://stackoverflow.com/questions/6061957/get-all-files-and-directories-in-specific-path-fast
        static List<FileInfo> FullDirList(DirectoryInfo dir, string searchPattern)
        {
            List<FileInfo> files = new List<FileInfo>();  // List that will hold the files and subfiles in path
            // Console.WriteLine("Directory {0}", dir.FullName);
            // list the files
            try
            {
                foreach (FileInfo f in dir.GetFiles(searchPattern))
                {
                    //Console.WriteLine("File {0}", f.FullName);
                    files.Add(f);
                }
            }
            catch
            {
                Console.WriteLine("Directory {0}  \n could not be accessed!!!!", dir.FullName);
                return null;  // We alredy got an error trying to access dir so dont try to access it again
            }
            return files;

            // process each directory
            // If I have been able to see the files in the directory I should also be able 
            // to look at its directories so I dont think I should place this in a try catch block
            //foreach (DirectoryInfo d in dir.GetDirectories())
            //{
            //    folders.Add(d);
            //    FullDirList(d, searchPattern);
            //}

        }

        private bool TempM3u8IndexBestaat(int index, string wd)
        {
            return File.Exists(wd + "\\livetvtemp" + index + ".m3u8");
        }

        private double getLengte(string EXTINFLengteTempM3U8)
        {
            List<string> parts = splitByDelimiter(EXTINFLengteTempM3U8, ":");
            parts = splitByDelimiter(parts[1], ",");
            double value = double.Parse(parts[0]);
            if (value > double.MaxValue)
            {
                return 0;
            }
            else
            {
                return value;
            }
        }


        private void MaakInitialM3U8ForNewLiveTV(string WorkingDirectory, int StreamerID)
        {
            MediaStreamer ms = GetStreamerByID(StreamerID);
            int msSegmentDuration = ms.Request.ActualSegmentDuration;

            StringBuilder sbIndexFile = new StringBuilder(1000);
            sbIndexFile.AppendLine("#EXTM3U");
            sbIndexFile.AppendLine("#EXT-X-VERSION:3");
            sbIndexFile.AppendLine("#EXT-X-TARGETDURATION:" + (msSegmentDuration * 3));  // maximum duration of any one file, in seconds, the ffmpeg segmenter rather fluctuates
            // I don't think mx player likes this changing ext-duration, so we set it at 3 times initially
            sbIndexFile.AppendLine("#EXT-X-MEDIA-SEQUENCE:1");
            sbIndexFile.AppendLine("#EXT-X-ALLOW-CACHE:YES"); // allow client to cache files
            sbIndexFile.AppendLine("start here");

            //sbIndexFile.AppendLine(TSFilesinM3U8(StreamerID, msSegmentDuration, 0));

            //sbIndexFile.AppendLine("#EXT-X-ENDLIST");

            using (StreamWriter sw = new StreamWriter(WorkingDirectory + "\\CurrentIndex.m3u8"))
                sw.Write(sbIndexFile.ToString());
            return;
        }

        //private string TSFilesinM3U8(int StreamerID, int duration, int index)
        //{
        //    MediaStreamer ms = GetStreamerByID(StreamerID);

        //    StringBuilder sbIndexFile = new StringBuilder(1000);

        //    double dNumberSegments;
        //    TimeSpan mediaDuration = new TimeSpan(0, ms.Request.DurationLiveTVBlocks, 0);
        //    dNumberSegments = mediaDuration.TotalSeconds / Convert.ToDouble(duration);
        //    int WholeNumberSegments = Convert.ToInt32(Math.Floor(dNumberSegments));
        //    int i;
        //    string strSegID = "";
        //    for (i = 0; i < WholeNumberSegments; i++)
        //    {
        //        sbIndexFile.AppendLine("#EXTINF:" + duration.ToString() + ",");
        //        strSegID = "liveseg_" + index + "-" + (i + 1).ToString() + ".ts";
        //        sbIndexFile.AppendLine(strSegID);
        //    }

        //    // Duration of final segment? TODO for NEWLIVETV
        //    double dFinalSegTime;
        //    dFinalSegTime = mediaDuration.TotalSeconds % Convert.ToDouble(duration);
        //    int iFinalSegTime = Convert.ToInt32(dFinalSegTime);
        //    sbIndexFile.AppendLine("#EXTINF:" + iFinalSegTime.ToString() + ",");
        //    string strFinalSegID = "";
        //    strFinalSegID = "liveseg_" + index + "-" + (i + 1).ToString() + ".ts";
        //    sbIndexFile.AppendLine(strFinalSegID);

        //    return sbIndexFile.ToString();
        //}

        List<string> SortByIndexThenSegmentNr(List<FileInfo> tsFiles)
        {
            List<string> files = tsFiles.ConvertAll(e => e.Name);
            files.Sort((a, b) => new StringNum(a).CompareTo(new StringNum(b)));
            return files;
        }

        /// <summary>
        /// Split a string by a delimiter, remove blank lines and trim remaining lines
        /// </summary>
        /// <param name="strText"></param>
        /// <param name="strDelimiter"></param>
        /// <returns></returns>
        List<String> splitByDelimiter(string strText, string strDelimiter)
        {
            List<string> strList = strText.Split(strDelimiter.ToCharArray()).ToList();
            List<string> strOutput = new List<string>();
            foreach (string s in strList)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                strOutput.Add(s.Trim());
            }

            return strOutput;
        }


        private int getIndex(string txtAction)
        {
            int iIndexNumber = 0;
            string strSegNumber;
            List<string> parts = txtAction.Split('_').ToList();
            if (parts.Count > 1)
            {
                parts = parts[1].Split('-').ToList();
                if (parts.Count > 1)
                {
                    strSegNumber = parts[0];
                }
                else
                {
                    return -1;
                }
            }
            else
            {
                return -1;
            }

            if (int.TryParse(strSegNumber, out iIndexNumber))
            {
                return iIndexNumber;
            }

            return -1;
        }

        #endregion

        #region Probing

        public TimeSpan GetMediaDuration(string fileName)
        {
            MediaInfoGrabber grabber = new MediaInfoGrabber(Functions.ToolkitFolder, Path.Combine(Functions.StreamBaseFolder, "probe_results"), fileName);
            grabber.DebugMessage += new EventHandler<WasFatAttitude.GenericEventArgs<string>>(grabber_DebugMessage);
            grabber.GetInfo("ffmpeglatest.exe", "");
            grabber.DebugMessage -= grabber_DebugMessage;

            TimeSpan duration = (grabber.Info.Success) ? grabber.Info.Duration : new TimeSpan(0);

            return duration;
        }

        void grabber_DebugMessage(object sender, WasFatAttitude.GenericEventArgs<string> e)
        {
            Functions.WriteLineToLogFileIfSetting(Settings.Default.DebugStreaming, e.Value);
        }
        public List<AVStream> ProbeFile(string fileName)
        {
            FFMPGProber prober = new FFMPGProber();

            string strTempDirName = "probe_results";
            string OutputBasePath = Path.Combine(Functions.StreamBaseFolder, strTempDirName);
            prober.DebugMessage += new EventHandler<WasFatAttitude.GenericEventArgs<string>>(prober_DebugMessage);
            bool result = prober.Probe("ffmpeglatest.exe", "", Functions.ToolkitFolder, fileName, OutputBasePath);
            prober.DebugMessage -= new EventHandler<WasFatAttitude.GenericEventArgs<string>>(prober_DebugMessage);
            if (!result)
                return new List<AVStream>();

            return prober.AVAudioAndVideoStreams;
        }
        void prober_DebugMessage(object sender, WasFatAttitude.GenericEventArgs<string> e)
        {
            Functions.WriteLineToLogFileIfSetting(Settings.Default.DebugStreaming, e.Value);
        }
        #endregion


        #region Singleton Methods
        static StreamingManager instance = null;
        static readonly object padlock = new object();
        public static StreamingManager Default
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new StreamingManager();
                    }
                    return instance;
                }
            }
        }
        #endregion
    }
}
//http://stackoverflow.com/questions/1250960/how-to-sort-out-numeric-strings-as-numerics
public class StringNum : IComparable<StringNum>
{

    private List<string> _strings;
    private List<int> _numbers;

    public StringNum(string value)
    {
        _strings = new List<string>();
        _numbers = new List<int>();
        int pos = 0;
        bool number = false;
        while (pos < value.Length)
        {
            int len = 0;
            while (pos + len < value.Length && Char.IsDigit(value[pos + len]) == number)
            {
                len++;
            }
            if (number)
            {
                _numbers.Add(int.Parse(value.Substring(pos, len)));
            }
            else
            {
                _strings.Add(value.Substring(pos, len));
            }
            pos += len;
            number = !number;
        }
    }

    public int CompareTo(StringNum other)
    {
        int index = 0;
        while (index < _strings.Count && index < other._strings.Count)
        {
            int result = _strings[index].CompareTo(other._strings[index]);
            if (result != 0) return result;
            if (index < _numbers.Count && index < other._numbers.Count)
            {
                result = _numbers[index].CompareTo(other._numbers[index]);
                if (result != 0) return result;
            }
            index++;
        }
        return 0;
    }

}

public struct StreamIdInputFilePair
{
    private int _sKey;
    public int sKey
    {
        get { return _sKey; }
        set { _sKey = value; }
    }
    private string _sValue;
    public string sValue
    {
        get { return _sValue; }
        set { _sValue = value; }
    }

}

/*

public sealed class Singleton
{
    static Singleton instance=null;
    static readonly object padlock = new object();

    Singleton()
    {
    }

    public static Singleton Instance
    {
        get
        {
            lock (padlock)
            {
                if (instance==null)
                {
                    instance = new Singleton();
                }
                return instance;
            }
        }
    }
}
*/