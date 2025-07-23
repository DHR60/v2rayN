namespace ServiceLib.Services;

/// <summary>
/// Centralized pre-checks before sensitive actions (set active profile, generate config, etc.).
/// </summary>
public class ActionPrecheckService(Config config)
{
    private static readonly Lazy<ActionPrecheckService> _instance = new(() => new ActionPrecheckService(AppManager.Instance.Config));
    public static ActionPrecheckService Instance => _instance.Value;

    private readonly Config _config = config;

    public async Task<List<string>> CheckBeforeSetActive(string? indexId)
    {
        if (indexId.IsNullOrEmpty())
        {
            return [ResUI.PleaseSelectServer];
        }

        var item = await AppManager.Instance.GetProfileItem(indexId);
        if (item is null)
        {
            return [ResUI.PleaseSelectServer];
        }

        return await CheckBeforeGenerateConfig(item);
    }

    public async Task<List<string>> CheckBeforeGenerateConfig(ProfileItem? item)
    {
        if (item is null)
        {
            return [ResUI.PleaseSelectServer];
        }

        var errors = new List<string>();

        errors.AddRange(await ValidateCurrentNodeAndCoreSupport(item));
        errors.AddRange(await ValidateRelatedNodesExistAndValid(item));

        return errors;
    }

    private async Task<List<string>> ValidateCurrentNodeAndCoreSupport(ProfileItem item)
    {
        if (item.ConfigType == EConfigType.Custom)
        {
            return [];
        }
        var coreType = new CoreLaunchContext(item, AppManager.Instance.Config).AdjustForConfigType().GetOutboundCoreType();
        return await ValidateNodeAndCoreSupport(item, coreType);
    }

    private async Task<List<string>> ValidateNodeAndCoreSupport(ProfileItem item, ECoreType? coreType = null)
    {
        var errors = new List<string>();
        
        // sing-box does not support xhttp / kcp
        // sing-box does not support transports like ws/http/httpupgrade/etc. when the node is not vmess/trojan/vless
        coreType ??= new CoreLaunchContext(item, AppManager.Instance.Config).AdjustForConfigType().GetOutboundCoreType();

        if (item.ConfigType is EConfigType.Custom)
        {
            errors.Add(string.Format(ResUI.CoreNotSupportProtocol, coreType.ToString(), item.ConfigType.ToString()));
            return errors;
        }

        if (item.ConfigType is EConfigType.PolicyGroup or EConfigType.ProxyChain)
        {
            ProfileGroupItemManager.Instance.TryGet(item.IndexId, out var group);
            if (group is null || group.ChildItems.IsNullOrEmpty())
            {
                errors.Add(string.Format(ResUI.GroupEmpty, item.Remarks));
                return errors;
            }
            
            foreach (var child in Utils.String2List(group.ChildItems))
            {
                if (child.IsNullOrEmpty())
                {
                    continue;
                }
                
                var childItem = await AppManager.Instance.GetProfileItem(child);
                if (childItem is null)
                {
                    errors.Add(string.Format(ResUI.NodeTagNotExist, child));
                    continue;
                }
                
                var childErrors = await ValidateNodeAndCoreSupport(childItem, coreType);
                errors.AddRange(childErrors);
            }
            return errors;
        }

        var net = item.GetNetwork() ?? item.Network;

        if (coreType == ECoreType.sing_box)
        {
            if (net is nameof(ETransport.kcp) or nameof(ETransport.xhttp))
            {
                errors.Add(string.Format(ResUI.CoreNotSupportNetwork, nameof(ECoreType.sing_box), net));
                return errors;
            }

            if (item.ConfigType is not (EConfigType.VMess or EConfigType.VLESS or EConfigType.Trojan))
            {
                if (net is nameof(ETransport.ws) or nameof(ETransport.http) or nameof(ETransport.h2) or nameof(ETransport.quic) or nameof(ETransport.httpupgrade))
                {
                    errors.Add(string.Format(ResUI.CoreNotSupportProtocolTransport, nameof(ECoreType.sing_box), item.ConfigType.ToString(), net));
                    return errors;
                }
            }
        }
        else if (coreType is ECoreType.Xray)
        {
            // Xray core does not support these protocols
            if (item.ConfigType is EConfigType.Hysteria2 or EConfigType.TUIC or EConfigType.Anytls)
            {
                errors.Add(string.Format(ResUI.CoreNotSupportProtocol, nameof(ECoreType.Xray), item.ConfigType.ToString()));
                return errors;
            }
        }

        return errors;
    }

    private async Task<List<string>> ValidateRelatedNodesExistAndValid(ProfileItem? item)
    {
        var errors = new List<string>();
        errors.AddRange(await ValidateProxyChainedNodeExistAndValid(item));
        errors.AddRange(await ValidateRoutingNodeExistAndValid(item));
        return errors;
    }

    private async Task<List<string>> ValidateProxyChainedNodeExistAndValid(ProfileItem? item)
    {
        var errors = new List<string>();
        if (item is null)
        {
            return errors;
        }

        // prev node and next node
        var subItem = await AppManager.Instance.GetSubItem(item.Subid);
        if (subItem is null)
        {
            return errors;
        }

        var prevNode = await AppManager.Instance.GetProfileItemViaRemarks(subItem.PrevProfile);
        var nextNode = await AppManager.Instance.GetProfileItemViaRemarks(subItem.NextProfile);
        var coreType = new CoreLaunchContext(item, AppManager.Instance.Config).AdjustForConfigType().GetInRouteCoreType();

        await CollectProxyChainedNodeValidation(prevNode, subItem.PrevProfile, coreType, errors);
        await CollectProxyChainedNodeValidation(nextNode, subItem.NextProfile, coreType, errors);

        return errors;
    }

    private async Task CollectProxyChainedNodeValidation(ProfileItem? node, string tag, ECoreType coreType, List<string> errors)
    {
        if (node is not null)
        {
            var nodeErrors = await ValidateNodeAndCoreSupport(node, coreType);
            errors.AddRange(nodeErrors.Select(s => ResUI.ProxyChainedPrefix + s));
        }
        else if (tag.IsNotEmpty())
        {
            errors.Add(ResUI.ProxyChainedPrefix + string.Format(ResUI.NodeTagNotExist, tag));
        }
    }

    private async Task<List<string>> ValidateRoutingNodeExistAndValid(ProfileItem? item)
    {
        var errors = new List<string>();

        if (item is null)
        {
            return errors;
        }

        var coreType = new CoreLaunchContext(item, AppManager.Instance.Config).AdjustForConfigType().GetInRouteCoreType();
        var routing = await ConfigHandler.GetDefaultRouting(_config);
        if (routing == null)
        {
            return errors;
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
                errors.Add(ResUI.RoutingRuleOutboundPrefix + string.Format(ResUI.NodeTagNotExist, outboundTag));
                continue;
            }

            var tagErrors = await ValidateNodeAndCoreSupport(tagItem, coreType);
            errors.AddRange(tagErrors.Select(s => ResUI.RoutingRuleOutboundPrefix + s));
        }

        return errors;
    }
}
