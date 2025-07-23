namespace ServiceLib.Handler.Fmt;

public class MieruFmt : BaseFmt
{
    public static ProfileItem? Resolve(string str, out string msg)
    {
        msg = ResUI.ConfigurationFormatIncorrect;

        ProfileItem item = new()
        {
            ConfigType = EConfigType.Mieru
        };

        var url = Utils.TryUri(str);
        if (url == null)
        {
            return null;
        }

        item.Address = url.IdnHost;
        item.Port = url.Port;
        item.Remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);
        var rawUserInfo = Utils.UrlDecode(url.UserInfo);
        var userInfoParts = rawUserInfo.Split(new[] { ':' }, 2);
        if (userInfoParts.Length == 2)
        {
            item.Id = userInfoParts.First();
            item.Security = userInfoParts.Last();
        }

        var query = Utils.ParseQueryString(url.Query);
        ResolveUriQuery(query, ref item);
        item.Ports = Utils.UrlDecode(query["mport"] ?? "");
        item.Network = Utils.UrlDecode(query["network"] ?? "tcp");

        return item;
    }

    public static string? ToUri(ProfileItem? item)
    {
        if (item == null)
        {
            return null;
        }

        var remark = string.Empty;
        if (item.Remarks.IsNotEmpty())
        {
            remark = "#" + Utils.UrlEncode(item.Remarks);
        }
        var dicQuery = new Dictionary<string, string>();
        if (item.Network.IsNotEmpty())
        {
            dicQuery.Add("network", item.Network);
        }
        if (item.Ports.IsNotEmpty())
        {
            dicQuery.Add("mport", Utils.UrlEncode(item.Ports.Replace(':', '-')));
        }

        return ToUri(EConfigType.Mieru, item.Address, item.Port, $"{item.Id}:{item.Security}", dicQuery, remark);
    }
}
