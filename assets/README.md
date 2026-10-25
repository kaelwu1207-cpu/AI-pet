# 内置宠物资源

AI-pet 维护本目录的声明式分发资源。`skins/green`、`skins/pink` 取自 [Kenney New Platformer Pack 1.1](https://kenney.nl/assets/new-platformer-pack) 的 Characters/Default，帧图原样复制，各包保留上游 CC0 License.txt。原始归档 SHA-256：`553b907f3f0e505ab65f56f245ccaff3123c8fe3f3a0dfce9373b996bfc18cc2`。仅选用16张角色帧，不引入其他素材或品牌标志。

`motions/rest.json`、`motions/wander.json` 为项目自有 Apache-2.0 时序，按共同的 `kenney-platformer/1` 帧角色绑定外观。外观、时序和独立黑幕分别替换。清单含帧大小与摘要，资源变更须更新清单并运行组合测试；弃用前检查引用。支持已展开的本地目录包，加载不执行脚本、不自动下载依赖。
