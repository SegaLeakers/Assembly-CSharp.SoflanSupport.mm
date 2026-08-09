using Manager;
using System;

namespace SoflanSupport
{
    public static class SoflanGameClock
    {
        public static TimeSpan CurrentTime
        {
            get { return SoflanNotesManagerHooks.GetCurrentTimeSnapshot(); }
        }
    }
}
