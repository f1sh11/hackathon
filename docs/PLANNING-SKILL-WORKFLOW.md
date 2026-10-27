# 快速修改旅遊規劃 Skill

只需編輯 `agent-skills/itinerary.zh-Hant.md`、`agent-skills/itinerary.en.md`，再更新 `agent-skills/version.json`。

原有全文必須保留，新規則追加在底部；如果新偏好取代舊偏好，請明確寫出優先順序。原有安全與硬限制仍須遵守。工具會拒絕刪改基底規則，或移除先前已啟用的追加規則。

## 1. 編輯與驗證

在專案根目錄執行：

```sh
python3 scripts/planning_skills.py build
```

檢查成功後產生 `agent-skills/bundle.json`。初始文件尚未追加內容時會拒絕產生更新，避免空更新。每次都要使用新版本號，例如 `2026-10-27.6`。

## 2. 快速套用 AWS

在已有 Kubernetes 操作權限的電腦，可直接：

```sh
python3 scripts/planning_skills.py push
```

若使用 AWS CloudShell：上傳 `scripts/planning_skills.py` 和 `agent-skills/bundle.json`，然後：

```sh
python3 ~/planning_skills.py push --bundle ~/bundle.json
```

CloudShell 必須使用既有叢集的 operator 帳號及正確 context。平常部署帳號為唯讀，需在授權的短期更新工作階段開啟權限，完成後撤銷。工具不建立 Access Key、不讀 Secret。新增的獨立管理 API 可讓 Postman 上傳與啟用，需額外管理憑證；團隊 App 連線碼本身只能讀取與測試，不能修改。詳見 SKILL-ADMIN-POSTMAN.md。

更新保存在既有 AWS App 持久磁碟，先備份舊版，再原子切換 `active.json`。不需要重建映像或重啟後端。下一個 HTTP 請求使用新版；進行中的規劃固定使用開始時的 Skill，避免內容與版本號不一致。新建／修改 Skill 不會自動重排已保存行程。

## 3. 查看與測試

使用既有 App Bearer 連線碼：

- `GET /sydneybuddy/planning/agent/status`：查看 `skillVersion`、`skillSource`、`baseSkillVersion`。
- `GET /sydneybuddy/planning/skills?language=zh-Hant`：有效全文、版本與 SHA256。
- `GET /sydneybuddy/planning/skills?language=en&download=true`：下載英文 Markdown。
- `POST /sydneybuddy/planning/test`：用同一份需求 JSON 比較更新前後的草稿；回傳含 `skillVersion`。

只有 `meterless`／`meterless-cloud` Skill Agent 會使用這些 Skill，並保留原有 JSON 輸出契約與後端驗證權限。

## 4. 回復

先檢視回復會移除哪些新規則，再明確執行：

```sh
python3 scripts/planning_skills.py rollback --confirm-rollback
```

回復上一個有效 override；若上一版沒有 override，恢復映像內建 Skill。回復後請把本機工作文件對齊該版，再準備後續更新。原始基底版本為 `2026-10-27.5`；未來映像變更基底版本時，需要重新匯出並審閱更新，避免舊 bundle 套到不相容的基底。無效 bundle 會明確報錯，不會悄悄宣稱更新成功。

Skill 修改影響模型的選擇與解釋，不會自行改寫後端步數算法、營業驗證、預算與交通硬限制。請用測試接口檢查結果及待確認事項，不將規則文字視為已驗證行程。

## Postman 管理流程

新版提供候選版上傳、預覽、啟用與明確回復接口。一次性設定管理憑證後，可全程用 Postman 上傳 JSON 或完整中英文 Markdown；見 [Postman 管理說明](SKILL-ADMIN-POSTMAN.md)。既有追加規則保留與移除前討論要求不變。
