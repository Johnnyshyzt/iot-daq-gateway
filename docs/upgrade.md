# 升级

页面可以上传发行 zip，校验 ECDSA P-256 签名和 SHA256SUMS，备份数据库，然后等重启。

做不到的事：

- 正在运行的进程不能替换自己的程序文件。Windows 服务和 systemd 由 `apply-upgrade.ps1` / `apply-upgrade.sh` 在进程退出后覆盖安装目录，并且不覆盖 `data`。
- Docker 要更换镜像标签并重建容器。数据库放在卷里。页面不会改容器里的程序。
- 签名私钥不在仓库里。没有 `SHA256SUMS.sig` 的包会被拒绝。GitHub Release 在没有 `RELEASE_SIGNING_KEY` 时只提供手工安装包。
- 失败时，脚本从 `data/upgrade/previous` 恢复程序。页面上的「恢复升级前数据库」只恢复数据库。
- 磁盘空间不够时，脚本应停在删除旧文件之前。不要在快满的盘上做升级。
- 升级包不含厂商 SDK。
