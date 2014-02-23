using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Threading;
using FatAttitude.Functions;
using System.IO.Pipes;
using WasFatAttitude;

namespace FatAttitude.MediaStreamer
{
    public class ShellCmdRunner
    {
        // Public
        public string DummyLoopOrFfmpegOrLatestffmpeg { get; set; }
        public string latestffmpeg { get; set; }
        public string latestffprobe { get; set; }
        public string ProbeFileName { get; set; }
        public string NamedPipeServer { get; set; }
        public string NamedPipeClient { get; set; }
        public string NamedPipe { get; set; }
        public string Arguments { get; set; }
        public string mappings { get; set; }
        public string inputFile { get; set; } // for newlivetv
        public string PathToTools { get; set; } // for newlivetv
        public int preferredAudioStreamIndex { get; set; } // for newlivetv
        public MediaStreamingRequest request { get; set; } // for newlivetv

        public bool DontCloseWindow { get; set; }

        private object caller;

        // Private members
        public bool IsRunning;
        public bool LiveTvIsRunning = false;
        private bool isLiveTV;
        private bool NewFFMPEG;
        private bool isNewLiveTV;
        public string ID;

        private Thread startLiveTVToPipe;
        private Process runningProcess;
        private Process ffMpegCopy;
        private Process ffMpegEncodeAndSegment;
        //private Process ffmpegProbe;
        string strQuotedFileName;
        string strQuotedFileName2;
        string quotedInputFile;
        Thread thrdReadStandardOut;
        object RunningProcessLock = new object();
        public object PipeLock = new object();
        object atomic = new object();

        private byte[] ReadBuffer = new byte[1];
        //        private BinaryWriter ffMpegSegmenterInput;

        public ShellCmdRunner(bool isLiveTV, bool isNewLiveTV, bool NewFFMPEG, string ID, object caller)
        {
            this.isLiveTV = isLiveTV;
            this.NewFFMPEG = NewFFMPEG;
            this.isNewLiveTV = isNewLiveTV;
            this.ID = ID;
            this.caller = caller;
        }

        public bool Start(ref string txtResult, int FileIndex)
        {
            ((FFHLSRunner)caller).SendDebugMessage("IsRunning : " + IsRunning);

            if (IsRunning) return false;

            ProcessStartInfo psi2 = new ProcessStartInfo();
            ((FFHLSRunner)caller).SendDebugMessage("   if (isNewLiveTV && !LiveTvIsRunning) " + isNewLiveTV + "&&" + !LiveTvIsRunning);
            {
                // Create Process for ffmpeg
                // Start Info
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.UseShellExecute = false;
                psi.CreateNoWindow = (!this.DontCloseWindow);

                string shortFN = Functions.FileWriter.GetShortPathName(this.DummyLoopOrFfmpegOrLatestffmpeg);
                string strQuotedFileName = @"""" + shortFN + @"""";
                psi.FileName = strQuotedFileName;

                string strstarttime = "";
                psi.Arguments = this.Arguments.Replace("{STARTTIME}", strstarttime);

                string rppath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "remotepotato");
                string workingdirectory = Path.Combine(rppath, "static\\mediastreams\\" + ID);
                //ffmpegProbe.StartInfo.CreateNoWindow = true;
                //ffmpegProbe.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = workingdirectory;


                // Redirect error
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardInput = false;

                // Events / Handlers
                runningProcess = new Process();
                runningProcess.EnableRaisingEvents = true;
                runningProcess.Exited += new EventHandler(runningProcess_Exited);
                runningProcess.ErrorDataReceived += new DataReceivedEventHandler(runningProcess_ErrorDataReceived);

                // Go
                Debug.Print("Running: " + psi.FileName + " " + psi.Arguments);
                runningProcess.StartInfo = psi;
                runningProcess.Start();
                runningProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
                IsRunning = true;

                /*StdOutBuffer = new byte[200000] ;
                runningProcess.StandardOutput.BaseStream.BeginRead(StdOutBuffer, 0, 256, ReadStdOut, null); 
                 THIS ISN'T WORKING; FFMPEG REFUSES TO WRITE TO STANDARD OUTPUT WHEN THIS ASYNC METHOD IS READING IT
                 */
                // Read standard output on a new thread
                thrdReadStandardOut = new Thread(new ThreadStart(ReadStandardOutput));
                thrdReadStandardOut.Priority = ThreadPriority.Lowest;
                thrdReadStandardOut.Start();

                runningProcess.BeginErrorReadLine(); // receive standard error asynchronously
            }

            //if (isNewLiveTV)
            //{
            //    string rpPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RemotePotato");
            //    string WorkingDirectory = Path.Combine(rpPath, "static\\mediastreams\\" + ID);
            //    psi.WorkingDirectory = WorkingDirectory;
            //}


            return true;
        }

        private void ReadCallBack(IAsyncResult asyncResult)
        {

            //int read;
            ////try
            ////{
            //read = ffMpegCopy.StandardOutput.BaseStream.EndRead(asyncResult);
            ////}
            ////catch (Exception e)
            ////{
            ////    Debug.Print("ffMpegCopy not started yet for this part so just skip.");
            ////    return;
            ////}
            //if (read > 0)
            //{
            //    ffMpegSegmenterInput.Write(ReadBuffer);
            //    ffMpegSegmenterInput.Flush();

            //    ffMpegCopy.StandardOutput.BaseStream.Flush();
            //    ffMpegCopy.StandardOutput.BaseStream.BeginRead(ReadBuffer, 0, ReadBuffer.Length, new AsyncCallback(ReadCallBack), null);
            //}
            //else
            //{
            //    ffMpegCopy.StandardOutput.BaseStream.Close();
            //}

        }

        bool CreateBatchFile(string batchFilePath)
        {
            StringBuilder sbBatchFile = new StringBuilder(150);

            // Short path name
            string shortFN = Functions.FileWriter.GetShortPathName(this.DummyLoopOrFfmpegOrLatestffmpeg);
            string strQuotedFileName = @"""" + shortFN + @"""";
            string strFileNameAndArguments = strQuotedFileName + " " + this.Arguments;
            sbBatchFile.AppendLine(strFileNameAndArguments);

            return FileWriter.WriteTextFileToDisk(batchFilePath, sbBatchFile.ToString(), Encoding.UTF8);
        }


        #region Kill
        volatile bool WasKilledManually = false;
        public void KillNow()
        {
            ((FFHLSRunner)caller).SendDebugMessage("if IsRunning: Killing now (manually).");

            if (!IsRunning) return;

            Debug.Print("Killing now (manually).");
            ((FFHLSRunner)caller).SendDebugMessage("Killing now (manually).");

            WasKilledManually = true;

            // Stop looking for timeouts (if indeed we are)
            EndTimeoutDetection();

            KillStandardOutputReadingThread();

            CloseOrKillRunningProcess();



            IsRunning = false;
            RaiseProcessFinishedEvent(true); // flag that it was aborted, e.g. so the FFMPGrunner doesn't write a half-read segment to disk


        }

        // test
        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        private void CloseOrKillRunningProcess()
        {
            Debug.Print("Close or kill running process.");
            ((FFHLSRunner)caller).SendDebugMessage("Close or kill running process.");

            if (!isNewLiveTV)
            {
                lock (RunningProcessLock)
                {
                    try // Raises error
                    {
                        if (!runningProcess.HasExited)
                        {
                            // Does not work LiveTVPart.CloseMainWindow();
                            //runningProcess.Close();
                            runningProcess.Kill();
                        }

                    }
                    catch (InvalidOperationException) // already killed
                    {
                        // Do nothing
                    }
                    catch
                    {
                    }
                }
            }
            else
            {
                lock (RunningProcessLock)
                {
                    try // Raises error
                    {
                        startLiveTVToPipe.Abort();
                        ffMpegEncodeAndSegment.CloseMainWindow();
                        ffMpegEncodeAndSegment.Kill();
                    }
                    catch (InvalidOperationException) // already killed
                    {
                        // Do nothing
                    }
                    catch
                    {
                    }
                }

                return;
            }
        }


        #endregion


        bool raisedProcessFinishedEvent = false;
        public event EventHandler<GenericEventArgs<processfinishedEventArgs>> ProcessFinished;
        void ffMpegEncodeAndSegment_Exited(object sender, EventArgs e)
        {
            // We're done
            Debug.Print("FFmpegEncoderAndSgementer Process finished (running process exited).");
            ((FFHLSRunner)caller).SendDebugMessage("FFmpegEncoderAndSgementer Process finished (running process exited).");

            if (!WasKilledManually)
            {
                // Stop looking for timeouts (if indeed we are)
                EndTimeoutDetection();

                KillStandardOutputReadingThread();
            }

            if (!isNewLiveTV)
            {
                startLiveTVToPipe.Abort();

                IsRunning = false;

                RaiseProcessFinishedEvent(false);
            }
            else if (!WasKilledManually)
            {
                // keep on encoding/segmenting
                ffMpegEncodeAndSegment.Start();
            }

        }

        void runningProcess_Exited(object sender, EventArgs e)
        {
            // We're done
            Debug.Print("Process finished (running process exited).");
            ((FFHLSRunner)caller).SendDebugMessage("Process finished (running process exited).");

            if (!WasKilledManually)
            {
                // Stop looking for timeouts (if indeed we are)
                EndTimeoutDetection();

                KillStandardOutputReadingThread();
            }

            IsRunning = false;

            RaiseProcessFinishedEvent(false);
        }


        private void RaiseProcessFinishedEvent(bool wasAborted)
        {
            if (raisedProcessFinishedEvent) return;

            Debug.Print("RaiseProcessFinishedEvent wasaborted=" + wasAborted);
            ((FFHLSRunner)caller).SendDebugMessage("RaiseProcessFinishedEvent wasaborted=" + wasAborted);

            raisedProcessFinishedEvent = true;

            // Raise event
            if (ProcessFinished != null)
                ProcessFinished(this, new GenericEventArgs<processfinishedEventArgs>(new processfinishedEventArgs(wasAborted)));

            IsRunning = false;
            runningProcess = null;
        }


        #region Standard Output stream capture

        public event EventHandler<GenericEventArgs<byte[]>> StandardOutputReceived;
        public object StandardOutputReceivedLock = new object();
        void ReadStandardOutput()
        {
            if (isNewLiveTV || (NewFFMPEG && ! isLiveTV))
            {
                // Time out this reader when the stream dries up; there is no other way to detect an EOS to my knowledge
                BeginTimeoutDetection();
                lock (lastReadStandardOutputLock)
                {
                    lastReadStandardOutput = DateTime.Now;  // Track the time out
                }

            }
            else
            {
                BinaryReader br;

                lock (RunningProcessLock)
                {
                    br = new BinaryReader(runningProcess.StandardOutput.BaseStream);
                }
                bool abort = false;

                // Time out this reader when the stream dries up; there is no other way to detect an EOS to my knowledge
                BeginTimeoutDetection();

                while (!abort)
                {
                    try
                    {
                        byte[] bytes;
                        lock (RunningProcessLock)
                        {
                            bytes = br.ReadBytes(1);  // Best keep at one, so when we hit EOS we've always got all the data out when it times out
                        }

                        lock (lastReadStandardOutputLock)
                        {
                            lastReadStandardOutput = DateTime.Now;  // Track the time out
                        }

                        lock (StandardOutputReceivedLock)
                        {
                            if (StandardOutputReceived != null)
                                StandardOutputReceived(this, new GenericEventArgs<byte[]>(bytes));
                        }
                    }
                    catch (EndOfStreamException)
                    {
                        abort = true;
                    }
                }
            }

        }
        private void ReadPipeOutputNewLiveTV()
        {
            // investigate on using http://stackoverflow.com/questions/5718473/c-sharp-processstartinfo-start-reading-output-but-with-a-timeout
            byte[] bytes = new byte[16 * 1024];//16 * 1024
            //http://social.msdn.microsoft.com/Forums/en-US/netfxbcl/thread/5966ab37-afec-4b96-8106-4de0fbc70040/

            BeginTimeoutDetection();

            BinaryWriter bw;
            bw = new BinaryWriter(new BufferedStream(ffMpegEncodeAndSegment.StandardInput.BaseStream));
            while (true)
            {
                NamedPipeClientStream PipeClient = new NamedPipeClientStream(".", "liveVideoStreamPipe" + ID, PipeDirection.In, PipeOptions.Asynchronous);
                //((FFHLSRunner)caller).SendDebugMessage("ShellCmdRunner: pipeclient going to connect");
                try
                {
                    PipeClient.Connect();
                    {
                        bw.Write(ReadFully(PipeClient, 0));
                        lock (lastReadStandardOutputLock)
                        {
                            lastReadStandardOutput = DateTime.Now;  // Track the time out
                        }
                    }
                    //http://stackoverflow.com/questions/895445/system-io-exception-pipe-is-broken
                    PipeClient.Close();
                }
                catch (Exception e)
                {
                    ((FFHLSRunner)caller).SendDebugMessage(e.Message);
                }
            }
            bw.Close();
            return;
        }
        /// <summary>
        /// Reads data from a stream until the end is reached. The
        /// data is written to a binarywriter. An IOException is
        /// thrown if any of the underlying IO calls fail.
        /// based on John Skeet: http://www.yoda.arachsys.com/csharp/readbinary.html
        /// </summary>
        /// <param name="stream">The stream to read data from</param>
        /// <param name="bw">The binary stream to write data to</param>
        /// <param name="initialLength">The initial buffer length</param>
        public static byte[] ReadFully(Stream stream, int initialLength)
        {
            // If we've been passed an unhelpful initial length, just
            // use 32K.
            if (initialLength < 1)
            {
                initialLength = 32768;
            }
            byte[] buffer = new byte[initialLength];
            Int32 read = 0;

            int chunk;
            while ((chunk = stream.Read(buffer, read, buffer.Length - read)) > 0)
            {
                read += chunk;

                // If we've reached the end of our buffer, check to see if there's
                // any more information
                if (read == buffer.Length)
                {
                    int nextByte = stream.ReadByte();

                    // End of stream? If so, we're done
                    if (nextByte == -1)
                    {
                        return buffer;
                    }

                    // Nope. Resize the buffer, put in the byte we've just
                    // read, and continue
                    byte[] newBuffer = new byte[buffer.Length * 2];
                    Array.Copy(buffer, newBuffer, buffer.Length);
                    newBuffer[read] = (byte)nextByte;
                    buffer = newBuffer;
                    read++;
                }
            }
            // Buffer is now too big. Shrink it.
            byte[] ret = new byte[read];
            Array.Copy(buffer, ret, read);
            return ret;
        }
        #region TimeOut
        private DateTime lastReadStandardOutput; // used to time out the reading thread 
        object lastReadStandardOutputLock = new object();
        Timer binaryReaderTimeOutTimer;
#if DEBUG
        const int ASSUME_FFMPEG_FINISHED_AFTER_TIMEOUT = 100;  // after 10 seconds of no output, we assume that we're hung on ReadBytes(1); above and at the end of the stream
#else
        const int ASSUME_FFMPEG_FINISHED_AFTER_TIMEOUT = 10;  // after 10 seconds of no output, we assume that we're hung on ReadBytes(1); above and at the end of the stream
#endif
        void BeginTimeoutDetection()
        {
            // Important - set this to prevent an immediate time out
            lock (lastReadStandardOutputLock)
            {
                lastReadStandardOutput = DateTime.Now;
            }

            TimerCallback tcb = new TimerCallback(TimeoutDetect_Tick);
            binaryReaderTimeOutTimer = new Timer(tcb, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));


        }
        void TimeoutDetect_Tick(object stateObject)
        {
            lock (lastReadStandardOutputLock)
            {
                TimeSpan timeSinceReadOutput = DateTime.Now.Subtract(lastReadStandardOutput);

                if ((!isLiveTV) && (!isNewLiveTV) && (!(NewFFMPEG && !isLiveTV)) && timeSinceReadOutput.TotalSeconds >= ASSUME_FFMPEG_FINISHED_AFTER_TIMEOUT)  // we never want LiveTV and newffmpeg to time out
                {
                    EndTimeoutDetection();
                    DoStandardOutputTimeOut();

                }
            }
        }
        void DoStandardOutputTimeOut()
        {
            Debug.Print("*** SHELLCMDRUNNER TIMEOUT :  Standard Output stopped for more than " + ASSUME_FFMPEG_FINISHED_AFTER_TIMEOUT.ToString() + " seconds - timing out.");
            ((FFHLSRunner)caller).SendDebugMessage("isnewlivetv=" + isNewLiveTV + "*** SHELLCMDRUNNER TIMEOUT :  Standard Output stopped for more than " + ASSUME_FFMPEG_FINISHED_AFTER_TIMEOUT.ToString() + " seconds - timing out.");

            // Kill the whole process, which will raise the Process Finished event
            if (!IsRunning) return;

            // Stop looking for timeouts (if indeed we are)
            EndTimeoutDetection();

            // Kill thread and/or process
            KillStandardOutputReadingThread();
            CloseOrKillRunningProcess();

            RaiseProcessFinishedEvent(false); // flag that it was NOT aborted, so the FFMPGrunner gets its final data segment

            IsRunning = false;
        }
        private void KillStandardOutputReadingThread()
        {
            // Kill STDOUT reading thread
            if (thrdReadStandardOut != null)
            {
                try
                {
                    thrdReadStandardOut.Abort();
                    thrdReadStandardOut = null;
                }
                catch { }
            }
        }
        void EndTimeoutDetection()
        {
            if (binaryReaderTimeOutTimer == null) return;

            // End timer
            binaryReaderTimeOutTimer.Dispose();
            binaryReaderTimeOutTimer = null;
        }
        #endregion

        #endregion

        #region Standard Error Redirection
        public event EventHandler<GenericEventArgs<string>> StandardErrorReceivedLine;
        public event EventHandler<GenericEventArgs<string>> StandardErrorReceivedLine2;
        public event EventHandler<GenericEventArgs<string>> StandardErrorReceivedLine3;
        public event EventHandler<GenericEventArgs<string>> StandardErrorReceivedLine4;
        void runningProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (StandardErrorReceivedLine != null)
                StandardErrorReceivedLine(this, new GenericEventArgs<string>(e.Data));
        }
        void LiveTVPart_ErrorDataReceived3(object sender, DataReceivedEventArgs e)
        {
            if (StandardErrorReceivedLine3 != null)
                StandardErrorReceivedLine3(this, new GenericEventArgs<string>(e.Data));
        }
        void LiveTVPart_ErrorDataReceived2(object sender, GenericEventArgs<string> e)
        {
            if (StandardErrorReceivedLine2 != null)
            {
                if (e.Value == null) return; // this is actually required.  wow.
                StandardErrorReceivedLine2(this, new GenericEventArgs<string>(e.Value));
            }
        }
        void LiveTVPart_ErrorDataReceived4(object sender, GenericEventArgs<string> e)
        {
            if (StandardErrorReceivedLine4 != null)
            {
                if (e.Value == null) return; // this is actually required.  wow.
                StandardErrorReceivedLine4(this, new GenericEventArgs<string>(e.Value));
            }
        }
        void LiveTVPart_ErrorDataReceived3(object sender, GenericEventArgs<string> e)
        {
            if (StandardErrorReceivedLine3 != null)
            {
                if (e.Value == null) return; // this is actually required.  wow.
                StandardErrorReceivedLine3(this, new GenericEventArgs<string>(e.Value));
            }
        }
        void LiveTVPart_ErrorDataReceived(object sender, GenericEventArgs<string> e)
        {
            if (StandardErrorReceivedLine != null)
            {
                if (e.Value == null) return; // this is actually required.  wow.
                StandardErrorReceivedLine(this, new GenericEventArgs<string>(e.Value));
            }
        }
        #endregion


        void DebugMessage2(object sender, GenericEventArgs<string> e)
        {
            // Pass up
            ((FFHLSRunner)caller).SendDebugMessage2(e.Value);
        }
        void DebugMessage(object sender, GenericEventArgs<string> e)
        {
            // Pass up
            ((FFHLSRunner)caller).SendDebugMessage(e.Value);
        }

        //private int index(byte[] toBeSearched, byte[] pattern)
        //{
        //    //http://stackoverflow.com/questions/283456/byte-array-pattern-search
        //    string needle, haystack;

        //    unsafe
        //    {
        //        fixed (byte* p = pattern)
        //        {
        //            needle = new string((SByte*)p, 0, pattern.Length);
        //        } // fixed

        //        fixed (byte* p2 = toBeSearched)
        //        {
        //            haystack = new string((SByte*)p2, 0, toBeSearched.Length);
        //        } // fixed

        //        return haystack.IndexOf(needle, 0);
        //    }
        //}

    }

    public class processfinishedEventArgs
    {
        public bool WasAborted { get; set; }

        public processfinishedEventArgs(bool wasAbort)
        {
            WasAborted = wasAbort;
        }
    }

}
