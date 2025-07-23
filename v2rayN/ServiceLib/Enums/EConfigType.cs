namespace ServiceLib.Enums;

public enum EConfigType
{
    VMess = 1,
    Custom = 2,
    Shadowsocks = 3,
    SOCKS = 4,
    VLESS = 5,
    Trojan = 6,
    Hysteria2 = 7,
    TUIC = 8,
    WireGuard = 9,
    HTTP = 10,
    Anytls = 11,
    PolicyGroup = 101,
    ProxyChain = 102,
    
    NaiveProxy = 201,
    Juicity = 202,
    Brook = 203,
    Shadowquic = 204,
}
