using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FatAttitude.MediaStreamer
{
    public class MediaStreamingResult
    {
        public bool Completed;
        public bool Success;
        public string ErrorText;
        public string LiveStreamingIndexPath; // legacy
        public MediaStreamingResultCodes ResultCode;
        public int StreamerID;
        public int FrameWidth;
        public int FrameHeight;
        //added for serialization into XML DB for overall persistency across clients:
        public bool LiveTVStarted;
        public long ResumePoint;
        public int AudioTrack;
        public int UseSubtitleStreamIndex;
        public string ProgramInfo;
        public string InputFile;
        public string UniekClientID;  //UnknownAndroid, iOS, Windows etc. configurable by the user in the client, normally this is the user who wants to keep track of media data like resumepoints

        public MediaStreamingResult()
        {
        }
        public MediaStreamingResult(MediaStreamingResultCodes _ResultCode, string _ErrorText)
        {
            ResultCode = _ResultCode;
            ErrorText = _ErrorText;
        }


    }

    public enum MediaStreamingResultCodes
    {
        NamedError,
        FileNotFound,
        OK
    }
}
