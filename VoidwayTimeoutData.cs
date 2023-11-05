using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voidway_Bot;

internal record VoidwayTimeoutData(string OriginalReason, string ModeratorName, VoidwayTimeoutData.TargetNotificationStatus TargetWarnStatus)
{
    public enum TargetNotificationStatus
    {
        UNKNOWN,
        NOT_ATTEMPTED,
        SUCCESS,
        FAILURE,
        NOT_APPLICABLE,
    }
}
