# main 7b726a1 的实际接口审计样本

- 测试源提交：`7b726a1b84af5fd3e2c8bb7eca83b85f9cae4a79`。
- 悉尼执行时钟：`2026-10-26T14:57:18.473244+11:00`。
- 固定桩：19 案例，16 PASS / 3 FAIL / 0 NOT_RUN；实际模型 transport 调用 20 次，均为明确标记的预置 SSE。
- live 未配置：3 PASS / 1 FAIL / 15 NOT_RUN；真实 NVIDIA 调用 0 次。四个模型无关接口案例确实执行，其余没有用桩回复代替。
- 本地数据库：PGlite 0.5.11 / pglite-socket 0.2.14; serial test execution。所有会话、数据库 schema 和记忆均隔离；没有既有用户数据。原生 PostgreSQL 17 的独立复核由仓库 Koko audit workflow 生成其自身产物。

[下载完整报告](api-reports.zip)，包含 `fixture/`、`live-unconfigured/` 下各自的 `report.html`、`summary.csv`、`turns.csv`、`run.json`，以及每个文件的 SHA256 清单。ZIP SHA256：`7c07fee7cea98a342319b512e92154da3585b5317ad7b2269cedced232287033`。解压后直接用浏览器打开 HTML，无 CDN、脚本或登录。

也可查看 [fixture 汇总](fixture/summary.csv)、[三个失败的完整证据](fixture/findings.json)、[未配置模型汇总](live-unconfigured/summary.csv)、[文件校验清单](manifest.json)。问题解释和手机交接在 [ISSUES.md](../../ISSUES.md)，运行命令在 [README](../../README.md)。

自然度由人工评阅，iOS 页面、动画和跳转未在本轮验收。HTML 已做结构、转义和安全测试；此环境没有完成浏览器截图验收。
