using System;
using System.Runtime.Serialization;

namespace EastFive.Azure.Communications;

/// <summary>
/// Capabilities available for an Azure Communication Services phone number.
/// </summary>
public enum PhoneNumberCapability
{
    /// <summary>
    /// Phone number can receive inbound voice calls.
    /// </summary>
    [EnumMember(Value = "inbound_calling")]
    InboundCalling,

    /// <summary>
    /// Phone number can make outbound voice calls.
    /// </summary>
    [EnumMember(Value = "outbound_calling")]
    OutboundCalling,

    /// <summary>
    /// Phone number can receive inbound SMS messages.
    /// </summary>
    [EnumMember(Value = "inbound_sms")]
    InboundSms,

    /// <summary>
    /// Phone number can send outbound SMS messages.
    /// </summary>
    [EnumMember(Value = "outbound_sms")]
    OutboundSms,
}

public static class PhoneNumberCapabilityExtensions
{

    /// <summary>
    /// Checks if the capabilities array contains the specified capability.
    /// </summary>
    public static bool HasCapability(this PhoneNumberCapability[]? capabilities, PhoneNumberCapability capability)
    {
        if (capabilities == null)
            return false;

        return Array.Exists(capabilities, c => c == capability);
    }
}
