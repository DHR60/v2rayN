namespace ServiceLib.Models;

/// <summary>
/// Core launch context that encapsulates all parameters required for launching the core.
/// 
/// Core Architecture:
/// - When SplitCore == true: user network -> InRouteCoreType (inbound and routing) -> OutboundCoreType (outbound)
/// - When SplitCore == false: user network -> CoreType (inbound, routing and outbound)
/// 
/// Usage Guidelines:
/// - Prefer using Get...() methods over direct property access
/// - Properties are intended for internal use or special requirements only
/// - Use GetOutboundCoreType() and GetInRouteCoreType() for core type retrieval
/// 
/// Property Details:
/// - CoreType: Set by user in profile, falls back to default if not specified
/// - PureEndpointCore: Set by user in config, falls back to default if not specified
/// - SplitRouteCore: Set by user in config, never overwritten
/// - SplitCore: Set by user in config, may be overwritten when SplitRouteCore and PureEndpointCore are equal,
///   TUN mode is enabled, or config type is custom
/// - SplitCore: Indicates whether dual-core mode is used
/// - OutboundCorePassThroughOnly: If true, outbound core only performs pass-through operations,
///   only effective when config type is custom
/// </summary>
public class CoreLaunchContext
{
    public ProfileItem Node { get; set; }
    public bool SplitCore { get; set; }
    public ECoreType CoreType { get; set; }
    public ECoreType PureEndpointCore { get; set; }
    public ECoreType SplitRouteCore { get; set; }
    public bool EnableTun { get; set; }
    public int PreSocksPort { get; set; }
    public EConfigType ConfigType { get; set; }

    public ECoreType? OutboundCoreType { get; set; }
    public ECoreType? InRouteCoreType { get; set; }
    public bool OutboundCorePassThroughOnly { get; set; }

    public ECoreType GetOutboundCoreType()
    {
        return OutboundCoreType ?? CoreType;
    }

    public ECoreType GetInRouteCoreType()
    {
        return InRouteCoreType ?? CoreType;
    }

    public CoreLaunchContext(ProfileItem node, Config config)
    {
        Node = node;
        SplitCore = config.SplitCoreItem.EnableSplitCore;
        CoreType = AppManager.Instance.GetCoreType(node, node.ConfigType);
        PureEndpointCore = AppManager.Instance.GetSplitCoreType(node, node.ConfigType);
        SplitRouteCore = config.SplitCoreItem.RouteCoreType;
        EnableTun = config.TunModeItem.EnableTun;
        PreSocksPort = 0;
        ConfigType = node.ConfigType;
        OutboundCoreType = null;
        InRouteCoreType = null;
        OutboundCorePassThroughOnly = true;
    }

    /// <summary>
    /// Adjust context parameters based on configuration type
    /// </summary>
    public CoreLaunchContext AdjustForConfigType()
    {
        (OutboundCoreType, InRouteCoreType) = AppManager.Instance.GetCoreAndPreType(Node);
        if (InRouteCoreType == null)
        {
            InRouteCoreType = OutboundCoreType;
        }
        else
        {
            SplitCore = true;
        }
        OutboundCorePassThroughOnly = SplitCore;
        if (Node.ConfigType == EConfigType.Custom)
        {
            OutboundCorePassThroughOnly = false;
            if (Node.PreSocksPort > 0)
            {
                PreSocksPort = Node.PreSocksPort.Value;
            }
            else
            {
                EnableTun = false;
                SplitCore = false;
            }
        }
        else if (SplitCore)
        {
            PreSocksPort = AppManager.Instance.GetLocalPort(EInboundProtocol.split);
        }
        return this;
    }
}
