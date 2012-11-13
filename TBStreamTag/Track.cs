using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TBStreamTag
{
    class Track
    {
        public int ID;
        public string Artist;
        public string Title;
        public string CoverURL;
        public DateTime TimeStart;
        public DateTime TimeEnd;

        public string ToString()
        {
            return "[" + TimeStart.Hour + ":" + TimeStart.Minute + " - " + TimeEnd.Hour + ":" + TimeEnd.Minute + "]  " + Artist + " - " + Title;
        }
    }
}
