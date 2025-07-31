using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using ServiceLib.Models;

namespace ServiceLib.Services;

/// <summary>
/// Centralized pre-checks before sensitive actions (set active profile, generate config, etc.).
/// Return (ok, msg) for VMs to decide.
/// </summary>
public class ActionPrecheckService
{
    private static readonly Lazy<ActionPrecheckService> _instance = new(() => new ActionPrecheckService(AppManager.Instance.Config));
    public static ActionPrecheckService Instance => _instance.Value;

    private readonly Config _config;

    public ActionPrecheckService(Config config)
    {
        _config = config;
    }

    private static List<string> OneMsg(string msg) => new() { msg };

    public async Task<(bool ok, List<string> msgs)> CheckBeforeSetActive(string? indexId)
    {
        if (indexId.IsNullOrEmpty())
        {
            return (false, OneMsg(ResUI.PleaseSelectServer));
        }

        var item = await AppManager.Instance.GetProfileItem(indexId);
        if (item is null)
        {
            return (false, OneMsg(ResUI.PleaseSelectServer));
        }

        return await CheckBeforeGenerateConfig(item);
    }

    public async Task<(bool ok, List<string> msgs)> CheckBeforeGenerateConfig(ProfileItem? item)
    {
        if (item is null)
        {
            return (false, OneMsg(ResUI.PleaseSelectServer));
        }

        var msgs = new List<string>();

        var currentNodeMsgs = ValidateCurrentNodeAndCoreSupport(item).ToList();
        if (currentNodeMsgs.Count > 0)
        {
            msgs.AddRange(currentNodeMsgs);
            return (false, msgs);
        }

        msgs.AddRange(await ValidateRelatedNodesExistAndValid(item));

        return (true, msgs);
    }

    private IEnumerable<string> ValidateCurrentNodeAndCoreSupport(ProfileItem item)
    {
        var context = new CoreLaunchContext(item, _config);
        var coreType = context.SplitCore ? context.SplitRouteCore : context.CoreType;
        return ValidateNodeAndCoreSupport(item, coreType);
    }

    /// <summary>
    /// <summary>
    /// Validate whether the node and chosen core combination is supported. Returns a collection of messages to show the user.
    /// An empty collection means there are no blocking errors.
    /// </summary>
    ///
    private IEnumerable<string> ValidateNodeAndCoreSupport(ProfileItem item, ECoreType? coreType = null)
    {
        // sing-box does not support xhttp / kcp
        // sing-box does not support transports like ws/http/httpupgrade/etc. when the node is not vmess/trojan/vless
        if (coreType == null)
        {
            var context = new CoreLaunchContext(item, _config);
            coreType = context.SplitCore ? context.SplitRouteCore : context.CoreType;
        }
        var net = item.GetNetwork() ?? item.Network;

        if (coreType == ECoreType.sing_box)
        {
            if (net is nameof(ETransport.kcp) or nameof(ETransport.xhttp))
            {
                yield return string.Format(ResUI.CoreNotSupportNetwork, nameof(ECoreType.sing_box), net);
                yield break;
            }

            if (item.ConfigType is not (EConfigType.VMess or EConfigType.VLESS or EConfigType.Trojan))
            {
                if (net is nameof(ETransport.ws) or nameof(ETransport.http) or nameof(ETransport.h2) or nameof(ETransport.quic) or nameof(ETransport.httpupgrade))
                {
                    yield return string.Format(ResUI.CoreNotSupportProtocolTransport, nameof(ECoreType.sing_box), item.ConfigType.ToString(), net);
                    yield break;
                }
            }
        }
        else if (coreType is ECoreType.Xray)
        {
            // Xray core does not support these protocols
            if (item.ConfigType is EConfigType.Hysteria2 or EConfigType.TUIC or EConfigType.Anytls)
            {
                yield return string.Format(ResUI.CoreNotSupportProtocol, nameof(ECoreType.Xray), item.ConfigType.ToString());
                yield break;
            }
        }

        yield break; // explicit for clarity; no blocking errors
    }

    /// <summary>
    /// Validate that nodes related to the current node (chained/routing) exist and are valid.
    /// </summary>
    private async Task<IEnumerable<string>> ValidateRelatedNodesExistAndValid(ProfileItem? item)
    {
        var msgs = new List<string>();
        msgs.AddRange(await ValidateProxyChainedNodeExistAndValid(item));
        msgs.AddRange(await ValidateRoutingNodeExistAndValid(item));
        return msgs;
    }

    private async Task<IEnumerable<string>> ValidateProxyChainedNodeExistAndValid(ProfileItem? item)
    {
        var msgs = new List<string>();
        if (item is null)
        {
            return msgs;
        }

        // prev node and next node
        var subItem = await AppManager.Instance.GetSubItem(item.Subid);
        if (subItem is null)
        {
            return msgs;
        }

        var prevNode = await AppManager.Instance.GetProfileItemViaRemarks(subItem.PrevProfile);
        var nextNode = await AppManager.Instance.GetProfileItemViaRemarks(subItem.NextProfile);
        var context = new CoreLaunchContext(item, _config);
        var coreType = context.SplitCore ? context.SplitRouteCore : context.CoreType;

        CollectProxyChainedNodeValidation(prevNode, subItem.PrevProfile, coreType, msgs);
        CollectProxyChainedNodeValidation(nextNode, subItem.NextProfile, coreType, msgs);

        return msgs;
    }

    private void CollectProxyChainedNodeValidation(ProfileItem? node, string tag, ECoreType coreType, List<string> msgs)
    {
        if (node is not null)
        {
            msgs.AddRange(ValidateNodeAndCoreSupport(node, coreType));
            if (node.ConfigType is EConfigType.Custom)
            {
                msgs.Add(string.Format(ResUI.ProxyChainedNodeTagNotSupportConfigType, node.Remarks, node.ConfigType.ToString()));
            }
        }
        else if (tag.IsNotEmpty())
        {
            msgs.Add(string.Format(ResUI.ProxyChainedNodeTagNotExist, tag));
        }
    }

    private async Task<IEnumerable<string>> ValidateRoutingNodeExistAndValid(ProfileItem? item)
    {
        var msgs = new List<string>();

        if (item is null)
        {
            return msgs;
        }

        var context = new CoreLaunchContext(item, _config);
        var coreType = context.SplitCore ? context.SplitRouteCore : context.CoreType;
        var routing = await ConfigHandler.GetDefaultRouting(_config);
        if (routing == null)
        {
            return msgs;
        }

        var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet);
        foreach (var ruleItem in rules ?? [])
        {
            if (!ruleItem.Enabled)
            {
                continue;
            }

            var outboundTag = ruleItem.OutboundTag;
            if (outboundTag.IsNullOrEmpty() || Global.OutboundTags.Contains(outboundTag))
            {
                continue;
            }

            var tagItem = await AppManager.Instance.GetProfileItemViaRemarks(outboundTag);
            if (tagItem is null)
            {
                msgs.Add(string.Format(ResUI.RoutingRuleOutboundTagNotExist, outboundTag));
                continue;
            }

            msgs.AddRange(ValidateNodeAndCoreSupport(tagItem, coreType));
            if (tagItem.ConfigType is EConfigType.Custom)
            {
                msgs.Add(string.Format(ResUI.RoutingRuleOutboundTagNotSupportConfigType, outboundTag, tagItem.ConfigType.ToString()));
            }
        }

        return msgs;
    }
}
