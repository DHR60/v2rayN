using System.Text.Json.Nodes;

namespace ServiceLib.Services.CoreConfig.Minimal;
public class CoreConfigMieruService(Config config) : CoreConfigServiceMinimalBase(config)
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

            if (node.ConfigType != EConfigType.Mieru)
            {
                ret.Msg = ResUI.Incorrectconfiguration + $" - {node.ConfigType}";
                return ret;
            }

            var configJsonNode = new JsonObject();

            // log
            var logLevel = string.Empty;
            switch (_config.CoreBasicItem.Loglevel)
            {
                case "warning":
                    logLevel = "warn";
                    break;
                default:
                    logLevel = _config.CoreBasicItem.Loglevel;
                    break;
            }
            configJsonNode["loggingLevel"] = logLevel.ToUpper();

            // inbound
            configJsonNode["socks5Port"] = port;

            // outbound
            configJsonNode["activeProfile"] = "default";

            var profileNode = new JsonObject();
            profileNode["profileName"] = "default";
            profileNode["user"] = new JsonObject
            {
                ["name"] = node.Id,
                ["password"] = node.Security,
            };
            var serverNode = new JsonObject
            {
                ["ipAddress"] = node.Address,
            };
            if (node.Sni.IsNotEmpty())
            {
                serverNode["domainName"] = node.Sni;
            }
            var portBindingsArray = new JsonArray();
            var network = node.Network.ToUpper();
            if (node.Ports.IsNullOrEmpty())
            {
                portBindingsArray.Add(new JsonObject
                {
                    ["port"] = node.Port,
                    ["protocol"] = network,
                });
            }
            else
            {
                var ports = node.Ports.Split(',')
                                .Select(p => p.Trim())
                                .Where(p => p.IsNotEmpty())
                                .Select(p => p.Replace(':', '-'))
                                .ToList();
                foreach (var p in ports)
                {
                    if (p.Contains('-'))
                    {
                        portBindingsArray.Add(new JsonObject
                        {
                            ["portRange"] = p,
                            ["protocol"] = network,
                        });
                    }
                    else if (int.TryParse(p, out int singlePort) && singlePort > 0 && singlePort <= 65535)
                    {
                        portBindingsArray.Add(new JsonObject
                        {
                            ["port"] = singlePort,
                            ["protocol"] = network,
                        });
                    }
                }
            }
            serverNode["portBindings"] = portBindingsArray;
            profileNode["servers"] = new JsonArray
            {
                serverNode
            };

            configJsonNode["profiles"] = new JsonArray
            {
                profileNode
            };

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
