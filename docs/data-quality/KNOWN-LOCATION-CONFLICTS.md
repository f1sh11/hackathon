# 已确认的来源位置冲突（2026-10-26）

真实云端 + iOS Apple Maps 路网回归发现，以下 Overture 记录把外州景点定位在悉尼 Circular Quay。真实路网只能验证两组坐标之间如何走，无法证明地点身份正确。

| 来源 ID | 来源名称 | 来源坐标 |
|---|---|---|
| overture:6807df81-5509-4b67-8318-8c619ef155eb | Great Barrier Reef Australia | -33.861187, 151.211419 |
| overture:ebf9a059-1f0f-420d-9a95-dea2a5da6424 | Ayers Rock, Uluru NT | -33.8627664546, 151.212478822 |

官方核验：
- Parks Australia：Uluru-Kata Tjuta 位于澳洲中部沙漠，距 Alice Springs 约450公里。https://uluru.gov.au/about/about-us/
- 澳大利亚政府：Great Barrier Reef 位于昆士兰东北沿海。https://www.waterquality.gov.au/projects-initiatives/great-barrier-reef

当前保护只隔离上述两个精确 ID 且坐标仍落在悉尼区域的记录，不按名字屏蔽其他商家，不猜测正确坐标，不修改云端、不删除已有行程。自动规划和步行备选均排除它们；必去项命中时明确报错并保留需求。生成 trace 记录排除数量。探索数据暂保持来源原样，因此仍需队友修正源记录；此保护不是全量数据清洗。

上游修正并核验身份和坐标后，可删除隔离条目及对应回归。当前条件也允许坐标修正到悉尼以外后通过本项保护，仍受常规目的地/距离约束限制。
