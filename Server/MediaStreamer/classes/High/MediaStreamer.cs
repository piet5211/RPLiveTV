using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Timers;
using System.Threading;
using FatAttitude;
using FatAttitude.MediaStreamer.HLS;
using FatAttitude.Collections;
using System.Collections.Concurrent;
using WasFatAttitude;
using System.Web;
using System.Xml.Serialization;
using RemotePotatoServer.Properties;

namespace FatAttitude.MediaStreamer
{
    public class MediaStreamer
    {

        // Public
        public int ID { get; set; }
        public DateTime CreationDate { get; set; }
        public string AdditionalStatusInfo { get; set; }
        bool SettingsDefaultDebugAdvanced;

        // Private
        public MediaStreamingRequest Request;
        SegmentStoreBroker broker;
        // Keepalive
        System.Timers.Timer lifeTimer;

        /// <summary>
        /// Note that everything should be passed into the constructor that depends upon encoding, since settings public
        /// fields afterwards would result in them not being passed to lower objects that are set up in the constructor.
        /// If the constructor grows really bloated, suggest a separate constructor and Configure() method.
        /// </summary>
        /// <param name="_ID"></param>
        /// <param name="request"></param>
        /// <param name="pathToTools"></param>
        /// <param name="timeToKeepAlive"></param>
        /// <param name="debugAdvanced"></param>
        public MediaStreamer(int _ID, MediaStreamingRequest request, string pathToTools, int timeToKeepAlive, bool debugAdvanced)
        {
            // Store variables
            ID = _ID;
            Request = request;
            SettingsDefaultDebugAdvanced = debugAdvanced;

            // Set up life timer
            lifeTimer = new System.Timers.Timer(1000);
            lifeTimer.AutoReset = true;
            lifeTimer.Elapsed += new ElapsedEventHandler(lifeTimer_Elapsed);
            lifeTimer.Start();

            // Creation date
            CreationDate = DateTime.Now;

            // Status
            AdditionalStatusInfo = "";

            // Create broker - and hook it to runner events
            broker = new SegmentStoreBroker(this.ID.ToString(), request, pathToTools);
            broker.SettingsDefaultDebugAdvanced = this.SettingsDefaultDebugAdvanced;
            broker.DebugMessage += new EventHandler<GenericEventArgs<string>>(broker_DebugMessage);
            broker.DebugMessage2 += new EventHandler<GenericEventArgs<string>>(broker_DebugMessage2);
            broker.DebugMessage3 += new EventHandler<GenericEventArgs<string>>(broker_DebugMessage3);
            broker.DebugMessage4 += new EventHandler<GenericEventArgs<string>>(broker_DebugMessage4);
        }
        void lifeTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // Any regularly called methods go here, e.g. auto stop streaming, prune segments, etc.
            AutoPause_Tick();
        }
        

        #region Top level Public
        bool everStartedRunner = false;
        bool IsConfigured;
        /// <summary>
        /// Set up the streamer and (current implementation) begin transcoding, to get ahead of the game for future segment requests
        /// </summary>
        /// <returns></returns>
        public MediaStreamingResult Configure()
        {
            // File exists?
            Request.InputFile = HttpUtility.HtmlDecode(Request.InputFile);
            if (!File.Exists(Request.InputFile))
            {
                MediaStreamingResult badResult = new MediaStreamingResult(MediaStreamingResultCodes.FileNotFound, "File not found: " + Request.InputFile);
                return badResult;
            }

            // OK let's try
            SendDebugMessage("MediaStreamer: Configuring streaming.");


            ////Hack proposed by Carl P. to get more HD like streaming in silverlight client
            //// Force change 640x480 to 960x720
            //if (Request.CustomParameters != null)
            //{
            //    if ((Request.CustomParameters.FrameWidth == 640) && (Request.CustomParameters.FrameHeight == 480))
            //    {
            //        Request.CustomParameters.FrameWidth = 960;
            //        Request.CustomParameters.FrameHeight = 720;

            //        // Add any additional parameters here, e.g. bitrate
            //        Request.CustomParameters.VideoBitRate = "2400k";
            //        Request.CustomParameters.BitRateDeviation = "120k";
            //        Request.CustomParameters.X264SubQ = 8;
            //        Request.CustomParameters.AudioBitRate = "96k";
            //    }
            //}
            // Force change 800x600 to 1920x1080
            //Changes propesed by neebb:
            if (Request.CustomParameters != null)
            {
                if ((Request.CustomParameters.FrameWidth == 800) && (Request.CustomParameters.FrameHeight == 600))
                {
                    Request.CustomParameters.FrameWidth = 1920;
                    Request.CustomParameters.FrameHeight = 1080;

                    // Add any additional parameters here, e.g. bitrate
                    Request.CustomParameters.VideoBitRate = "5000k";
                    Request.CustomParameters.BitRateDeviation = "200k";
                    Request.CustomParameters.X264SubQ = 8;
                    Request.CustomParameters.AudioBitRate = "96k";
                }
            }

            // Force change 640x480 to 1920x1080
            if (Request.CustomParameters != null)
            {
                if ((Request.CustomParameters.FrameWidth == 640) && (Request.CustomParameters.FrameHeight == 480))
                {
                    Request.CustomParameters.FrameWidth = 1920;
                    Request.CustomParameters.FrameHeight = 1080;

                    // Add any additional parameters here, e.g. bitrate
                    Request.CustomParameters.VideoBitRate = "5000k";
                    Request.CustomParameters.BitRateDeviation = "200k";
                    Request.CustomParameters.X264SubQ = 8;
                    Request.CustomParameters.AudioBitRate = "96k";
                }
            }

            // Force change 576x432 to 1280x720
            if (Request.CustomParameters != null)
            {
                if ((Request.CustomParameters.FrameWidth == 576) && (Request.CustomParameters.FrameHeight == 432))
                {
                    Request.CustomParameters.FrameWidth = 1280;
                    Request.CustomParameters.FrameHeight = 720;

                    // Add any additional parameters here, e.g. bitrate
                    Request.CustomParameters.VideoBitRate = "3000k";
                    Request.CustomParameters.BitRateDeviation = "200k";
                    Request.CustomParameters.X264SubQ = 8;
                    Request.CustomParameters.AudioBitRate = "96k";
                }
            }

            // Force change 512x384 to 896x504
            if (Request.CustomParameters != null)
            {
                if ((Request.CustomParameters.FrameWidth == 512) && (Request.CustomParameters.FrameHeight == 384))
                {
                    Request.CustomParameters.FrameWidth = 896;
                    Request.CustomParameters.FrameHeight = 504;

                    // Add any additional parameters here, e.g. bitrate
                    Request.CustomParameters.VideoBitRate = "2000k";
                    Request.CustomParameters.BitRateDeviation = "200k";
                    Request.CustomParameters.X264SubQ = 8;
                    Request.CustomParameters.AudioBitRate = "96k";
                }
            }


            // Force change 320x240 to 640x360
            if (Request.CustomParameters != null)
            {
                if ((Request.CustomParameters.FrameWidth == 320) && (Request.CustomParameters.FrameHeight == 240))
                {
                    Request.CustomParameters.FrameWidth = 640;
                    Request.CustomParameters.FrameHeight = 360;

                    // Add any additional parameters here, e.g. bitrate
                    Request.CustomParameters.VideoBitRate = "1000k";
                    Request.CustomParameters.BitRateDeviation = "100k";
                    Request.CustomParameters.X264SubQ = 6;
                    Request.CustomParameters.AudioBitRate = "32k";
                }
            }




            // Used in auto-die
            lastContactAtTime = DateTime.Now;
            everStartedRunner = true;  // used in auto-die

            // We did it
            IsConfigured = true;

            // Return positive result
            MediaStreamingResult result = new MediaStreamingResult();
            result.FrameWidth = Request.CustomParameters.FrameWidth;
            result.FrameHeight = Request.CustomParameters.FrameHeight;
            result.ResultCode = MediaStreamingResultCodes.OK;
            result.Completed = true;
            result.Success = true;
            return result;
        }

        static public void SerializeTranscodedMSRToXML(List<MediaStreamingRequest>  msr)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(List<MediaStreamingRequest>));
            string rppath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "remotepotato");
            string MSRFile = Path.Combine(rppath, "settings\\transcodedmsr.xml");
            TextWriter textWriter = new StreamWriter(MSRFile);
            serializer.Serialize(textWriter, msr);
            textWriter.Close();
        }

        static public List<MediaStreamingRequest> DeserializeTranscodedMSRFromXML()
        {
            XmlSerializer deserializer = new XmlSerializer(typeof(List<MediaStreamingRequest>));
            string rppath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "remotepotato");
            string MSRFile = Path.Combine(rppath, "settings\\transcodedmsr.xml");
            List<MediaStreamingRequest> msr= new List<MediaStreamingRequest>(); 
            if (!File.Exists(MSRFile)) return msr;
            TextReader textReader = new StreamReader(MSRFile);
            msr = (List<MediaStreamingRequest>)deserializer.Deserialize(textReader);
            textReader.Close();

            return msr;
        }
        
        bool isAborted;
        public void AbortStreaming(bool removeFilesFromDisk)
        {
            if (isAborted) return;

            try
            {
                SendDebugMessage("MediaStreamer: Stopping streaming (and killing child process).");

                // Kill the broker
                broker.Stop(removeFilesFromDisk);

                // Stop timing our life
                if (lifeTimer != null) lifeTimer.Stop();
                lifeTimer = null;
            }
            catch (Exception ex)
            {
                SendDebugMessage("Couldn't abort cleanly: " + ex.Message);
            }

            isAborted = true;
        }
        // KeepAlive is in a region below
        #endregion


        #region Segment Requests
        public bool GetSegment(int index, int SegmentNumber, ref byte[] Data, ref string txtError, bool doTranscode)
        {
            if (!IsConfigured)
            {
                txtError = "Not configured.";
                return false;
            }

            // Used in Auto stop
            lastContactAtTime = DateTime.Now;

            return broker.GetSegment(0, index, SegmentNumber, ref Data, ref txtError, doTranscode);
        }

        #endregion


        #region Auto Stop
        public DateTime lastContactAtTime;
        int SECONDS_BEFORE_AUTO_PAUSE = 35;
        const int SECONDS_BEFORE_AUTO_DIE = 6000; // 10 minutes
        public bool isPaused;
        public event EventHandler AutoDied;
        void AutoPause_Tick()
        {
            //if (Request.OnlyDump)
            //{
            //    SECONDS_BEFORE_AUTO_PAUSE = (int) Functions2.DurationOfMediaFile_OSSpecific(Request.InputFile,"")+10; //just add 10 seconds to the length before pausing
            //}
            //else 
            if (Request.LiveTV)
            {
                SECONDS_BEFORE_AUTO_PAUSE = (Request.CustomParameters.SegmentDuration) * 9;
            }
            else if (Request.NewLiveTV)
            {
                SECONDS_BEFORE_AUTO_PAUSE = (int)(95);// / (Settings.Default.SpeedFactorLiveTV * Settings.Default.SpeedFactorLiveTV));  //allow when even playing at half speed to continue
            }
            else
            {
                SECONDS_BEFORE_AUTO_PAUSE = 35;
            }
            if (! everStartedRunner) return;  // Don't die before we've even started!

            if (!isAborted)
            {
                TimeSpan timeSinceLastContact = DateTime.Now.Subtract(lastContactAtTime);
                if (!isPaused)
                {
                    if (timeSinceLastContact.TotalSeconds > SECONDS_BEFORE_AUTO_PAUSE)
                    {
                        SendDebugMessage(timeSinceLastContact.TotalSeconds.ToString() + "sec since last segment request - auto pausing streamer.");

                        isPaused = true;
                        broker.Stop(false);
                    }
                }
                else
                {
                    // We're paused... ...should we die?
                    if (timeSinceLastContact.TotalSeconds > SECONDS_BEFORE_AUTO_DIE)
                    {
                        AbortStreaming(true);

                        if (AutoDied != null) AutoDied(this, new EventArgs());
                    }
                }
            }
        }
        #endregion


        #region Incoming Events
        void broker_DebugMessage(object sender, GenericEventArgs<string> e)
        {
            SendDebugMessage(e.Value);
        }
        void broker_DebugMessage2(object sender, GenericEventArgs<string> e)
        {
            SendDebugMessage2(e.Value);
        }
        void broker_DebugMessage3(object sender, GenericEventArgs<string> e)
        {
            SendDebugMessage3(e.Value);
        }
        void broker_DebugMessage4(object sender, GenericEventArgs<string> e)
        {
            SendDebugMessage4(e.Value);
        }
        #endregion


        #region Status / Debug
        bool IsLiveStreamAvailable
        {
            get
            {
                // This counts as contact from the server
                lastContactAtTime = DateTime.Now;

                // LEGACY SUPPORT:  Live stream is always available now;
                return true;
            }
        }

        //Debug
        public event EventHandler<GenericEventArgs<string>> DebugMessage;
        public event EventHandler<GenericEventArgs<string>> DebugMessage2;
        public event EventHandler<GenericEventArgs<string>> DebugMessage3;
        public event EventHandler<GenericEventArgs<string>> DebugMessage4;
        void SendDebugMessage(string txtDebug)
        {
            // Add our ID
            txtDebug = "[" + this.ID.ToString() + "]" + txtDebug;

            if (DebugMessage != null)
                DebugMessage(this, new GenericEventArgs<string>(txtDebug));

            System.Diagnostics.Debug.Print(txtDebug);
        }
        void SendDebugMessage2(string txtDebug)
        {
            // Add our ID
            txtDebug = "[" + this.ID.ToString() + "]" + txtDebug;

            if (DebugMessage2 != null)
                DebugMessage2(this, new GenericEventArgs<string>(txtDebug));

            System.Diagnostics.Debug.Print(txtDebug);
        }
        void SendDebugMessage3(string txtDebug)
        {
            // Add our ID
            txtDebug = "[" + this.ID.ToString() + "]" + txtDebug;

            if (DebugMessage3 != null)
                DebugMessage3(this, new GenericEventArgs<string>(txtDebug));

            System.Diagnostics.Debug.Print(txtDebug);
        }
        void SendDebugMessage4(string txtDebug)
        {
            // Add our ID
            txtDebug = "[" + this.ID.ToString() + "]" + txtDebug;

            if (DebugMessage4 != null)
                DebugMessage4(this, new GenericEventArgs<string>(txtDebug));

            System.Diagnostics.Debug.Print(txtDebug);
        }
        #endregion
    }



}
