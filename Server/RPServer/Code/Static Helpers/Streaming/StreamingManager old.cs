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
using getnextpartLiveTV;

namespace RemotePotatoServer
{
    public sealed class StreamingManager
    {
        Dictionary<int, MediaStreamer> mediaStreamers;
        System.Timers.Timer JanitorTimer;
        private SQLiteDatabase db;

        StreamingManager()
        {
            // Set up streamers
            mediaStreamers = new Dictionary<int, MediaStreamer>();

            // Delete all streaming files
            DeleteAllStreamingFiles();

            InitJanitor();

            InitIdsAndInputFilesDatabase();
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

        public int newUniqueID(MediaStreamingRequest request, bool getIDOnly, bool dontstart)
        {
            if (dontstart)
            {
                return IdsAndInputFilesContains(request.InputFile + request.UniekClientID);//it must exist because this function only gets called when exist in deserialized MediaSteramRequest
            }
            else
            {
                return newUniqueID(request, getIDOnly);
            }
        }

        public int newUniqueID(MediaStreamingRequest request, bool getIDOnly)
        {
            int newId = IdsAndInputFilesContains(request.InputFile + request.UniekClientID);
            if (request.KeepSameIDForSameInputFile && newId != 0) // != 0 means found as existing id that can be resumed
            {
                var ms = GetStreamerByID(newId);
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

                //bump up the id in the database
                db.Delete("IDANDINPUTFILE", String.Format("STREAMID = {0}", "" + newId));
                var item = new Dictionary<string, string>();
                item.Add("STREAMID", "" + newId);
                string i = request.InputFile.Replace("'", "''");
                string u = request.UniekClientID.Replace("'", "''");
                item.Add("INPUTFILE", i + u);
                db.Insert("IDANDINPUTFILE", item);

                return newId;
            }




            //clean up if database is large
            const int maxFileToRememberResume = 1000;
            int count = 0;
            Int32.TryParse(db.ExecuteScalar("select COUNT(*) from IDANDINPUTFILE where STREAMID IS NOT NULL;"),
                           out count);
            if (count > maxFileToRememberResume)
            {
                try
                {
                    DataTable tabel;
                    String query = "select STREAMID, INPUTFILE from IDANDINPUTFILE;";
                    tabel = db.GetDataTable(query);
                    // The results can be directly applied to a DataGridView control
                    //                            recipeDataGrid.DataSource = tabel;
                    // Or looped through for some other reason
                    var i = 0;
                    foreach (DataRow r in tabel.Rows)
                    {
                        if (i < count / 2)
                        {
                            db.ExecuteNonQuery("delete from IDANDINPUTFILE where STREAMID=" + r["STREAMID"] + ";");
                        }
                        else
                        {
                        }
                        i++;
                    }
                }
                catch (Exception fail)
                {
                    String error = "The following error has occurred in cleaning up database : " + db.ToString() + "\n";
                    error += fail.Message;
                    if (Settings.Default.DebugStreaming)
                        Functions.WriteLineToLogFile("StreamingManager: " + error);
                }
            }




            do
            {
                var r = new Random();
                //newId = (getIDOnly ? r.Next(100000, 999999) : r.Next(10000, 99999));
                newId = r.Next(10000, 99999);
            } while (mediaStreamers.ContainsKey(newId) || !IdsAndInputFilesContains(newId).Equals(""));

            if (IdsAndInputFilesContains(request.InputFile + request.UniekClientID) == 0 && !request.InputFile.Contains("RMCLiveTV")) //live tv gets a new iD all the time anyway, due to randowm nr in inputfile string
            {
                var item = new Dictionary<string, string>();
                item.Add("STREAMID", "" + newId);
                string i = request.InputFile.Replace("'", "''");
                string u = request.UniekClientID.Replace("'", "''");
                item.Add("INPUTFILE", i + u);
                db.Insert("IDANDINPUTFILE", item);
                //  db.CommitTransaction();
            }
            return newId;
        }

        public int IdsAndInputFilesContains(string InputFile)
        {
            int uit = 0;
            if (Settings.Default.DebugStreaming)
                Functions.WriteLineToLogFile("StreamingManager: " + "select STREAMID from IDANDINPUTFILE where INPUTFILE='" + InputFile + "';");
            //var command = new SqlCommand("select STREAMID from IDANDINPUTFILE where INPUTFILE = @inputfile;");
            //SqlParameter param = new SqlParameter();
            //param.ParameterName = "@inputfile";
            //param.Value = InputFile;
            //command.Parameters.Add(param);
            InputFile = InputFile.Replace("'", "''"); // escape single quotes
            Int32.TryParse(db.ExecuteScalar(String.Format("select STREAMID from IDANDINPUTFILE where INPUTFILE = '{0}';", InputFile)), out uit);
            return uit;
        }

        //private bool IdsAndInputFilesContains(int ID)
        //{
        //    int count = 0;
        //    Int32.TryParse(db.ExecuteScalar("select COUNT(*) from IDANDINPUTFILE where STREAMID='" + ID + "';"),
        //                   out count);
        //    return count == 1;
        //}

        private string IdsAndInputFilesContains(int ID)
        {
            return db.ExecuteScalar("select INPUTFILE from IDANDINPUTFILE where STREAMID='" + ID + "';");
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


        private void InitIdsAndInputFilesDatabase()
        {
            string rppath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "remotepotato");
            string databaseFile = Path.Combine(rppath, "settings\\StreamIDsAndFiles.s3db");

            try
            {
                if (!File.Exists(databaseFile))
                {
                    string emptydatabaseFile = Path.Combine(Functions.ToolkitFolder, "StreamIDsAndFiles.s3db");
                    File.Copy(emptydatabaseFile, databaseFile);
                }

                //http://stackoverflow.com/questions/4683142/c-sharp-waiting-for-a-copy-operation-to-complete
                while (true)
                {
                    try
                    {
                        using (System.IO.File.Open(databaseFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            break;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                db = new SQLiteDatabase(databaseFile);
            }
            catch (Exception fail)
            {
                String error = "The following error has occurred when using databasefile " + databaseFile + "\n";
                error += fail.Message;
                if (Settings.Default.DebugStreaming)
                    Functions.WriteLineToLogFile("StreamingManager: " + error);
            }
            //            Deze code vervangen door iets beters ivm setpoweroptions
            // at restart of RP load transcoded mediastreamers into memory
            //List<MediaStreamingRequest> msrlist = MediaStreamer.DeserializeTranscodedMSRFromXML();
            //bool AlreadyExists;
            //foreach (var mediaStreamingRequest in msrlist)
            //{
            //    MediaStreamingResult msresult = StartStreamer(mediaStreamingRequest, null, true, out AlreadyExists);
            //}
        }




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
            int newStreamerID = newUniqueID(request, false, dontstart);

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
                else if (request.UseNewerFFMPEG)
                {
                    result.LiveStreamingIndexPath = "/httplivestream/" + newStreamerID.ToString() + "/index2.m3u8";
                }
                else
                {
                    result.LiveStreamingIndexPath = "/httplivestream/" + newStreamerID.ToString() + "/index.m3u8";
                }

                // Add streamer ID to result
                result.StreamerID = newStreamerID;

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
        public string IndexFileForStreamer(int StreamerID, bool background)
        {
            MediaStreamer ms = GetStreamerByID(StreamerID);

            ms.Request.InputFile = HttpUtility.HtmlDecode(ms.Request.InputFile);
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
                if (LiveTVParts.usingVLCWithsegmenter || LiveTVParts.UseVLCHLSsegmenter || LiveTVParts.dontUsePipe)
                {
                    // do it the old  style
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
            if (ms.Request.UseNewerFFMPEG)
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
            int from = (ms.Request.NewLiveTV?1:0);
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
                    if (ms.Request.UseNewerFFMPEG)
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
                    strFinalSegID = "liveseg-" + i+".ts";
                }
                sbIndexFile.AppendLine(strFinalSegID);
            }

            sbIndexFile.AppendLine("#EXT-X-ENDLIST");

            TextWriter tw = new StreamWriter(Functions.AppDataFolder + "\\static\\mediastreams\\" + StreamerID + "\\index.m3u8");
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