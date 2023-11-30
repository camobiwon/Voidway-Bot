using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voidway_Bot;

internal record VoidwayModerationData(string OriginalReason, string ModeratorName, VoidwayModerationData.TargetNotificationStatus TargetWarnStatus)
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
