using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using System.Threading;
using System.Management;
using FatAttitude.MediaStreamer.HLS;
using System.Globalization;
using WasFatAttitude;
using RemotePotatoServer.Properties;

namespace FatAttitude.MediaStreamer
{
    internal class FFHLSRunner
    {
        public string InputFile { get; set; }
        public string WorkingDirectory { get; set; }
        public string AdditionalArgsString { get; set; }
        public bool SettingsDefaultDebugAdvanced { get; set; }
        public bool Transcode { get; set; }
        public bool IsRunning;
        int StartAtSeconds = 0;
        public int startAtSegment = 0;

        // Private
        SegmentStore Store;
        private ShellCmdRunner shellRunner;
        private CommandArguments cmdArguments;

        private CommandArguments segmentArguments;
        public VideoEncodingParameters EncodingParameters;
        private MediaStreamingRequest request;
        public string MapArgumentsString;
        private string PathToTools;
        public string ID;

        private double secondsToStartNextTime = 0;

        //private double CurrentPosition; //in seconds
        //private double CurrentOverallPosition; //in seconds

        public FFHLSRunner(string pathToTools, SegmentStore segStore)
        {
            // From parameters
            PathToTools = pathToTools;
            Store = segStore;
            ID = segStore.ID;
            // Defaults
            EncodingParameters = new VideoEncodingParameters();
            AudioSyncAmount = 1;  // 2 can create streaming issues
        }
        public FFHLSRunner(string pathToTools, SegmentStore store, MediaStreamingRequest mrq)
            : this(pathToTools, store)
        {
            request = mrq;
            EncodingParameters = mrq.CustomParameters;
            if (request.LiveTV)
            {
                MaxIncomingDataSize = 20000000; // 20Mb per segment maximum size
            }
            if (request.LiveTV || request.NewLiveTV)
            {
                EncodingParameters.SegmentDuration = request.ActualSegmentDuration;//LiveTV
            }
            else
            {
                EncodingParameters.SegmentDuration = 4;
            }
        }

        #region Top Level - Start/Stop/IsRunning
        public bool IsReStarting = false;
        int SegmentDuration;
        public bool Start(int _startAtSegment, ref string txtResult, int FileIndex)
        {
            AwaitingSegmentNumber = _startAtSegment; // need to set this pretty sharpish
            startAtSegment = _startAtSegment; // need to set this pretty sharpish
            AwaitingFileIndex = FileIndex;
            if (request.LiveTV)// || request.NewLiveTV)
            {
                // Start up slowly
                SegmentDuration = Math.Min(EncodingParameters.SegmentDuration, request.InitialWaitTimeBeforeLiveTVStarts + _startAtSegment * request.SegmentIncreasingStepsLiveTV); //starting at 16 seconds make segments q second bigger untill 60 seconds reached
                int q = request.InitialWaitTimeBeforeLiveTVStarts;
                int r = request.SegmentIncreasingStepsLiveTV;
                int x = SegmentDuration;
                StartAtSeconds = (x - q) * (x + q - r) / (2 * r); //This is the general quadratic solution to the valuse x=4,y=0, x=4+a, y=4, x=4+2a, y=4+4+a etc. 
                double StartAtSegmentWhereCalculatedSegmentDurationIsMax = (EncodingParameters.SegmentDuration - q) / r;
                double StartAtSecondsWhereCalculatedSegmentDurationIsMax = (Math.Min(q + (int)StartAtSegmentWhereCalculatedSegmentDurationIsMax * r, EncodingParameters.SegmentDuration)
                    - q) * (Math.Min(q + (int)StartAtSegmentWhereCalculatedSegmentDurationIsMax * r, EncodingParameters.SegmentDuration) + q - r) / (2 * r);
                if (_startAtSegment >= StartAtSegmentWhereCalculatedSegmentDurationIsMax)
                {
                    StartAtSeconds = (int)(StartAtSecondsWhereCalculatedSegmentDurationIsMax + (_startAtSegment - (int)(StartAtSegmentWhereCalculatedSegmentDurationIsMax)) * EncodingParameters.SegmentDuration);
                }
                //                }
                SendDebugMessage("SegmentDuration: ********* " + SegmentDuration + "start at: " + StartAtSeconds);
            }
            else
            {
                SegmentDuration = EncodingParameters.SegmentDuration;
                if (_startAtSegment>=99999)
                    StartAtSeconds = 0;
                else
                StartAtSeconds = _startAtSegment * EncodingParameters.SegmentDuration;
            }

            if (IsRunning)
            {
                IsReStarting = true;
                SendDebugMessage("Aborting, then restarting");
                Abort();
            }

            Initialise(_startAtSegment, ID);

            IsReStarting = false;
            SendDebugMessage("Running: " + shellRunner.DummyLoopOrFfmpegOrLatestffmpeg);
            SendDebugMessage("Arguments: " + shellRunner.Arguments);
            //if (_startAtSegment == 0) Thread.Sleep (500+request.InitialWaitTimeBeforeLiveTVStarts*1000);

            SendDebugMessage("Starting shllcmdrunner: ");

            IsRunning = shellRunner.Start(ref txtResult, FileIndex);

            if (request.NewLiveTV || request.UseNewerFFMPEG)// ||request.UseNewerFFMPEG)
            {
                Thread cancellock = new Thread(unlocksegments); // cancel the lock on segment retrieval!
                cancellock.Start();
            }

            return IsRunning;
        }
        void unlocksegments()  //especially for newlivetv, since segments are created by the latest ffmpeg (segmenter)
        {
            while (IsRunning)
            {
                Store.CancelWaitingSegments();
                Thread.Sleep(100);
            }
        }

        void Initialise(int startAtSegment, string ID)
        {
            shellRunner = new ShellCmdRunner(request.LiveTV, request.NewLiveTV, request.UseNewerFFMPEG, ID, this);
            shellRunner.ProcessFinished += new EventHandler<GenericEventArgs<processfinishedEventArgs>>(shellRunner_ProcessFinished);
            if (request.NewLiveTV)
            {
                shellRunner.PathToTools = PathToTools;
                shellRunner.preferredAudioStreamIndex = request.UseAudioStreamIndex;
                shellRunner.latestffmpeg = Path.Combine(PathToTools, "ffmpeglatest.exe");
                shellRunner.latestffprobe = Path.Combine(PathToTools, "ffprobelatest.exe");
                shellRunner.DummyLoopOrFfmpegOrLatestffmpeg = Path.Combine(PathToTools, "dummyloop.bat");
                shellRunner.ProbeFileName = Path.Combine(PathToTools, "ffprobelatest.exe");
                shellRunner.mappings = (string.IsNullOrEmpty(MapArgumentsString)) ? "" : MapArgumentsString;
                shellRunner.request = request;
                shellRunner.NamedPipeServer = Path.Combine(PathToTools, "NamedPipeServer.exe");
            }
            else if (request.UseNewerFFMPEG)
                shellRunner.DummyLoopOrFfmpegOrLatestffmpeg = Path.Combine(PathToTools, "ffmpeglatest.exe");
            else
                shellRunner.DummyLoopOrFfmpegOrLatestffmpeg = Path.Combine(PathToTools, "ffmpeg.exe");
            shellRunner.StandardErrorReceivedLine += new EventHandler<GenericEventArgs<string>>(shellRunner_StandardErrorReceivedLine);
            shellRunner.StandardErrorReceivedLine2 += new EventHandler<GenericEventArgs<string>>(shellRunner_StandardErrorReceivedLine2);
            shellRunner.StandardErrorReceivedLine3 += new EventHandler<GenericEventArgs<string>>(shellRunner_StandardErrorReceivedLine3);
            shellRunner.StandardErrorReceivedLine4 += new EventHandler<GenericEventArgs<string>>(shellRunner_StandardErrorReceivedLine4);
            shellRunner.StandardOutputReceived += new EventHandler<GenericEventArgs<byte[]>>(shellRunner_StandardOutputReceived);

            // Incoming data from ffmpeg STDOUt - mangements
            InitTempWriter();

            // Set up objects
            cmdArguments = new CommandArguments();
            segmentArguments = new CommandArguments();

            // Arguments
            ConstructArguments(startAtSegment);
            shellRunner.Arguments = cmdArguments.ToString();
            shellRunner.inputFile = InputFile;

        }
        public void Abort()
        {
            SendDebugMessage("FF Runner: if IsRunning then going to abort.");


            if (!IsRunning) return;

            // insert kill file into the directory to stop the streamer
            SendDebugMessage("FF Runner: Abort signalled.");

            if (shellRunner != null)
            {
                lock (shellRunner.StandardOutputReceivedLock)
                {
                    // Unhook events, they are static and thus not garbagecollected
                    shellRunner.StandardErrorReceivedLine -= new EventHandler<GenericEventArgs<string>>(shellRunner_StandardErrorReceivedLine);
                    shellRunner.StandardErrorReceivedLine2 -= new EventHandler<GenericEventArgs<string>>(shellRunner_StandardErrorReceivedLine2);
                    shellRunner.StandardErrorReceivedLine3 -= new EventHandler<GenericEventArgs<string>>(shellRunner_StandardErrorReceivedLine3);
                    shellRunner.StandardErrorReceivedLine4 -= new EventHandler<GenericEventArgs<string>>(shellRunner_StandardErrorReceivedLine4);
                    shellRunner.StandardOutputReceived -= new EventHandler<GenericEventArgs<byte[]>>(shellRunner_StandardOutputReceived);
                    shellRunner.ProcessFinished -= new EventHandler<GenericEventArgs<processfinishedEventArgs>>(shellRunner_ProcessFinished);
                }

                // Kill the shell runner 
                shellRunner.KillNow();
            }

            shellRunner = null;
            IsRunning = false;
        }
        #endregion

        #region Incoming Events

        void shellRunner_ProcessFinished(object sender, GenericEventArgs<processfinishedEventArgs> e)
        {
            if (sender != shellRunner) return;
            if (request.LiveTV)
            {

                //if (VideoEncodingParameters.EOSDetected)

                //                VideoEncodingParameters.EOSDetected = false;
                SendDebugMessage("FF Runner: Shell process is finished in live TV.  (");
                shellRunner = null;
                IsRunning = false;
                if (!e.Value.WasAborted) //finished normally
                {
                    string txtResult = "";
                    SendDebugMessage("FF Runner finished normally: Restart runner for the following segments");

                    beginNextSegment();
                    this.Start(incomingSegment.Number, ref txtResult, incomingSegment.FileIndex);
                }
            }
            else if (request.NewLiveTV)
            {

                SendDebugMessage("FF Runner: Shell process is finished in new live TV.  (");
                shellRunner = null;
                IsRunning = false;
                if (!e.Value.WasAborted) //finished normally
                {
                    SendDebugMessage("FF Runner finished normally.");
                }
            }
            else
            {
                if (!e.Value.WasAborted) // If finished normally, write the [final] segment to disk
                {
                    processRemainingBytes();
                    finaliseCurrentSegment();
                }
                SendDebugMessage("FF Runner: Shell process is finished in non-live tv.  (");
                shellRunner = null;
                IsRunning = false;
            }

        }
        #endregion


        #region Incoming Segments
        // Members
        BinaryWriter bw;
        List<byte> byteHoldingBuffer;
        int delimiterLength;
        public int AwaitingSegmentNumber { get; set; }
        public int AwaitingFileIndex { get; set; }
        byte[] Delimiter = null;

        // Methods
        void InitTempWriter()
        {
            byteHoldingBuffer = new List<byte>();

            initDelimiter();

            beginNextSegment();
        }
        void initDelimiter()
        {
            //if (Delimiter == null) Delimiter = System.Text.UTF8Encoding.UTF8.GetBytes(  "-------SEGMENT-BREAK-------");
            if (Delimiter == null) Delimiter = System.Text.UTF8Encoding.UTF8.GetBytes("-SEGBREAK-");
            delimiterLength = 10;
        }
        void shellRunner_StandardOutputReceived(object sender, GenericEventArgs<byte[]> e)
        {
            if (sender != shellRunner) return;  // an old shell runner

            processByteBuffer(e.Value);
        }
        void processByteBuffer(byte[] bytes)
        {
            foreach (byte b in bytes)
            {
                processByte(b);
            }
        }
        bool startedPumping = false;
        void processByte(byte b)
        {
            lock (byteHoldingBuffer)
            {
                byteHoldingBuffer.Add(b);
                if (byteHoldingBuffer.Count == delimiterLength)
                {
                    if (!startedPumping) startedPumping = true;

                    // Check for a match - if it matches then dump the byte buffer and start a new file
                    if (byteHoldingBuffer.SequenceEqual(Delimiter))
                    {
                        byteHoldingBuffer.Clear();
                        AwaitingSegmentNumber++; //not so interested in fileindex here, since only writing segments
                        switchToNextSegment();
                    }
                    else
                    {
                        if (bw.BaseStream.Position < MaxIncomingDataSize)
                        {
                            // dequeue the first byte (FIFO) and write it to disk
                            bw.Write(byteHoldingBuffer[0]);
                            byteHoldingBuffer.RemoveAt(0);
                        }
                        else
                        {
                            SendDebugMessage("WARNING: Data spill; segment exceeded max (" + MaxIncomingDataSize.ToString() + ") size.");
                            return;
                        }
                    }
                }

            }
        }
        void processRemainingBytes()
        {
            while (byteHoldingBuffer.Count > 0)
            {
                if (bw.BaseStream.Position < MaxIncomingDataSize)
                {
                    // dequeue the first byte (FIFO) and write it to disk
                    bw.Write(byteHoldingBuffer[0]);
                    byteHoldingBuffer.RemoveAt(0);
                }
                else
                    SendDebugMessage("WARNING: Data spill; segment exceeded max (" + MaxIncomingDataSize.ToString() + ") size.");
            }
        }
        void switchToNextSegment()
        {
            finaliseCurrentSegment();

            beginNextSegment();

        }
        void finaliseCurrentSegment()
        {
            // Transfer the data across from the buffer
            incomingSegment.Data = new byte[bw.BaseStream.Position];
            bw.Flush();
            bw.Close();

            Array.Copy(incomingSegmentDataBuffer, incomingSegment.Data, incomingSegment.Data.Length);
            incomingSegmentDataBuffer = null;  // clear/dispose

            StoreSegment();
        }
        private void StoreSegment()
        {
            // Store the segment
            Store.StoreSegment(incomingSegment);
        }
        Segment incomingSegment = null;

        private int MaxIncomingDataSize = 2000000;  // 2Mb per segment maximum size
        byte[] incomingSegmentDataBuffer;
        void beginNextSegment()
        {
            incomingSegment = new Segment();

            incomingSegment.Number = AwaitingSegmentNumber;
            incomingSegment.FileIndex = AwaitingFileIndex;

            incomingSegmentDataBuffer = new byte[MaxIncomingDataSize];
            MemoryStream ms = new MemoryStream(incomingSegmentDataBuffer);
            bw = new BinaryWriter(ms);
        }
        #endregion

        #region Parameters
        public int AudioSyncAmount { get; set; }


        /*
         * C:\Program Files (x86)\AirVideoServer\ffmpeg.exe" 
         * --segment-length 4
         * --segment-offset 188
         * --conversion-id 548cf790-c04f-488a-96be-aae2968f272bdefd0e1d-2bdf-457d-ab15-3eb6c51ccf85 
         * --port-number 46631 
         * -threads 4 
         * -flags +loop 
         * -g 30 -keyint_min 1 
         * -bf 0 
         * -b_strategy 0 
         * -flags2 -wpred-dct8x8 -cmp +chroma -deblockalpha 0 -deblockbeta 0 -refs 1 -coder 0 -me_range 16 -subq 5 -partitions +parti4x4+parti8x8+partp8x8 
         * -trellis 0 
         * -sc_threshold 40 -i_qfactor 0.71 -qcomp 0.6 -map 0.1:0.1 -map 0.0:0.0 -ss 188.0 
         * -vf "crop=720:572:0:2, scale=568:320"
         * -aspect 720:576
         * -y
         * -async 1
         * -f mpegts
         * -vcodec libx264
         * -bufsize 1024k
         * -b 1200k
         * -bt 1300k
         * -qmax 48
         * -qmin 2
         * -r 25.0
         * -acodec libmp3lame
         * -ab 192k
         * -ar 48000
         * -ac 2 
         */
        int segment_start_number = 0;

        private void ConstructArguments(int startAtSegment)
        {

            MediaInfoGrabber g = null;
//            if (!request.NewLiveTV)
            {
                //Get info on intput file...
                SendDebugMessage("MediaInfoGrabber: Setting up prober in HLSRunner...");
                g = new MediaInfoGrabber(PathToTools, Environment.GetEnvironmentVariable("tmp"), InputFile);
                if (request.NewLiveTV)
                {
                    //g.GetInfo2("ffmpeg.exe", "");
                }
                else if (request.LiveTV)
                {
                    g.GetInfo2("ffmpeg.exe", "");
                }
                else
                {
                    g.GetInfo("ffmpeg.exe", "");
                }
            }

            string args ="";
         //   if (Settings.Default.UseOsmo4)
           // {
                //request.UseNewerFFMPEG = Settings.Default.UseOsmo4;
                //string rppath = Path.Combine(
                //    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "remotepotato");
                //string workingdirectory = Path.Combine(rppath, "static\\mediastreams\\" + ID + "\\");
                //args =
                //    @"{THREADS} {STARTTIME} {INPUTFILE} {MAPPINGS} -vbsf h264_mp4toannexb -flags -global_header {H264PROFILE} -level 30 -preset ultrafast {USEGPU} -flags +loop -g 30 -keyint_min 1 -bf 0 -b_strategy 0 -weightb 0 -8x8dct 0 -deblock 0:0 -cmp chroma -refs 1 {MOTIONSEARCHRANGE} {SUBQ} {PARTITIONS} -trellis 0 -coder 0 -sc_threshold 40 -i_qfactor 0.71 -qcomp 0.6 -qdiff 4 -rc_eq 'blurCplx^(1-qComp)' {ASPECT} {FRAMESIZE} {DEINTERLACE} -y {AUDIOSYNC} -vcodec libx264 {VIDEOBITRATE} {VIDEOBITRATEDEVIATION} -qmax 48 -qmin 2 -r {FRAMERATE} {AUDIOCODEC} {AUDIOBITRATE} {AUDIOSAMPLERATE} {AUDIOCHANNELS} {VOLUMELEVEL} -force_key_frames expr:gte(t,n_forced*" +
                //    SegmentDuration + ") -f segment -segment_time " + SegmentDuration + " -segment_list " +
                //    workingdirectory + "index2.m3u8 -segment_start_number {SEGMENTSTARTNR} segment-%d.ts";
        //    }
          //  else 
        if (request.NewLiveTV)
                //                args = @" {INPUTFILE} -y {THREADS} {H264PROFILE} {H264LEVEL} -flags +loop -g 30 -keyint_min 1 -b_strategy 0 {MOTIONSEARCHRANGE} {SUBQ} {PARTITIONS} -trellis 0 -coder 0 -sc_threshold 40 -i_qfactor 0.71 -qcomp 0.6 -qdiff 4 -rc_eq 'blurCplx^(1-qComp)' {MAPPINGS} {AUDIOCODEC} {AUDIOBITRATE} {MAXAUDIOBITRATE} {MINAUDIOBITRATE} {AUDIOSAMPLERATE} {AUDIOCHANNELS} -c:v libx264 {AUDIOSYNC} {ASPECT} -vbsf h264_mp4toannexb {FRAMESIZE} {VIDEOBITRATE} {MAXVIDEOBITRATE} {MINVIDEOBITRATE} {VIDEOBITRATEDEVIATION} -qmax 48 -qmin 2 -flags -global_header -f segment {SEGMENTLENGTH} {INDEXFILE} {segment_start_number} seg-%d.ts";
                //                args = @" {INPUTFILE} -map 0 -y {THREADS} -c copy -f segment {SEGMENTLENGTH} {INDEXFILE} {segment_start_number} liveseg-%d.ts";
                args = "";
            //                args = @" {INPUTFILE} -y {THREADS} {MAPPINGS} {AUDIOCODEC} {AUDIOBITRATE} {MAXAUDIOBITRATE} {MINAUDIOBITRATE} {AUDIOSAMPLERATE} {AUDIOCHANNELS} -c:v libx264 -profile:v baseline -level 1 {AUDIOSYNC} {ASPECT} -vbsf h264_mp4toannexb {FRAMESIZE} {VIDEOBITRATE} {MAXVIDEOBITRATE} {MINVIDEOBITRATE} {VIDEOBITRATEDEVIATION}  -flags -global_header c:\scratch\scratch.ts";
            else if (request.LiveTV)
                //                args = @"{THREADS} {H264PROFILE} {H264LEVEL} -flags +loop -g 30 -keyint_min 1 -b_strategy 0 -flags2 -wpred-dct8x8 -cmp +chroma -deblockalpha 0 -deblockbeta 0 -refs 1 {MOTIONSEARCHRANGE} {SUBQ} {PARTITIONS} -trellis 0 -coder 0 -sc_threshold 40 -i_qfactor 0.71 -qcomp 0.6 -qdiff 4 -rc_eq 'blurCplx^(1-qComp)' {MAPPINGS} {STARTTIME} {INPUTFILE} {AUDIOSYNC} {ASPECT} {FRAMESIZE} {DEINTERLACE} -y -f mpegts -vcodec libx264 {VIDEOBITRATE} {MINVIDEOBITRATE} {MAXVIDEOBITRATE} {VIDEOBITRATEDEVIATION} -qmax 48 -qmin 2 -r 25 {AUDIOCODEC} {AUDIOBITRATE} {AUDIOSAMPLERATE} {AUDIOCHANNELS} {VOLUMELEVEL}";
                args =
                    @"{THREADS} {H264PROFILE} {H264LEVEL}  {USEGPU}  -flags +loop -g 30 -keyint_min 1 -b_strategy 0 -flags2 -wpred-dct8x8 -cmp +chroma -deblockalpha 0 -deblockbeta 0 -refs 1 {MOTIONSEARCHRANGE} {SUBQ} {PARTITIONS} -trellis 0 -coder 0 -sc_threshold 40 -i_qfactor 0.71 -qcomp 0.6 -qdiff 4 -rc_eq 'blurCplx^(1-qComp)' {MAPPINGS} {STARTTIME} {INPUTFILE} {AUDIOSYNC} {ASPECT} {FRAMESIZE} {DEINTERLACE} -y -f mpegts -vcodec libx264 {VIDEOBITRATE} {MINVIDEOBITRATE} {MAXVIDEOBITRATE} {VIDEOBITRATEDEVIATION} -qmax 48 -qmin 2 -r {FRAMERATE} {AUDIOCODEC} {AUDIOBITRATE} {AUDIOSAMPLERATE} {AUDIOCHANNELS} {VOLUMELEVEL}";
                // see for efficiency: http://smorgasbork.com/component/content/article/35-linux/98-high-bitrate-real-time-mpeg-2-encoding-with-ffmpeg as well
            else if (request.UseNewerFFMPEG && !request.NewLiveTV && !request.LiveTV)
            {
                string rppath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "remotepotato");
                string workingdirectory = Path.Combine(rppath, "static\\mediastreams\\" + ID + "\\");
                args =

                    @"{THREADS} {STARTTIME} {INPUTFILE} {MAPPINGS} -vbsf h264_mp4toannexb -flags -global_header {H264PROFILE} {H264LEVEL} {USEGPU} -flags +loop -g 30 -keyint_min 1 -bf 0 -b_strategy 0 -weightb 0 -8x8dct 0 -deblock 0:0 -cmp chroma -refs 1 {MOTIONSEARCHRANGE} {SUBQ} {PARTITIONS} -trellis 0 -coder 0 -sc_threshold 40 -i_qfactor 0.71 -qcomp 0.6 -qdiff 4 -rc_eq 'blurCplx^(1-qComp)' {ASPECT} {FRAMESIZE} {DEINTERLACE} -y {AUDIOSYNC} -vcodec libx264 {VIDEOBITRATE} {VIDEOBITRATEDEVIATION} -qmax 48 -qmin 2 -r {FRAMERATE} {AUDIOCODEC} {AUDIOBITRATE} {AUDIOSAMPLERATE} {AUDIOCHANNELS} {VOLUMELEVEL} -force_key_frames expr:gte(t,n_forced*" +
                    SegmentDuration + ") -f segment -segment_time " + SegmentDuration + " -segment_list " +
                    workingdirectory + "index2.m3u8 -segment_start_number {SEGMENTSTARTNR} segment-%d.ts";
                //args = @"{THREADS} {INPUTFILE} -map 0:0 -map 0:1 -vbsf h264_mp4toannexb -flags -global_header {H264PROFILE} {H264LEVEL} -flags +loop -g 30 -keyint_min 1 -bf 0 -b_strategy 0 -weightb 0 -8x8dct 0 -deblock 0:0 -cmp chroma -refs 1 {MOTIONSEARCHRANGE} {SUBQ} {PARTITIONS} -trellis 0 -coder 0 -sc_threshold 40 -i_qfactor 0.71 -qcomp 0.6 -qdiff 4 -rc_eq 'blurCplx^(1-qComp)' {STARTTIME} {ASPECT} {FRAMESIZE} {DEINTERLACE} -y {AUDIOSYNC} -vcodec libx264 {VIDEOBITRATE} {VIDEOBITRATEDEVIATION} -qmax 48 -qmin 2 -r {FRAMERATE} {AUDIOCODEC} {AUDIOBITRATE} {AUDIOSAMPLERATE} {AUDIOCHANNELS} {VOLUMELEVEL}  -f segment -segment_time 3 -segment_list " + workingdirectory + "index2.m3u8 segment-%d.ts";
            }
            else // never change a winning team:
                //                args = @"{THREADS} {H264PROFILE} {H264LEVEL} -flags +loop -g 30 -keyint_min 1 -bf 0 -b_strategy 0 -flags2 -wpred-dct8x8 -cmp +chroma -deblockalpha 0 -deblockbeta 0 -refs 1 {MOTIONSEARCHRANGE} {SUBQ} {PARTITIONS} -trellis 0 -coder 0 -sc_threshold 40 -i_qfactor 0.71 -qcomp 0.6 -qdiff 4 -rc_eq 'blurCplx^(1-qComp)' {MAPPINGS} {STARTTIME} {INPUTFILE} {ASPECT} {FRAMESIZE} {DEINTERLACE} -y {AUDIOSYNC} -f mpegts -vcodec libx264 {VIDEOBITRATE} {VIDEOBITRATEDEVIATION} -qmax 48 -qmin 2 -r {FRAMERATE} {AUDIOCODEC} {AUDIOBITRATE} {AUDIOSAMPLERATE} {AUDIOCHANNELS} {VOLUMELEVEL} -scodec copy";
                args =
                    @"{THREADS} {H264PROFILE} {H264LEVEL} {USEGPU} -flags +loop -g 30 -keyint_min 1 -bf 0 -b_strategy 0 -flags2 -wpred-dct8x8 -cmp +chroma -deblockalpha 0 -deblockbeta 0 -refs 1 {MOTIONSEARCHRANGE} {SUBQ} {PARTITIONS} -trellis 0 -coder 0 -sc_threshold 40 -i_qfactor 0.71 -qcomp 0.6 -qdiff 4 -rc_eq 'blurCplx^(1-qComp)' {MAPPINGS} {STARTTIME} {INPUTFILE} {ASPECT} {FRAMESIZE} {DEINTERLACE} -y {AUDIOSYNC} -f mpegts -vcodec libx264 {VIDEOBITRATE} {VIDEOBITRATEDEVIATION} -qmax 48 -qmin 2 -r {FRAMERATE} {AUDIOCODEC} {AUDIOBITRATE} {AUDIOSAMPLERATE} {AUDIOCHANNELS} {VOLUMELEVEL} ";

            //if (request.LiveTV)
            //            args = @"{THREADS} {H264PROFILE} {H264LEVEL} -flags +loop -g 30 -keyint_min 1 -b_strategy 0 -flags2 -wpred-dct8x8 -cmp +chroma -deblockalpha 0 -deblockbeta 0 -refs 1 {MOTIONSEARCHRANGE} {SUBQ} {PARTITIONS} -trellis 0 -coder 0 -sc_threshold 40 -i_qfactor 0.71 -qcomp 0.6 -qdiff 4 -rc_eq 'blurCplx^(1-qComp)' {MAPPINGS} {STARTTIME} {INPUTFILE} {AUDIOSYNC} {ASPECT} {FRAMESIZE} {DEINTERLACE} -y -f mpegts -vcodec libx264 {VIDEOBITRATE} {MINVIDEOBITRATE} {MAXVIDEOBITRATE} {VIDEOBITRATEDEVIATION} -qmax 48 -qmin 2 -r 25 {AUDIOCODEC} {AUDIOBITRATE} {AUDIOSAMPLERATE} {AUDIOCHANNELS} {VOLUMELEVEL}";
            //// see for efficiency: http://smorgasbork.com/component/content/article/35-linux/98-high-bitrate-real-time-mpeg-2-encoding-with-ffmpeg as well
            //else // never change a winning team:
            //    args = @"{THREADS} {H264PROFILE} {H264LEVEL} -flags +loop -g 30 -keyint_min 1 -bf 0 -b_strategy 0 -flags2 -wpred-dct8x8 -cmp +chroma -deblockalpha 0 -deblockbeta 0 -refs 1 {MOTIONSEARCHRANGE} {SUBQ} {PARTITIONS} -trellis 0 -coder 0 -sc_threshold 40 -i_qfactor 0.71 -qcomp 0.6 -qdiff 4 -rc_eq 'blurCplx^(1-qComp)' {MAPPINGS} {STARTTIME} {INPUTFILE} {AUDIOSYNC} {ASPECT} {FRAMESIZE} {DEINTERLACE} -y -f mpegts -vcodec libx264 {VIDEOBITRATE} {VIDEOBITRATEDEVIATION} -qmax 48 -qmin 2 -r 25 {AUDIOCODEC} {AUDIOBITRATE} {AUDIOSAMPLERATE} {AUDIOCHANNELS} {VOLUMELEVEL}";

            // Use either the standard ffmpeg template or a custom one
            string strFFMpegTemplate = (string.IsNullOrWhiteSpace(EncodingParameters.CustomFFMpegTemplate)) ? args : EncodingParameters.CustomFFMpegTemplate;

            if (request.NewLiveTV)
            {
                //                strFFMpegTemplate = strFFMpegTemplate.Replace("{SEGMENTLENGTH}", "-segment_time " + SegmentDuration.ToString());
                strFFMpegTemplate = strFFMpegTemplate.Replace("{SEGMENTLENGTH}", "-segment_time 4");  // this results in segments ABOUT this size of 4 seconds
                //    strFFMpegTemplate = strFFMpegTemplate.Replace("{SEGMENTLENGTH}", " -segment_list_flags +live ");
                // Segment length and offset
            }
            else if (request.LiveTV)
            {// start with 4 seconds first then gradually increase up to 1 minute of segmentlength
                segmentArguments.AddArgCouple("--segment-length", SegmentDuration.ToString());
                segmentArguments.AddArgCouple("--segment-offset", StartAtSeconds.ToString());
                cmdArguments.AddArg(segmentArguments.ToString());
            }
            else if (!request.UseNewerFFMPEG)
            {
                segmentArguments.AddArgCouple("--segment-length", EncodingParameters.SegmentDuration.ToString());
                segmentArguments.AddArgCouple("--segment-offset", StartAtSeconds.ToString());
                cmdArguments.AddArg(segmentArguments.ToString());
            }


            // Multi threads
            if (!(request.LiveTV || request.NewLiveTV || (request.UseNewerFFMPEG && !request.LiveTV)))
            {// never change a winning team:
                strFFMpegTemplate = strFFMpegTemplate.Replace("{THREADS}", "-threads 4");
                strFFMpegTemplate = strFFMpegTemplate.Replace("{USEGPU}", " ");
            }
            //if ((Settings.Default.applyLiveTVSettingsToVideosAsWell && (request.UseNewerFFMPEG && !request.LiveTV)) || request.NewLiveTV )
            // {
            //     strFFMpegTemplate = strFFMpegTemplate.Replace("{THREADS}",
            //                                                  "-threads " + Settings.Default.NumberOfCoresX264);
            //     strFFMpegTemplate = strFFMpegTemplate.Replace("{USEGPU}",
            //                                                   (Settings.Default.GPUtranscode
            //                                                        ? "  -x264opts ref=9:opencl:crf=16.0 "
            //                                                        : " "));
            // }
            //else 
            {
                strFFMpegTemplate = strFFMpegTemplate.Replace("{THREADS}", "-threads 8");
                strFFMpegTemplate = strFFMpegTemplate.Replace("{USEGPU}", " ");
            }

            string rate = "50";
            SendDebugMessage("frame rate set to: " + rate);
            if (g.Info.Success && (g.Info.AVVideoStreams.Count > 0))
            {
                if (!request.NewLiveTV && !request.LiveTV && g.Info.Success)
                {
                    rate = g.Info.AVVideoStreams[0].frameRate;
                    SendDebugMessage("However detected frame rate is, so set to: " + rate);
                }
            }
            double rateD;
            double.TryParse(rate, NumberStyles.Any, CultureInfo.InvariantCulture, out rateD);
            if (rateD > 31)
            {
                SendDebugMessage("Computed frame rate:" + (rateD / 2));
                strFFMpegTemplate = strFFMpegTemplate.Replace("{FRAMERATE}", "" + (rateD / 2)); //If over 30Hz, assume 50 or 59.94Hz and convert to 25 or 29.97 
            }
            else
                strFFMpegTemplate = strFFMpegTemplate.Replace("{FRAMERATE}", rate);

            // Me Range
            strFFMpegTemplate = strFFMpegTemplate.Replace("{MOTIONSEARCHRANGE}", ("-me_range " + EncodingParameters.MotionSearchRange.ToString()));

            // SUBQ - important as setting it too high can slow things down
            strFFMpegTemplate = strFFMpegTemplate.Replace("{SUBQ}", ("-subq " + EncodingParameters.X264SubQ.ToString()));

            // Partitions
            string strPartitions = (EncodingParameters.PartitionsFlags.Length > 0) ?
                "-partitions " + EncodingParameters.PartitionsFlags : "";
            strFFMpegTemplate = strFFMpegTemplate.Replace("{PARTITIONS}", strPartitions);

            // Add Mappings
            if (((true || request.UseNewerFFMPEG) && !request.NewLiveTV))// for newlivetv change mappings in shellcmdrunner
            {
                string strMapArgs = (string.IsNullOrEmpty(MapArgumentsString)) ? "" : MapArgumentsString;
                strFFMpegTemplate = strFFMpegTemplate.Replace("{MAPPINGS}", strMapArgs);
            } 
            else if (!request.NewLiveTV)
                strFFMpegTemplate = strFFMpegTemplate.Replace("{MAPPINGS}", "");

            // Start at : MUST BE BEFORE INPUT FILE FLAG -i *** !!! 
            string strStartTime;
            if (request.NewLiveTV)
            {
                strStartTime = (secondsToStartNextTime <= 0) ? "" : ("-ss " + secondsToStartNextTime.ToString());
            }
            else
            {
                strStartTime = (StartAtSeconds <= 0) ? "" : ("-ss " + StartAtSeconds.ToString());
            }

            strFFMpegTemplate = strFFMpegTemplate.Replace("{STARTTIME}", strStartTime);

            int segstart = (startAtSegment >= 99999) ? 0 : startAtSegment;
            strFFMpegTemplate = strFFMpegTemplate.Replace("{SEGMENTSTARTNR}", segstart.ToString());

            //// for liveTV, -async 1 alone does not seem to work, so:
            //string strVideoAudioSync = (EncodingParameters.AVSyncDifference <= 0) ? "" : ("-itsoffset " + EncodingParameters.AVSyncDifference.ToString(CultureInfo.InvariantCulture));
            //strFFMpegTemplate = strFFMpegTemplate.Replace("{ITOFFSET}", strVideoAudioSync);

            // Input file - make short to avoid issues with UTF-8 in batch files  IT IS VERY IMPORTANT WHERE THIS GOES; AFTER SS BUT BEFORE VCODEC AND ACODEC
            if (request.NewLiveTV)
            {
                //                strFFMpegTemplate = strFFMpegTemplate.Replace("{INPUTFILE}", (" -ab 256000 -vb 10000000 -mbd rd -trellis 2 -cmp 2 -subcmp 2 -g 100 -f mpeg -i -")); //let it catch the pipe
                //strFFMpegTemplate = strFFMpegTemplate.Replace("{INPUTFILE}", (" -f mpeg -i -")); //let it catch the pipe
                string shortInputFile = Functions.FileWriter.GetShortPathName(InputFile);
                // Quotes around file
                string quotedInputFile = "\"" + shortInputFile + "\"";
                strFFMpegTemplate = strFFMpegTemplate.Replace("{INPUTFILE}", ("-i " + quotedInputFile));
            }
            else
            {
                string shortInputFile = Functions.FileWriter.GetShortPathName(InputFile);
                // Quotes around file
                string quotedInputFile = "\"" + shortInputFile + "\"";
                strFFMpegTemplate = strFFMpegTemplate.Replace("{INPUTFILE}", ("-i " + quotedInputFile));
            }

            if (request.NewLiveTV)
            {
                string workingFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RemotePotato");
                workingFolderPath = Path.Combine(workingFolderPath + "\\static\\mediastreams\\", ID);
                if (!Directory.Exists(workingFolderPath)) Directory.CreateDirectory(workingFolderPath);
                string quotedIndexFile = "\"" + workingFolderPath + "\\livetv5.m3u8" + "\"";
                strFFMpegTemplate = strFFMpegTemplate.Replace("{INDEXFILE}", ("-segment_list " + quotedIndexFile));

                shellRunner.NamedPipe = Path.Combine(workingFolderPath, "PipestreamPath");
            }



            // Aspect ratio and frame size
            string asp = (request.NewLiveTV || (request.UseNewerFFMPEG && !request.LiveTV)) ? "-aspect:v " : "-aspect ";
            string strAspectRatio = (EncodingParameters.OutputSquarePixels) ? asp + "1:1" : asp + EncodingParameters.AspectRatio;
            strFFMpegTemplate = strFFMpegTemplate.Replace("{ASPECT}", strAspectRatio);
            string strFrameSize = ((request.NewLiveTV || (request.UseNewerFFMPEG && !request.LiveTV)) ? "-s:v " : "-s ") + EncodingParameters.ConstrainedSize;
            strFFMpegTemplate = strFFMpegTemplate.Replace("{FRAMESIZE}", strFrameSize);


            // Deinterlace (experimental)
            string strDeinterlace = (EncodingParameters.DeInterlace) ? "-deinterlace" : "";
            //string strdeinterlace = (encodingparameters.deinterlace) ? "-deinterlace" : "";
            strFFMpegTemplate = strFFMpegTemplate.Replace("{DEINTERLACE}", strDeinterlace);

            // OPTIONAL FOR LATER:  -vf "crop=720:572:0:2, scale=568:320"
            // Think this means crop to the aspect ratio, then scale to the normal frame

            // Audio sync amount
            string strAudioSync = "-async " + AudioSyncAmount.ToString();
            strFFMpegTemplate = strFFMpegTemplate.Replace("{AUDIOSYNC}", strAudioSync);

            int maxDuration = EncodingParameters.SegmentDuration;
            int currentSegmentDuration = Math.Min(EncodingParameters.SegmentDuration, request.InitialWaitTimeBeforeLiveTVStarts + startAtSegment * request.SegmentIncreasingStepsLiveTV); ;
            int videobitrate = 0;
            int audiobitrate = 0;
            // Video bitrate
            if (request.LiveTV)
            {
                if (maxDuration == 0)
                {
                }
                else
                {
                    videobitrate = (int)(toInteger(EncodingParameters.VideoBitRate) * (Math.Sin((Math.PI) * currentSegmentDuration / (2 * maxDuration))));//sin seems to be a nice fast climbing function
                    //                    audiobitrate = (int)(toInteger(EncodingParameters.AudioBitRate) * (Math.Sin((Math.PI) * currentSegmentDuration / (2 * maxDuration))));
                    audiobitrate = toInteger(EncodingParameters.AudioBitRate);
                    //videobitrate = (int)(toInteger(EncodingParameters.VideoBitRate) * (currentSegmentDuration / maxDuration)); //linear
                    //audiobitrate = (int)(toInteger(EncodingParameters.AudioBitRate) * (currentSegmentDuration / maxDuration));
                    SendDebugMessage("VideoBitRate now:  " + videobitrate);
                }
                string strVideoBitRateOptions = "-bufsize " + "50Mi -b " + videobitrate;  //cmdArguments.AddArgCouple("-maxrate", VideoBitRate);
                strFFMpegTemplate = strFFMpegTemplate.Replace("{VIDEOBITRATE}", strVideoBitRateOptions);
            }
            else
            {
                string strVideoBitRateOptions = "-bufsize " + EncodingParameters.VideoBitRate + ((request.NewLiveTV || (request.UseNewerFFMPEG && !request.LiveTV)) ? " -b:v " : " -b ") + EncodingParameters.VideoBitRate;  //cmdArguments.AddArgCouple("-maxrate", VideoBitRate);
                strFFMpegTemplate = strFFMpegTemplate.Replace("{VIDEOBITRATE}", strVideoBitRateOptions);
            }
            if (request.NewLiveTV || (request.UseNewerFFMPEG  && !request.LiveTV))
            {
                // Max video bitrate (optional)
                string strMaxVideoBitRate = "-maxrate " + EncodingParameters.VideoBitRate;
                strFFMpegTemplate = strFFMpegTemplate.Replace("{MAXVIDEOBITRATE}", strMaxVideoBitRate);
                string strMinVideoBitRate = "-minrate " + EncodingParameters.VideoBitRate;
                strFFMpegTemplate = strFFMpegTemplate.Replace("{MINVIDEOBITRATE}", strMinVideoBitRate);
                string strMaxAudioBitRate = "-maxrate " + EncodingParameters.AudioBitRate;
                strFFMpegTemplate = strFFMpegTemplate.Replace("{MAXAUDIOBITRATE}", strMaxAudioBitRate);
                string strMinAudioBitRate = "-minrate " + EncodingParameters.AudioBitRate;
                strFFMpegTemplate = strFFMpegTemplate.Replace("{MINAUDIOBITRATE}", strMinAudioBitRate);

                string strVideoBitRateDeviation = "";
                strFFMpegTemplate = strFFMpegTemplate.Replace("{VIDEOBITRATEDEVIATION}", strVideoBitRateDeviation);
            }
            else if (request.LiveTV)
            {
                // Max video bitrate (optional)
                string strMaxVideoBitRate = "-maxrate " + videobitrate;
                strFFMpegTemplate = strFFMpegTemplate.Replace("{MAXVIDEOBITRATE}", strMaxVideoBitRate);
                string strMinVideoBitRate = "-minrate " + videobitrate;
                strFFMpegTemplate = strFFMpegTemplate.Replace("{MINVIDEOBITRATE}", strMinVideoBitRate);
                string strMaxAudioBitRate = "-maxrate " + audiobitrate;
                strFFMpegTemplate = strFFMpegTemplate.Replace("{MAXAUDIOBITRATE}", strMaxAudioBitRate);
                string strMinAudioBitRate = "-minrate " + audiobitrate;
                strFFMpegTemplate = strFFMpegTemplate.Replace("{MINAUDIOBITRATE}", strMinAudioBitRate);

                string strVideoBitRateDeviation = "";
                strFFMpegTemplate = strFFMpegTemplate.Replace("{VIDEOBITRATEDEVIATION}", strVideoBitRateDeviation);
            }
            else
            {
                // Max video bitrate (optional)
                string strMaxVideoBitRate = "-maxrate " + EncodingParameters.VideoBitRate;
                strFFMpegTemplate = strFFMpegTemplate.Replace("{MAXVIDEOBITRATE}", strMaxVideoBitRate);

                string strVideoBitRateDeviation = "-bt " + EncodingParameters.BitRateDeviation;
                strFFMpegTemplate = strFFMpegTemplate.Replace("{VIDEOBITRATEDEVIATION}", strVideoBitRateDeviation);
            }


            // Restrict H264 encoding level (e.g. for iPhone 3G)
            string strH264Level = (EncodingParameters.X264Level > 0) ? ("-level " + EncodingParameters.X264Level.ToString()) : "";
            strFFMpegTemplate = strFFMpegTemplate.Replace("{H264LEVEL}", strH264Level);
            string strH264Profile = (string.IsNullOrWhiteSpace(EncodingParameters.X264Profile)) ? "" : ((request.NewLiveTV || (request.UseNewerFFMPEG && !request.LiveTV)) ? "-profile:v " : "-profile ") + EncodingParameters.X264Profile;
            strFFMpegTemplate = strFFMpegTemplate.Replace("{H264PROFILE}", strH264Profile);

            // Audio: MP3 - must be after input file flag -i  //
            string strAudioCodecOptions = "";
            string cod = (request.NewLiveTV || (request.UseNewerFFMPEG && !request.LiveTV)) ? "-c:a " : "-acodec ";
            switch (EncodingParameters.AudioCodec)
            {
                case VideoEncodingParameters.AudioCodecTypes.NONE:
                    strAudioCodecOptions = " -an ";
                    break;

                case VideoEncodingParameters.AudioCodecTypes.AAC:
                    strAudioCodecOptions = cod + "aac -strict experimental ";
                    break;

                default:
                    strAudioCodecOptions = cod + "libmp3lame";
                    break;
                //// "libfaac");
            }
            if (request.NewLiveTV && EncodingParameters.AudioCodec!=VideoEncodingParameters.AudioCodecTypes.NONE)
            {
                strAudioCodecOptions = cod + " aac -strict experimental ";
            }
            strFFMpegTemplate = strFFMpegTemplate.Replace("{AUDIOCODEC}", strAudioCodecOptions);

            // Audio Bitrate
            string strAudioBitRate = "";
            if (request.LiveTV)
            {
                strAudioBitRate = "-ab " + audiobitrate;
            }
            else if (request.NewLiveTV || (request.UseNewerFFMPEG && !request.LiveTV))
            {
                strAudioBitRate = "-ab:a " + EncodingParameters.AudioBitRate;
            }
            else
            {
                strAudioBitRate = "-ab " + EncodingParameters.AudioBitRate;
            }
            strFFMpegTemplate = strFFMpegTemplate.Replace("{AUDIOBITRATE}", strAudioBitRate);

            // Audio sample rate
            string strAudioSampleRate = ((request.NewLiveTV || (request.UseNewerFFMPEG && !request.LiveTV)) ? "-ar:a " : "-ar ") + "44100";//EncodingParameters.AudioSampleRate; 48000 results in no sound on android!!!
            strFFMpegTemplate = strFFMpegTemplate.Replace("{AUDIOSAMPLERATE}", strAudioSampleRate);

            // Force stereo
            string strAudioChannels = ((request.NewLiveTV || (request.UseNewerFFMPEG && !request.LiveTV)) ? "-ac:a 2 " : "-ac 2 ");
            strFFMpegTemplate = strFFMpegTemplate.Replace("{AUDIOCHANNELS}", strAudioChannels);

            // Volume Level
            string strVolumeBoost = "";
            if (EncodingParameters.AudioVolumePercent != 100)
            {
                double fVolumeBytes = (256.0 * (EncodingParameters.AudioVolumePercent / 100.0));
                int iVolumeBytes = Convert.ToInt32(fVolumeBytes);
                strVolumeBoost = "-vol " + iVolumeBytes.ToString();
            }
            strFFMpegTemplate = strFFMpegTemplate.Replace("{VOLUMELEVEL}", strVolumeBoost);

            if (request.NewLiveTV)
            {
                //http://stackoverflow.com/questions/1179970/c-sharp-find-most-recent-file-in-dir
                var directory = new DirectoryInfo(WorkingDirectory);
                int LatestSegmentNr = 0;
                if (File.Exists(WorkingDirectory + "\\liveseg-1.ts"))
                {
                    var myFile = directory.GetFiles("*.ts").OrderByDescending(f => f.LastWriteTime).First();
                    string bestand = myFile.Name;
                    bestand = bestand.Replace(".ts", ""); // remove extension
                    // Get segment number
                    string strSegNumber;
                    List<string> parts = bestand.Split('-').ToList();
                    if (parts.Count > 1)
                    {
                        strSegNumber = parts[1];
                        if (!int.TryParse(strSegNumber, out LatestSegmentNr))
                        {
                        }
                    }
                }

                //                strFFMpegTemplate = strFFMpegTemplate.Replace("{segment_times}", "-segment_times 4,20,40,60");
                strFFMpegTemplate = strFFMpegTemplate.Replace("{segment_times}", "");
                strFFMpegTemplate = strFFMpegTemplate.Replace("{segment_start_number}", "-segment_start_number " + Math.Max(1, LatestSegmentNr + 1));
            }
            else if (!(request.UseNewerFFMPEG && !request.LiveTV) && !request.NewLiveTV)
            {
                // Pipe to segmenter (ie send to standard output now)
                strFFMpegTemplate = strFFMpegTemplate + " -";
            }


            // Commit - add to the arguments
            cmdArguments.AddArg(strFFMpegTemplate);

        }
        string croppedSizeForAspectRatio()
        {
            return "";
        }
        #endregion

        #region Debug
        public event EventHandler<GenericEventArgs<string>> DebugMessage;
        public event EventHandler<GenericEventArgs<string>> DebugMessage2;
        public event EventHandler<GenericEventArgs<string>> DebugMessage3;
        public event EventHandler<GenericEventArgs<string>> DebugMessage4;
        void shellRunner_StandardErrorReceivedLine(object sender, GenericEventArgs<string> e)
        {
            if (sender != shellRunner) return;
            if (e.Value == null) return; // this is actually required.  wow.

            //            VideoEncodingParameters.EOSDetected = (e.Value.Contains("Warning MVs not available"));

            //if (e.Value.Contains("time=") && e.Value.Contains("bitrate=")) //keep track of duration of media being played, does not work when user has used seek function!!!, e.g. in case of livetv?
            //{  //problem: debug info is not unique, can be made unique by putting streamid in debuginfo????????
            //    string part = e.Value;
            //    part = part.Substring(part.IndexOf("time=")+5);
            //    part = part.Substring(0,part.IndexOf(" "));
            //    double.TryParse(part, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out CurrentPosition);//extract the time
            //    SendDebugMessage("^^&^^&*&*(&(*^^(*" + CurrentPosition.ToString());
            //}


            if (SettingsDefaultDebugAdvanced)
                SendDebugMessage("[C]" + e.Value);
        }

        void shellRunner_StandardErrorReceivedLine2(object sender, GenericEventArgs<string> e)
        {
            if (sender != shellRunner) return;
            if (e.Value == null) return; // this is actually required.  wow.

            if (SettingsDefaultDebugAdvanced)
                SendDebugMessage2("[C] to wtv's:" + e.Value);
        }

        void shellRunner_StandardErrorReceivedLine3(object sender, GenericEventArgs<string> e)
        {
            if (sender != shellRunner) return;
            if (e.Value == null) return; // this is actually required.  wow.

            if (SettingsDefaultDebugAdvanced)
                SendDebugMessage3("[C] FFMpeg to measure duration" + e.Value);
        }

        void shellRunner_StandardErrorReceivedLine4(object sender, GenericEventArgs<string> e)
        {
            if (sender != shellRunner) return;
            if (e.Value == null) return; // this is actually required.  wow.

            if (SettingsDefaultDebugAdvanced)
                SendDebugMessage4("[C] to pipe2:" + e.Value);
        }

        public void SendDebugMessage(string txtDebug)
        {
            if (DebugMessage != null)
                DebugMessage(this, new GenericEventArgs<string>(txtDebug));
        }
        public void SendDebugMessage2(string txtDebug)
        {
            if (DebugMessage2 != null)
                DebugMessage2(this, new GenericEventArgs<string>(txtDebug));
        }
        public void SendDebugMessage3(string txtDebug)
        {
            if (DebugMessage3 != null)
                DebugMessage3(this, new GenericEventArgs<string>(txtDebug));
        }
        public void SendDebugMessage4(string txtDebug)
        {
            if (DebugMessage4 != null)
                DebugMessage4(this, new GenericEventArgs<string>(txtDebug));
        }
        void prober_DebugMessage(object sender, GenericEventArgs<string> e)
        {
            SendDebugMessage(e.Value);
        }
        //void grabber_DebugMessage(object sender, GenericEventArgs<string> e)
        //{
        //    WriteLineToLogFile(e.Value);
        //}
        //static object writeLogLock = new object();
        //public static string DebugLogFileFN;
        //static List<string> StoredLogEntries;
        //public static void WriteLineToLogFile(string txtLine)
        //{
        //    Monitor.Enter(writeLogLock);
        //    string logLine = System.String.Format("{0:O}: {1}.", System.DateTime.Now, txtLine);

        //    System.IO.StreamWriter sw;
        //    try
        //    {
        //        sw = System.IO.File.AppendText(DebugLogFileFN);
        //    }
        //    catch
        //    {
        //        // Store the log entry for later
        //        if (StoredLogEntries.Count < 150)  // limit
        //            StoredLogEntries.Add(logLine);

        //        Monitor.Exit(writeLogLock);
        //        return;
        //    }

        //    try
        //    {
        //        // Write any pending log entries
        //        if (StoredLogEntries.Count > 0)
        //        {
        //            foreach (string s in StoredLogEntries)
        //            {
        //                sw.WriteLine(s);
        //            }
        //            StoredLogEntries.Clear();
        //        }

        //        sw.WriteLine(logLine);
        //    }
        //    finally
        //    {
        //        sw.Close();
        //    }

        //    Monitor.Exit(writeLogLock);
        //}

        #endregion

        #region helpers
        private static int toInteger(string input)
        {
            if (input.IndexOf("k") == -1)
            {
                return Convert.ToInt32(input);
            }
            else
            {
                input = input.Replace("k", "");
                return Convert.ToInt32(input) * 1024;
            }
        }
        #endregion
        #region probing

        public TimeSpan GetMediaDuration(string fileName)
        {
            MediaInfoGrabber grabber = new MediaInfoGrabber(Functions2.ToolkitFolder, Path.Combine(Functions2.StreamBaseFolder, "probe_results"), fileName);
            grabber.DebugMessage += new EventHandler<GenericEventArgs<string>>(grabber_DebugMessage);
            grabber.GetInfo("ffmpeg.exe", "");
            grabber.DebugMessage -= grabber_DebugMessage;

            TimeSpan duration = (grabber.Info.Success) ? grabber.Info.Duration : new TimeSpan(0);

            return duration;
        }

        void grabber_DebugMessage(object sender, GenericEventArgs<string> e)
        {
            Functions2.WriteLineToLogFile(e.Value);
        }

        #endregion
    }


}
