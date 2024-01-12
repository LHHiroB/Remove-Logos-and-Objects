using System;

namespace IOCore.Libs
{
    public class TimeUtils
    {
        public static ulong GetCurrentUnixTimestamp()
        {
            return GetUnixTimestamp(DateTime.Now);
        }

        public static ulong GetUnixTimestamp(DateTime time)
        {
            var zero = new DateTime(1970, 1, 1);
            var span = time.Subtract(zero);

            return (ulong)span.TotalSeconds;
        }

        public static DateTime UnixTimeStampToDateTime(ulong unixTimeStamp)
        {
            var dateTime = new DateTime(1970, 1, 1);
            dateTime = dateTime.AddSeconds(unixTimeStamp);
            return dateTime;
        }

        public static ulong DateTimeToUnixTimestamp(DateTime dateTime)
        {
            return (ulong)dateTime.ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }
    }
}
