using System.Text.Json.Nodes;

namespace ServiceLib.Services.CoreConfig.Minimal;
public class CoreConfigNaiveService(Config config) : CoreConfigServiceMinimalBase(config)
{
    protected override async Task<RetResult> GeneratePassthroughConfig(ProfileItem node, int port)
    {
        var ret = new RetResult();
        try
        {
            if (node == null
                || node.Port <= 0)
            {
                ret.Msg = ResUI.CheckServerSettings;
                return ret;
            }

            if (node.ConfigType != EConfigType.NaiveProxy)
            {
                ret.Msg = ResUI.Incorrectconfiguration + $" - {node.ConfigType}";
                return ret;
            }

            var configJsonNode = new JsonObject();

            // inbound
            configJsonNode["listen"] = Global.SocksProtocol + Global.Loopback + ":" + port.ToString();

            // outbound
            var address = node.Address;
            if (!Utils.IsDomain(address))
            {
                address = node.Sni;
            }
            var proxyAddress = $"{node.Id}:{node.Security}@{address}";
            if (node.Port is not 443 and not 0)
            {
                proxyAddress += ":" + node.Port;
            }
            if (node.HeaderType == "quic")
            {
                proxyAddress = $"quic://{proxyAddress}";
            }
            else
            {
                proxyAddress = $"https://{proxyAddress}";
            }
            configJsonNode["proxy"] = proxyAddress;

            ret.Success = true;
            ret.Data = JsonUtils.Serialize(configJsonNode, true);

            return await Task.FromResult(ret);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            ret.Msg = ResUI.FailedGenDefaultConfiguration;
            return await Task.FromResult(ret);
        }
    }
}
