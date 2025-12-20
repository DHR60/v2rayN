# v2rayN

<!-- ---

## 关于本 Fork

本仓库基于上游项目 [2dust/v2rayN](https://github.com/2dust/v2rayN)。
上游仍在持续更新，本 fork 会不定期同步上游变更。

本 fork 主要增加：

- 批量测试节点的 IP 地址。
- 批量测试节点的 UDP 转发功能和延迟测试，支持 DNS, STUN, Minecraft Bedrock Edition 等多种协议。
- 批量测试节点的 Nat 类型，包括绑定测试，过滤测试和映射测试。
- 可分离入站分流核心和实际出站核心，由此实现了：
  - 避免启用 TUN 时的二次路由问题。
  - 避免启用 TUN 时的回环问题。
  - 界面化支持多种出站核心（NaiveProxy、Mieru 等）。
  - 可以普通节点的方式储存和导入导出 Naive, ShadowQUIC, overtls, Mieru 等节点。
  - 测试节点时可使用自定义的出站核心。
- 可使用 cdn-cgi trace 获取 IP 地址和属地（在一定程度上属于滥用，没有提交到上游）。
- 添加~~紫薇~~延迟测试除数选项。~~(太好了，我的美西延迟终于优化到 10 ms 了！！！😭😭😭)~~
- 尝试修复一些潜在的界面 Bug。
- .NET 10 编译产物以及 r2r 产物的发布支持。
- 其他一些小改动和修复。

**注意：本 fork 并非上游替代品。**

如果你不需要上述功能，建议直接使用上游版本以获得更好的稳定性和兼容性。

**在此特别感谢上游作者 2dust 以及所有贡献者的辛勤付出！**

--- -->

A GUI client for Windows, Linux and macOS, support [Xray](https://github.com/XTLS/Xray-core)
and [sing-box](https://github.com/SagerNet/sing-box)
and [others](https://github.com/2dust/v2rayN/wiki/List-of-supported-cores)

[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/2dust/v2rayN)](https://github.com/2dust/v2rayN/commits/master)
[![CodeFactor](https://www.codefactor.io/repository/github/2dust/v2rayn/badge)](https://www.codefactor.io/repository/github/2dust/v2rayn)
[![GitHub Releases](https://img.shields.io/github/downloads/2dust/v2rayN/latest/total?logo=github)](https://github.com/2dust/v2rayN/releases)
[![Chat on Telegram](https://img.shields.io/badge/Chat%20on-Telegram-brightgreen.svg)](https://t.me/v2rayn)

## How to use

Read the [Wiki](https://github.com/2dust/v2rayN/wiki) for details.

## Telegram Channel

[github_2dust](https://t.me/github_2dust)
