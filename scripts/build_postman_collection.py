#!/usr/bin/env python3
"""Build a secret-free Postman collection and verify coverage of mapped API routes."""
import json, re
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'examples/postman'
BASE = 'https://api.a6c43f1d-2765-487d-89af-99b612b39de9.cloud.zetaris.com/sydneybuddy'
folders = {}
covered = set()
def add(group, name, method, path, body=None, description='', requires=(), capture='', local=False, form=None):
    covered.add((method, re.sub(r'\{\{(\w+)\}\}', lambda m: {'trip_id':'{id}','memory_id':'{id}','review_id':'{id}','session_id':'{id}','run_id':'{id}','simulation_id':'{runId}','social_id':'{id}'}.get(m[1], '{'+m[1]+'}'), path.split('?')[0])))
    req = {'method':method, 'header':[{'key':'X-Travel-Data-Source','value':'{{data_source}}'},{'key':'X-App-Language','value':'{{language}}'}], 'url': ('{{local_base_url}}' if local else '{{base_url}}')+path, 'description':description}
    if body is not None:
        raw = body if isinstance(body,str) else json.dumps(body,ensure_ascii=False,indent=2)
        req['body']={'mode':'raw','raw':raw,'options':{'raw':{'language':'json'}}}
        req['header'].append({'key':'Content-Type','value':'application/json'})
    if form: req['body']=form
    if local: req['auth']={'type':'noauth'}
    item={'name':name,'request':req,'response':[]}
    events=[]
    checks=list(requires)
    if method in ('DELETE','PUT','PATCH') or name.startswith('⚠'):
        checks.append('allow_writes')
    pre=[]
    for key in checks:
        if key=='allow_writes': pre.append("if (pm.variables.get('allow_writes') !== 'yes') { pm.execution.skipRequest(); throw new Error('此請求會修改資料；確認後將 allow_writes 設為 yes，再單獨傳送。'); }")
        else: pre.append(f"if (!pm.variables.get('{key}')) {{ pm.execution.skipRequest(); throw new Error('請先完成前置請求，或填入 {key}'); }}")
    if pre: events.append({'listen':'prerequest','script':{'type':'text/javascript','exec':pre}})
    post=["pm.test('HTTP 2xx', function () { pm.expect(pm.response.code).to.be.within(200,299); });"]
    if capture: post.append('if (pm.response.code >= 200 && pm.response.code < 300) { const d = pm.response.json(); '+capture+' }')
    events.append({'listen':'test','script':{'type':'text/javascript','exec':post}})
    item['event']=events
    folders.setdefault(group,[]).append(item)

profile=json.loads((ROOT/'examples/planning/request.json').read_text())
profile.update(date='{{travel_date}}',mode='assisted',language='{{language}}',transportPreference='{{transport}}')
raw=json.dumps(profile,ensure_ascii=False,indent=2).replace('"days": 1','"days": {{days}}').replace('"budget": 180','"budget": {{budget}}').replace('"maxSteps": 10000','"maxSteps": {{max_steps}}')
point={'name':'Circular Quay','lat':-33.8611,'lng':151.2126}
opera={'id':'opera','name':'Sydney Opera House','lat':-33.8568,'lng':151.2153}
capdraft="const t=d.draft || d; pm.collectionVariables.set('draft_json',JSON.stringify(t)); if(d.runId) pm.collectionVariables.set('run_id',d.runId);"
add('01 連線與資料狀態','健康檢查','GET','/health')
add('01 連線與資料狀態','Agent 與 Skill 狀態','GET','/planning/agent/status')
add('01 連線與資料狀態','資料來源狀態','GET','/data/status')
add('01 連線與資料狀態','地圖資料圖層','GET','/data/map-layers')
add('01 連線與資料狀態','規劃景點目錄','GET','/catalog')
add('02 行程 JSON 測試（先從這裡）','下載範例 JSON','GET','/planning/test/example',description='Send and Download 可另存 JSON。')
add('02 行程 JSON 測試（先從這裡）','不同參數產生行程（JSON）','POST','/planning/test',raw,'可調 days、budget、max_steps、transport 與 Body 偏好。會使用雲端模型額度；不儲存行程，但保留診斷記錄。',capture=capdraft)
add('02 行程 JSON 測試（先從這裡）','上傳 JSON 檔並下載結果','POST','/planning/test?download=true',description='Body → form-data → file 選擇 JSON；上限 200000 bytes。Send and Download 下載結果。不儲存行程。',form={'mode':'formdata','formdata':[{'key':'file','type':'file','src':[]} ]},capture=capdraft)
add('02 行程 JSON 測試（先從這裡）','JSON 參數並下載結果','POST','/planning/test?download=true',raw,'Send and Download 另存結果；會使用模型額度，不儲存行程。',capture=capdraft)
add('02 行程 JSON 測試（先從這裡）','讀取規劃診斷','GET','/planning/runs/{{run_id}}',requires=['run_id'])
add('02 行程 JSON 測試（先從這裡）','有效 Skill 中文版','GET','/planning/skills?language=zh-Hant')
add('02 行程 JSON 測試（先從這裡）','有效 Skill 英文下載','GET','/planning/skills?language=en&download=true',description='唯讀；更新 Skill 請使用操作員流程，App 連線碼沒有寫入 Skill 的權限。')
add('03 景點、住宿與天氣','附近景點搜尋','POST','/poi/search',{'lat':-33.8611,'lng':151.2126,'radius':3000,'category':'all','query':'','provider':'osm','language':'{{language}}'},'category 可用 all / sights / gems / food / lodging。來源不同可能回傳不同數量。')
add('03 景點、住宿與天氣','景點照片與介紹','POST','/poi/media',opera)
add('03 景點、住宿與天氣','景點與住宿候選清單','POST','/planning/inventory',{'start':point},'住宿為候選；不代表空房、已訂房或價格已核實。')
add('03 景點、住宿與天氣','逐日天氣','POST','/weather',raw)
add('03 景點、住宿與天氣','交通、人潮與費用資訊','POST','/data/insights',{'date':'{{travel_date}}','places':[opera]})
add('04 行程規劃與調整','App 行程規劃','POST','/plan',raw,'與測試 API 共用規劃邏輯；不自動儲存。可能呼叫模型。',capture=capdraft)
add('04 行程規劃與調整','步行候選預處理','POST','/plan/prepare',raw,'初篩候選，不代表已驗證所有道路時間。可能呼叫模型。')
add('04 行程規劃與調整','時間軸試算','POST','/schedule/editor','{"trip": {{draft_json}}, "settings": {"pace":"balanced","timeLimit":600}}','先產生草案。未提供 days 時由編輯器安排活動；此請求不儲存，但非步行偏好可能查詢正式交通。',requires=['draft_json'],capture=capdraft)
add('04 行程規劃與調整','剩餘行程重新安排','POST','/plan/remaining','{"tripId":"{{trip_id}}","expectedRevision":{{trip_revision}},"day":0,"currentMinute":840,"origin":'+json.dumps(point)+',"completedIds":[],"skippedIds":[],"walkingRoutes":{{walking_routes_json}},"request":"下午有點累，減少步行，保留晚餐及回飯店時間。"}', '需要已儲存行程、最新版本與真實 walkingRoutes 證據；不是 navigation 回傳原文直接貼上。請按 App walkingRoutes 格式提供。',requires=['trip_id','trip_revision','walking_routes_json'],capture=capdraft)
add('05 步行與公共運輸','步行導航','POST','/navigation/route',{'locations':[{'lat':-33.8611,'lon':151.2126},{'lat':-33.8568,'lon':151.2153}]},'注意本接口使用 lon；需後端配置 Valhalla。')
add('05 步行與公共運輸','公共運輸路線','POST','/journey/plan',{'routingProvider':'nsw','points':[point,opera],'modes':['transit'],'departureTime':'{{travel_date}}T10:00:00+11:00'},'需 TfNSW 後端設定；日期請選可查詢範圍，時間需含時區，夏令時間注意 +11:00。')
add('06 已儲存行程（寫入需確認）','列出已儲存行程','GET','/trips','' if False else None)
add('06 已儲存行程（寫入需確認）','⚠ 儲存草案為行程','POST','/trips','{"trip": {{draft_json}}, "expectedRevision":0}', '寫入團隊共用資料。新草案 expectedRevision=0；若草案已有 id，請用其目前 revision。',requires=['draft_json'],capture="if(d.id){pm.collectionVariables.set('trip_id',d.id);pm.collectionVariables.set('trip_revision',String(d.revision));}")
add('06 已儲存行程（寫入需確認）','⚠ 修改行程名稱','POST','/trips','{"trip":{"id":"{{trip_id}}","title":"Postman 測試行程"},"renameOnly":true,"expectedRevision":{{trip_revision}}}',requires=['trip_id','trip_revision'],capture="if(d.revision) pm.collectionVariables.set('trip_revision',String(d.revision));")
add('06 已儲存行程（寫入需確認）','⚠ 刪除指定行程','DELETE','/trips/{{trip_id}}',description='只刪除你指定的測試行程；不可復原，不要使用夥伴行程 ID。',requires=['trip_id'])
add('07 可行性審查','建立草案審查','POST','/agents/feasibility/reviews','{"trip":{{draft_json}},"planId":"postman-draft","planRevision":1,"requirementsRevision":1}', '建立審查記錄，可能呼叫模型與交通服務；不等於確認儲存。',requires=['draft_json'],capture="pm.collectionVariables.set('review_id',d.reviewId);pm.collectionVariables.set('plan_revision',String(d.planRevision));pm.collectionVariables.set('requirements_revision',String(d.requirementsRevision));")
add('07 可行性審查','取得審查結果','GET','/agents/feasibility/reviews/{{review_id}}',requires=['review_id'])
add('07 可行性審查','⚠ 確認已通過審查並儲存','POST','/agents/feasibility/reviews/{{review_id}}/confirm','{"planRevision":{{plan_revision}},"requirementsRevision":{{requirements_revision}},"expectedSavedRevision":0}', '只有可確認且未過期的審查可用；既有行程需填目前 saved revision。',requires=['review_id','plan_revision','requirements_revision'])
add('08 World Model 與模擬','行程最新世界狀態','GET','/trips/{{trip_id}}/world',requires=['trip_id'])
add('08 World Model 與模擬','行程最新規劃狀態','GET','/trips/{{trip_id}}/planning-state',requires=['trip_id'])
add('08 World Model 與模擬','⚠ 建立模擬記錄','POST','/trips/{{trip_id}}/simulation-runs',{'requestId':'{{$guid}}'},'寫入模擬狀態；重試同一操作時保留相同 requestId。',requires=['trip_id'],capture="if(d.id || d.runId || d.run?.id) pm.collectionVariables.set('simulation_id',d.id || d.runId || d.run.id);")
add('08 World Model 與模擬','取得模擬結果','GET','/simulation-runs/{{simulation_id}}',requires=['simulation_id'])
add('08 World Model 與模擬','⚠ 記錄模擬建議決定','POST','/simulation-runs/{{simulation_id}}/decisions',{'suggestionId':'{{suggestion_id}}','decision':'accepted'},'decision 為 accepted 或 rejected。先從模擬結果選擇 suggestionId。',requires=['simulation_id','suggestion_id'])
add('09 餐廳證據與估價','解析貼上的 JSON-LD 菜單','POST','/restaurants/parse-menu',{'channel':'dine_in','content':json.dumps({'@context':'https://schema.org','@type':'MenuItem','name':'Demo pasta (sample only)','offers':{'@type':'Offer','price':28,'priceCurrency':'AUD'}})},'純解析範例，不抓取官網、不儲存；示例菜價不是店家真實價格。')
add('09 餐廳證據與估價','餐廳已發布資料','GET','/restaurants/published')
add('09 餐廳證據與估價','⚠ 匯入餐廳資料','POST','/restaurants/import',{'placeId':'{{place_id}}','placeName':'{{place_name}}','lat':'{{place_lat}}','lng':'{{place_lng}}','sourceType':'user_import','sourceUrl':'{{source_url}}','sourceName':'User supplied content','author':'{{source_author}}','observedAt':'{{observed_at}}','license':'{{source_permission}}','sourceText':'{{source_text}}','reviews':[{'quote':'{{review_quote}}','author':'{{source_author}}'}]},'先以 poi/search 選實際餐廳；填真實來源、授權與逐字引句。user_import 不能變成官方菜單價格。',requires=['place_id','place_name','place_lat','place_lng','source_url','source_author','observed_at','source_permission','source_text','review_quote'],capture="if(d.id) pm.collectionVariables.set('restaurant_id',d.id);")
# Coordinates must be numeric JSON values, even when supplied as collection variables.
folders['09 餐廳證據與估價'][-1]['request']['body']['raw']=folders['09 餐廳證據與估價'][-1]['request']['body']['raw'].replace('"{{place_lat}}"','{{place_lat}}').replace('"{{place_lng}}"','{{place_lng}}')
add('09 餐廳證據與估價','⚠ 餐點每人預算估算並保存','POST','/restaurants/estimate',{'placeId':'{{restaurant_id}}','people':2,'channel':'dine_in','currency':'AUD','items':[{'id':'{{menu_item_id}}','quantity':2}]},'需先匯入有授權的官方或授權菜單；menu_item_id 取 restaurantData.menuItems[].id。不是地圖原始 placeId。',requires=['restaurant_id','menu_item_id'])
add('10 活動與參考票價','指定景點票價搜尋','POST','/activities/search',{'placeId':'opera','placeName':'Sydney Opera House','lat':-33.8568,'lng':151.2153,'radius':1000,'query':''},'只回傳已比對此景點的活動；沒有資料會是空陣列，不代表免費。')
add('10 活動與參考票價','批次最低可比較票價摘要','POST','/activities/summaries',{'places':[opera]})
social={'platform':'instagram','postUrl':'{{social_post_url}}','text':'{{social_text}}','comments':[]}
add('11 社群內容匯入','AI 萃取已取得貼文','POST','/social/extract',social,'貼上自己已取得的內容；不會自動抓 Instagram。會呼叫模型。',requires=['social_post_url','social_text'],capture="if(d.mentions) pm.collectionVariables.set('mentions_json',JSON.stringify(d.mentions));")
add('11 社群內容匯入','⚠ 匯入貼文及證據草稿','POST','/social/import','{"platform":"instagram","postUrl":"{{social_post_url}}","author":"{{source_author}}","text":"{{social_text}}","comments":[],"photos":[],"mentions":{{mentions_json}}}', '先萃取或自行提供可對照引句的 mentions。含換行或引號的貼文，請在 Body 正確 JSON 跳脫。',requires=['social_post_url','social_text','source_author','mentions_json'],capture="if(d.id) pm.collectionVariables.set('social_id',d.id);")
add('11 社群內容匯入','列出匯入記錄','GET','/social/imports')
add('11 社群內容匯入','列出已發布社群景點','GET','/social/published')
add('11 社群內容匯入','⚠ 配對地點並發布','POST','/social/{{social_id}}/publish','{"mentionIndex":0,"placeId":"{{place_id}}","placeName":"{{place_name}}","lat":{{place_lat}},"lng":{{place_lng}}}', '需先從 poi/search 選擇實際地點；公開至此 App 的共用候選資料。',requires=['social_id','place_id','place_name','place_lat','place_lng'])
add('12 偏好記憶（寫入需同意）','列出已保存偏好','GET','/memories')
mem={'consent':True,'summary':'Postman test: prefer museums and public transport','profile':{'interests':['文化'],'maxSteps':10000}}
add('12 偏好記憶（寫入需同意）','⚠ 保存測試偏好','POST','/memories',mem,'實際使用者同意才能保存；此請求寫入長期記憶。',capture="if(d.id) pm.collectionVariables.set('memory_id',d.id);")
add('12 偏好記憶（寫入需同意）','⚠ 更新指定偏好','PUT','/memories/{{memory_id}}',mem,requires=['memory_id'],capture="if(d.id) pm.collectionVariables.set('memory_id',d.id);")
add('12 偏好記憶（寫入需同意）','⚠ 刪除指定偏好','DELETE','/memories/{{memory_id}}',requires=['memory_id'])
add('13 Koko 對話','⚠ 建立對話','POST','/pet/sessions',{'language':'{{language}}','fields':{}},'建立會話；回傳 id 可供後續對話使用。',capture="if(d.id) pm.collectionVariables.set('session_id',d.id);")
add('13 Koko 對話','讀取對話','GET','/pet/sessions/{{session_id}}',requires=['session_id'])
add('13 Koko 對話','⚠ 更新對話旅行欄位','PATCH','/pet/sessions/{{session_id}}',{'language':'{{language}}','fields':{'destination':'悉尼','date':'{{travel_date}}','days':'1'}},requires=['session_id'])
add('13 Koko 對話','⚠ 傳送對話訊息（NDJSON）','POST','/pet/sessions/{{session_id}}/turn',{'language':'{{language}}','requestId':'{{$guid}}','message':'I like museums and would prefer public transport.'},'回應為逐行 JSON，查看 Response → Raw。會使用模型並保存對話；同一輪重試需沿用 requestId。',requires=['session_id'])
add('13 Koko 對話','⚠ 決定候選偏好只用於本次旅行','POST','/pet/sessions/{{session_id}}/memory',{'candidateId':'{{candidate_id}}','consent':False,'expectedConflicts':[]},'candidate_id 取對話待確認偏好；consent=false 為本次旅行，true 才是長期保存。',requires=['session_id','candidate_id'])
add('13 Koko 對話','⚠ 完成 Travel DNA','POST','/pet/sessions/{{session_id}}/profile/complete',{},'需要至少一項已確認偏好，且沒有待確認資料。',requires=['session_id'])
add('13 Koko 對話','⚠ 刪除對話','DELETE','/pet/sessions/{{session_id}}',requires=['session_id'])
add('14 僅本機設定（AWS 禁止）','本機設定頁','GET','/setup',description='僅本機可用；AWS gateway 回 404。不要把頁面中的 token 或任何服務密鑰存進共用 Postman。',local=True)
add('14 僅本機設定（AWS 禁止）','⚠ 提交本機設定表單','POST','/setup',description='只建立路由範例；使用瀏覽器本機設定頁較合適。CSRF token 取 GET /setup，服務金鑰由本人在本機頁面設定；不要匯出到 Collection。',local=True,requires=['setup_csrf_token'],form={'mode':'urlencoded','urlencoded':[{'key':'token','value':'{{setup_csrf_token}}','type':'text'}]})

admin_group = '15 Skill 管理（獨立管理憑證）'
status_capture = "if(d.activeFingerprint) pm.collectionVariables.set('active_fingerprint',d.activeFingerprint);"
upload_capture = status_capture + " if(d.version) pm.collectionVariables.set('skill_version',d.version);"
add(admin_group,'01 管理狀態與目前版本指紋','GET','/admin/planning/skills/status',capture=status_capture)
add(admin_group,'02 已保存版本清單','GET','/admin/planning/skills/versions')
add(admin_group,'03 下載累積規則 JSON 範本','GET','/admin/planning/skills/template',description='保留既有 appendices，再追加規則並使用新 version；下載不會啟用。')
add(admin_group,'04 上傳 JSON 檔（不啟用）','POST','/admin/planning/skills/versions',description='Body 的 file 選擇 skill-bundle.json；純上傳驗證，不會立即啟用。',form={'mode':'formdata','formdata':[{'key':'file','type':'file','src':[]}]},capture=upload_capture)
add(admin_group,'04b 貼上 JSON（不啟用）','POST','/admin/planning/skills/versions',{'version':'{{skill_version}}','baseVersion':'2026-10-27.5','enAppendix':'Keep all existing appendices, then add your new rules here.','zhHantAppendix':'保留先前追加規則，再把新增規則寫在這裡。'},requires=['skill_version'],capture=upload_capture)
add(admin_group,'04c 上傳完整中英文 Markdown（不啟用）','POST','/admin/planning/skills/documents',description='先下載目前完整中英文 Skill，僅在檔案尾端新增規則。version 使用新版本，不能刪改內建基底；是否保留所有已啟用追加規則，仍需在預覽檢查。',requires=['skill_version'],form={'mode':'formdata','formdata':[{'key':'version','type':'text','value':'{{skill_version}}'},{'key':'baseVersion','type':'text','value':'2026-10-27.5'},{'key':'en','type':'file','src':[]},{'key':'zhHant','type':'file','src':[]}]},capture=upload_capture)
add(admin_group,'05 預覽候選版本與將移除的規則','GET','/admin/planning/skills/versions/{{skill_version}}',requires=['skill_version'],capture=status_capture)
add(admin_group,'06 ⚠ 啟用候選版本','POST','/admin/planning/skills/versions/{{skill_version}}/activate',{'expectedActiveFingerprint':'{{active_fingerprint}}'},'確認 appendOnly=true 才啟用；若回 409，重新預覽與比較後再決定。',requires=['skill_version','active_fingerprint','allow_writes'],capture=status_capture)
add(admin_group,'07 ⚠ 明確回復舊版','POST','/admin/planning/skills/rollback','{"version":"{{rollback_version}}","expectedActiveFingerprint":"{{active_fingerprint}}","confirmRemoval":{{confirm_removal}}}', '先將 skill_version 設為目標舊版並執行 05，審閱 removedRules。與使用者討論後，才將 confirm_removal 改 true；allow_writes 也需 yes。',requires=['rollback_version','active_fingerprint','allow_writes'],capture=status_capture)
# Fix confirmations to false until the operator explicitly changes the variable.
for name,lang in [('08 下載目前完整中文 Skill','zh-Hant'),('09 下載目前完整英文 Skill','en')]:
    add(admin_group,name,'GET',f'/planning/skills?language={lang}&download=true')
admin_items = folders.pop(admin_group)
for item in admin_items:
    item['request']['url']=item['request']['url'].replace('{{base_url}}','{{skill_admin_base_url}}')
    item['request']['header'].append({'key':'X-Skill-Admin-Token','value':'{{vault:sydneybuddy-skill-admin-token}}'})
admin_collection={'info':{'name':'SydneyBuddy — Skill 管理 API','schema':'https://schema.getpostman.com/json/collection/v2.1.0/collection.json','description':'已部署 AWS。skill_admin_base_url 指向既有 /sydneybuddy 網址；本機測試可另改為 http://127.0.0.1:8787，並設定獨立管理憑證。App Bearer 沿用 Vault，管理憑證放本機 Vault 的 sydneybuddy-skill-admin-token。上傳不啟用；先預覽再啟用。不得對整組執行 Runner。'},'auth':{'type':'bearer','bearer':[{'key':'token','value':'{{vault:sydneybuddy-app-access-code}}','type':'string'}]},'variable':[{'key':k,'value':v,'type':'string'} for k,v in {'skill_admin_base_url':BASE,'skill_version':'2026-10-27.6','active_fingerprint':'','rollback_version':'2026-10-27.5','confirm_removal':'false','allow_writes':'no','data_source':'local','language':'zh-Hant'}.items()],'item':admin_items}
OUT.mkdir(parents=True,exist_ok=True)
(OUT/'SkillAdmin.postman_collection.json').write_text(json.dumps(admin_collection,ensure_ascii=False,indent=2)+'\n')

variables={'base_url':BASE,'local_base_url':'http://127.0.0.1:8787','app_access_code':'','data_source':'zetaris-cloud','language':'en','travel_date':'2026-10-28','days':'1','budget':'180','max_steps':'10000','transport':'walk','allow_writes':'no'}
# Every dependency has a visible collection variable; blank until explicitly supplied/captured.
for items in folders.values():
    for item in items:
        for key in re.findall(r'\{\{(\w+)\}\}',json.dumps(item)):
            variables.setdefault(key,'')
collection={'info':{'name':'SydneyBuddy — 完整 API 測試','schema':'https://schema.getpostman.com/json/collection/v2.1.0/collection.json','description':'全路由請求範例。先在本機 Postman Vault 設定 sydneybuddy-app-access-code（App 連線碼，不是 NVIDIA 或 Zetaris 密碼），再開 01 健康檢查。02 可測 JSON 規劃與下載。預設 allow_writes=no；⚠ 請求需確認後改 yes 並逐一傳送，不要對整組執行 Runner。請勿分享含連線碼或個人資料的匯出檔。雲端固定使用 zetaris-cloud；本機可改 base_url 與 data_source=local。'},'auth':{'type':'bearer','bearer':[{'key':'token','value':'{{vault:sydneybuddy-app-access-code}}','type':'string'}]},'variable':[{'key':k,'value':v,'type':'string'} for k,v in variables.items()], 'item':[{'name':k,'item':v} for k,v in folders.items()]}
OUT.mkdir(parents=True,exist_ok=True)
(OUT/'SydneyBuddy.postman_collection.json').write_text(json.dumps(collection,ensure_ascii=False,indent=2)+'\n')
for name,url,source in [('AWS',BASE,'zetaris-cloud'),('Local','http://127.0.0.1:8787','local')]:
    env={'name':'SydneyBuddy '+name,'values':[{'key':k,'value':v,'enabled':True,'type':'secret' if k=='app_access_code' else 'default'} for k,v in {'base_url':url,'data_source':source,'app_access_code':''}.items()],'_postman_variable_scope':'environment'}
    (OUT/f'{name}.postman_environment.json').write_text(json.dumps(env,ensure_ascii=False,indent=2)+'\n')
# Normalize all parameter names to prove method/path route coverage independent of chosen variable names.
norm=lambda p: re.sub(r'\{[^{}]+\}', '{}',p)
actual=set()
for file in (ROOT/'server-dotnet/SydneyBuddy.Api/Api').glob('*Endpoints.cs'):
    for method,path in re.findall(r'\.Map(Get|Post|Put|Patch|Delete)\("([^"]+)"',file.read_text()): actual.add((method.upper(),norm(("/admin/planning/skills" if file.name=="SkillAdminEndpoints.cs" else "")+path)))
produced={(m,norm(p)) for m,p in covered}
missing=actual-produced
assert not missing, f'Missing routes: {missing}'
print(json.dumps({'requests':sum(map(len,folders.values()))+len(admin_items),'mapped_routes':len(actual),'folders':len(folders),'missing':list(missing)},ensure_ascii=False))
