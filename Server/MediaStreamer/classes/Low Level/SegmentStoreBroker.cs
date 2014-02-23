using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using FatAttitude.Collections;
using WasFatAttitude;

namespace FatAttitude.MediaStreamer.HLS
{
    internal partial class SegmentStoreBroker
    {
        public bool SettingsDefaultDebugAdvanced { set; get; }
        MediaStreamingRequest Request;
        SegmentStore store;
        string PathToTools;
        string MapArguments;//= "-map 0:0 -map 0:1";
        string WorkingDirectory;
        FFHLSRunner Runner;

        const int NUMBER_OF_SEGMENTS_CONSIDERED_COMING_SOON = 5;
        const int NUMBER_OF_SEGMENTS_CONSIDERED_COMING_SOON_LIVETV = 5;
        const int NUMBER_OF_SEGMENTS_CONSIDERED_COMING_SOON_NEWLIVETV = 3;
        const int NUMBER_OF_SEGMENTS_CONSIDERED_COMING_SOON_NEWFFMPEG = 5;

        // Constructor
        internal SegmentStoreBroker(string ID, MediaStreamingRequest _request, string pathToTools)
        {
            Request = _request;
            store = new SegmentStore(ID, Request);
            PathToTools = pathToTools;

            string rpPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RemotePotato");
            WorkingDirectory = Path.Combine(rpPath, "static\\mediastreams\\" + ID.ToString());
            if (!Directory.Exists(WorkingDirectory)) Directory.CreateDirectory(WorkingDirectory);

            if (Request.NewLiveTV)
            {
                //done in shelCmdRunner
                return;
            }

            // Probe / Map Audio streams:
            if (Request.NewLiveTV || Request.UseAudioStreamIndex >= 0 || Request.UseSubtitleStreamIndex >= 0 || (Request.UseNewerFFMPEG && !Request.LiveTV))
            {
                SendDebugMessage("MediaStreamer: Mapping streams using requested audio stream " + Request.UseAudioStreamIndex.ToString());
                // We still have to probe again to discover the index of the video stream, as both A and V MUST be mapped, can't just map one
                while (true)
                {
                    MapArguments = GetProbeMapArguments(Request.UseAudioStreamIndex);
                    if (string.IsNullOrEmpty(MapArguments)) // May take a small while
                    {
                        SendDebugMessage("Probing failed, trying again");
                    }
                    else
                    {
                        SendDebugMessage("Success! Mappings are :" + MapArguments);
                        break;
                    }
                }
            }
            /*  QUICKSTART CODE
            // So far, so good.  If the required segment is available then don't bother starting transcoding; it could already be cached to disk.
            // Otherwise, let's start transcoding, as the client is likely to request the segment soon.
            double dStartingSegment = Math.Floor(Convert.ToDouble(Request.StartAt) / Convert.ToDouble(Runner.EncodingParameters.SegmentDuration));
            int iStartingSegment = Convert.ToInt32(dStartingSegment);
            if (! CanGetSegment(iStartingSegment))  // cant get segment
            {
                string txtResult = "";
                if (!Runner.Start(iStartingSegment, ref txtResult))
                    throw new Exception("The FFRunner failed to start : " + txtResult);
            }*/
        }

        bool Start(int segmentNumber, ref string txtResult, int FileIndex)
        {
            lock (createNewRunnerLock)
            {
                CreateNewRunner();

                return Runner.Start(segmentNumber, ref txtResult, FileIndex);
            }
        }
        internal void Stop(bool deleteFiles)
        {
            // Stop and destroy the current runner
            if (Runner != null)
                DestroyRunner();

            if (deleteFiles)
                DeleteAllSegmentsFromDisk();
        }


        #region Runner
        object createNewRunnerLock = new object();
        void CreateNewRunner()
        {

            SendDebugMessage("Broker] Cancelling waiting segments");
            store.CancelWaitingSegments();

            if (Runner != null)
                DestroyRunner();

            SendDebugMessage("broker] Creating new runner.");

            Runner = new FFHLSRunner(PathToTools, store, Request);

            // Set runner variables
            Runner.SettingsDefaultDebugAdvanced = SettingsDefaultDebugAdvanced;
            Runner.MapArgumentsString = MapArguments;
            Runner.WorkingDirectory = WorkingDirectory;
            Runner.InputFile = Request.InputFile;

            // Hook runner events
            Runner.DebugMessage += new EventHandler<GenericEventArgs<string>>(Runner_DebugMessage);
            Runner.DebugMessage2 += new EventHandler<GenericEventArgs<string>>(Runner_DebugMessage2);
            Runner.DebugMessage3 += new EventHandler<GenericEventArgs<string>>(Runner_DebugMessage3);
            Runner.DebugMessage4 += new EventHandler<GenericEventArgs<string>>(Runner_DebugMessage4);
        }
        void DestroyRunner()
        {
            lock (createNewRunnerLock)
            {
                SendDebugMessage("broker] if runner!= null then destroy old runner.");

                if (Runner == null) return;

                SendDebugMessage("broker] Destroying old runner.");

                Runner.Abort();

                UnwireRunner();
                Runner = null;
            }
        }
        void UnwireRunner()
        {
            if (Runner == null) return;

            Runner.DebugMessage -= new EventHandler<GenericEventArgs<string>>(Runner_DebugMessage);
            Runner.DebugMessage2 -= new EventHandler<GenericEventArgs<string>>(Runner_DebugMessage2);
            Runner.DebugMessage3 -= new EventHandler<GenericEventArgs<string>>(Runner_DebugMessage3);
            Runner.DebugMessage4 -= new EventHandler<GenericEventArgs<string>>(Runner_DebugMessage4);
        }
        #endregion

        // TOP LEVEL
        internal bool GetSegment(int recurseLevel, int index, int SegmentNumber, ref byte[] Data, ref string txtError, bool doTranscode) // wrapped because of the stackoverflow variable
        {
            //lock (createNewRunnerLock)  // Mustnt try to retrieve a segment while we're restarting the runner
            {

                SendDebugMessage("Segment " + index + "-" + SegmentNumber.ToString() + " requested: trying to get...");
                Segment retrievedSegment = null; SegmentAvailabilities availability = SegmentAvailabilities.IsAvailable;
                if (TryGetSegment(index, SegmentNumber, ref retrievedSegment, ref availability, doTranscode))
                {
                    Data = retrievedSegment.Data;
                    SendDebugMessage("RETURNING segment " + index + "-" + SegmentNumber.ToString());
                    return true;
                }
                else
                {
                    // What happened
                    if (availability == SegmentAvailabilities.IsError)
                    {
                        SendDebugMessage("Segment " + index + "-" + SegmentNumber.ToString() + " errored.");
                        txtError = "Segment could not be retrieved due to an error.";
                        return false;
                    }
                    else if (availability == SegmentAvailabilities.Cancelled)
                    {
                        SendDebugMessage("Segment " + index + "-" + SegmentNumber.ToString() + " cancelled.");
                        txtError = "Segment was cancelled, possibly due to a seek request.";
                        return false;
                    }
                    else if (availability == SegmentAvailabilities.RequiresSeek)
                    {
                        SendDebugMessage("Segment " + index + "-" + SegmentNumber.ToString() + " requires a seek - (re)starting runner.");

                        // Create a new runner and start it  (cancels any waiting segments)
                        string txtResult = "";
                        if (!Start(SegmentNumber, ref txtResult, index))
                        {
                            SendDebugMessage("Segment could not be retrieved due to the FFRunner failing to start : " +
                                             txtResult);
                            txtError = "Segment could not be retrieved due to the FFRunner failing to start : " +
                                       txtResult;
                            return false;
                        }
                        else
                        {
                            SendDebugMessage("New runner started....");
                        }


                        // RUNNER re-started, so let's recurse as the segment availability will now be 'coming soon'
                        // Recurse
                        if ((recurseLevel++) < 4)
                        {
                            SendDebugMessage("GetSegment recursing level: " + recurseLevel);
                            return GetSegment(recurseLevel, index, SegmentNumber, ref Data, ref txtError, doTranscode);
                        }
                        else
                        {
                            txtError = "Recursion level overflow.";
                            return false;
                        }
                    }

                    // Shouldnt get here
                    return false;
                }
            }
        }
        bool TryGetSegment(int index, int segmentNumber, ref Segment retrievedSegment, ref SegmentAvailabilities segAvailability, bool doTranscode)
        {
            
            if (store.HasSegment(index, segmentNumber) || !doTranscode)
            {
                //                SendDebugMessage("Broker] Segment " + index + "-" + segmentNumber.ToString() + " is available in store - retrieving");
                SendDebugMessage("Broker] Segment " + segmentNumber.ToString() + " is available in store - retrieving");
                bool foo = store.TryGetSegmentByNumber(index, segmentNumber, ref retrievedSegment, doTranscode);
                // shouldn't block, as it's in the store
                segAvailability = SegmentAvailabilities.IsAvailable;
                return true;
            }


            // Is there a runner
            if (Runner == null)
            {
                SendDebugMessage("Broker] require seek (runner stopped)");
                segAvailability = SegmentAvailabilities.RequiresSeek;  // require, in fact!
                return false;
            }


            // Store does not have segment.  Is it coming soon?
            //int difference2 = (index - Runner.AwaitingFileIndex);
//            int difference = (segmentNumber - Runner.AwaitingSegmentNumber);
            int difference = (segmentNumber - Runner.AwaitingSegmentNumber);
    //            if (difference2 < 0 || (difference2 == 0 && difference < 0)) // requested segment is in past
            if (difference < 0)// requested segment is in past
            {
                //                SendDebugMessage("Broker] Seg " + index + "-" + segmentNumber.ToString() + " is in the past - require seek.");
                SendDebugMessage("Broker] Seg " + segmentNumber.ToString() + " is in the past - require seek.");
                segAvailability = SegmentAvailabilities.RequiresSeek;
                return false;
            }

           //            if (difference >= NUMBER_OF_SEGMENTS_CONSIDERED_COMING_SOON)
                //if (Request.NewLiveTV) && (difference2 >= NUMBER_OF_SEGMENTS_CONSIDERED_COMING_SOON_NEWLIVETV))
                //{
                //    //                SendDebugMessage("Broker] Seg " + index + "-" + segmentNumber.ToString() + " is a huge " + difference2 + " indexfiles away from arrival - require seek.");
                //    SendDebugMessage("Broker] Seg " + segmentNumber.ToString() + " is a huge " + difference2 + " indexfiles away from arrival - require seek.");
                //    segAvailability = SegmentAvailabilities.RequiresSeek;
                //    return false;
                //}
                //else
                    if ((!Request.LiveTV && !Request.NewLiveTV && (difference >= NUMBER_OF_SEGMENTS_CONSIDERED_COMING_SOON)) ||
                    (Request.LiveTV && Request.NewLiveTV && (difference >= NUMBER_OF_SEGMENTS_CONSIDERED_COMING_SOON_LIVETV)))
                    {
                        //SendDebugMessage("Broker] Seg " + index + "-" + segmentNumber.ToString() + " is a huge " + difference + " segs away from arrival - require seek.");
                        SendDebugMessage("Broker] Seg " + segmentNumber.ToString() + " is a huge " + difference + " segs away from arrival - require seek.");
                        segAvailability = SegmentAvailabilities.RequiresSeek;
                        return false;
                    }

            // WAIT FOR A SEGMENT **************************************************************
            //             SendDebugMessage("Broker] Seg " + index + "-" + segmentNumber.ToString() + " is only " + difference2 + " indexfiles away from arrival - requesting from store, which will block...");
            SendDebugMessage("Broker] Seg " + segmentNumber.ToString() + " - requesting from store, which will block...");

            bool didGet = (store.TryGetSegmentByNumber(index, segmentNumber, ref retrievedSegment, doTranscode));
            segAvailability = didGet ? SegmentAvailabilities.IsAvailable : SegmentAvailabilities.Cancelled;
            SendDebugMessage("Broker] Seg " + segmentNumber.ToString() + " did get = " + didGet);
            //segAvailability = segmentNumber == 99999 ? SegmentAvailabilities.IsAvailable : segAvailability;  //NewLiveTV hack, s.t. non available waitsscreen gets displayed
            //return (didGet || segmentNumber==99999);//99999 is newlivtv hack
            return didGet;
        }
        internal bool CanGetSegment(int index, int segmentNumber)
        {
            return (store.HasSegment(index, segmentNumber));
        }
        internal void DeleteAllSegmentsFromDisk()
        {
            store.DeleteAllStoredSegmentsFromDisk();
        }


        #region Helpers
        string GetProbeMapArguments(int preferredAudioStreamIndex)
        {
            FFMPGProber prober = new FFMPGProber();
            bool result = prober.Probe("ffmpeglatest.exe", "", PathToTools, Request.InputFile, WorkingDirectory, preferredAudioStreamIndex);

            if (!result)
                return "";

            return prober.mapArguments.ToString();
        }
        #endregion

        #region Debug
        void Runner_DebugMessage(object sender, GenericEventArgs<string> e)
        {
            // Pass up
            SendDebugMessage(e.Value);
        }
        void Runner_DebugMessage2(object sender, GenericEventArgs<string> e)
        {
            // Pass up
            SendDebugMessage2(e.Value);
        }
        void Runner_DebugMessage3(object sender, GenericEventArgs<string> e)
        {
            // Pass up
            SendDebugMessage3(e.Value);
        }
        void Runner_DebugMessage4(object sender, GenericEventArgs<string> e)
        {
            // Pass up
            SendDebugMessage4(e.Value);
        }
        public event EventHandler<GenericEventArgs<string>> DebugMessage;
        public event EventHandler<GenericEventArgs<string>> DebugMessage2;
        public event EventHandler<GenericEventArgs<string>> DebugMessage3;
        public event EventHandler<GenericEventArgs<string>> DebugMessage4;
        void SendDebugMessage(string txtDebug)
        {
            if (DebugMessage != null)
                DebugMessage(this, new GenericEventArgs<string>(txtDebug));
        }
        void SendDebugMessage2(string txtDebug)
        {
            if (DebugMessage2 != null)
                DebugMessage2(this, new GenericEventArgs<string>(txtDebug));
        }
        void SendDebugMessage3(string txtDebug)
        {
            if (DebugMessage3 != null)
                DebugMessage3(this, new GenericEventArgs<string>(txtDebug));
        }
        void SendDebugMessage4(string txtDebug)
        {
            if (DebugMessage4 != null)
                DebugMessage4(this, new GenericEventArgs<string>(txtDebug));
        }
        #endregion



    }
}
