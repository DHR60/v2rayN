using System.Text.Json.Nodes;

namespace ServiceLib.Services.CoreConfig.Minimal;
public class CoreConfigOvertlsService(Config config) : CoreConfigServiceMinimalBase(config)
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

            if (node.ConfigType != EConfigType.Overtls)
            {
                ret.Msg = ResUI.Incorrectconfiguration + $" - {node.ConfigType}";
                return ret;
            }

            var configJsonNode = new JsonObject();
            var clientSettingsNode = new JsonObject();

            // inbound
            clientSettingsNode["listen_host"] = Global.Loopback;
            clientSettingsNode["listen_port"] = port;

            // outbound
            clientSettingsNode["server_host"] = node.Address;
            clientSettingsNode["server_port"] = node.Port;
            clientSettingsNode["server_domain"] = node.StreamSecurity == Global.StreamSecurity ? node.Sni : node.Address;

            var tunnelPath = node.Id.Split(',')
                                .Select(p => p.Trim())
                                .Where(p => p.IsNotEmpty())
                                .ToList();
            if (tunnelPath.Count == 1)
            {
                configJsonNode["tunnel_path"] = tunnelPath[0];
            }
            else
            {
                var tunnelPathArray = new JsonArray();
                foreach (var p in tunnelPath)
                {
                    tunnelPathArray.Add(p);
                }
                configJsonNode["tunnel_path"] = tunnelPathArray;
            }
            configJsonNode["client_settings"] = clientSettingsNode;

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
