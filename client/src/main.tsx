import { useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api, type Bootstrap, type Profile, type Trip, type TripRequest, type TripSummary, type TripVersion } from './api';
import './styles.css';

const questions = [
  { title: '一天走多少路，你会觉得舒服？', hint: '按平时的自己来选，画像没有高分或低分之分。', options: ['慢慢走，约 2–4 公里就好', '5–8 公里，边走边停', '喜欢步行，9 公里以上也可以'] },
  { title: '看到台阶和上坡，你会怎么想？', hint: '我们会据此考虑路线坡度与预计体力消耗。', options: ['尽量平路，能坐电梯更好', '少量坡道可以接受', '愿意登高，风景值得多走一点'] },
  { title: '你想要怎样的一天？', hint: '留白也是旅行的一部分。', options: ['只去 1–2 个重点，留足休息', '3 个左右的重点，松紧适中', '想多看一些，行程可以紧凑'] },
  { title: '值得的体验，你愿意多花一点吗？', hint: '只了解消费偏好，实际预算在创建任务时填写。', options: ['省钱优先，免费也能很好玩', '特别喜欢的，可以适当多花', '在预算内，体验和舒适更重要'] },
  { title: '一个小众地点，值得你绕路吗？', hint: '经典景点与意外发现，你更喜欢哪种？', options: ['先看经典，不想特意绕路', '顺路探索，也愿意小幅绕路', '喜欢发现，愿意专程前往'] },
  { title: '如果计划临时变了，你会怎样？', hint: '了解你对等待和变动的接受程度。', options: ['希望确定，尽量少等待和变动', '少量排队或调整可以接受', '比较随性，现场决定也没关系'] },
];
const axes = ['步行耐受', '爬坡耐受', '行程密度', '消费意愿', '探索意愿', '变化接受'];
const shortTime = (v: string) => v.slice(0, 5);
const minuteOfDay = (v: string) => Number(v.slice(0,2)) * 60 + Number(v.slice(3,5));
const currency = (n: number) => new Intl.NumberFormat('en-AU', { maximumFractionDigits: 2 }).format(n);
const dateLabel = (v: string) => new Intl.DateTimeFormat('zh-CN', { month: 'long', day: 'numeric' }).format(new Date(v + 'T12:00:00'));
const today = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Australia/Sydney', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
const reasonLabel = (reason: string) => reason.includes('轻松版') ? '更轻松的安排' : reason.includes('备注') ? '你补充的要求' : /预算|支出/.test(reason) ? '预算与余力' : /平缓|坡度|爬坡/.test(reason) ? '适合你的路线' : /休息|简餐/.test(reason) ? '休息也在计划里' : '照顾你的节奏';
const statusLabel = (s: string) => ({ completed: '已生成', infeasible: '需要调整', failed: '生成失败' }[s] ?? s);
const initialDraft = (): TripRequest => ({ profileId: '', title: '悉尼，慢慢走的一天', date: today(), startTime: '09:00', endTime: '18:00', budget: 120, energy: 100, theme: 'harbour', notes: '' });
type Screen = 'profile' | 'new' | 'trips' | 'itinerary';

function Radar({ profile }: { profile: Profile }) {
  const ref = useRef<SVGSVGElement>(null);
  const [width, setWidth] = useState(320);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const observer = new ResizeObserver(() => setWidth(el.clientWidth || 320));
    observer.observe(el);
    return () => observer.disconnect();
  }, []);
  const radius = Math.max(35, Math.min((width - 150) / 2, 87));
  const point = (i: number, fraction: number) => [width / 2 + Math.sin(i * Math.PI / 3) * radius * fraction, 133 - Math.cos(i * Math.PI / 3) * radius * fraction];
  return <svg ref={ref} className="radar" viewBox={`0 0 ${width} 270`} role="img" aria-label="根据回答生成的只读六维旅行画像">
    <title>旅行角色画像，分数由问答自动生成，不能手动调整</title>
    <desc>{axes.map((a, i) => `${a} ${profile.values[i]} 分`).join('，')}</desc>
    {[.25, .5, .75, 1].map(f => <polygon key={f} className="gridline" points={axes.map((_, i) => point(i, f).join(',')).join(' ')} />)}
    {axes.map((a, i) => { const p = point(i, 1), l = point(i, 1.28); return <g key={a}><line className="gridline" x1={width / 2} y1="133" x2={p[0]} y2={p[1]} /><text x={l[0]} y={l[1]} textAnchor={i === 0 || i === 3 ? 'middle' : i < 3 ? 'start' : 'end'} dominantBaseline="middle">{a}</text></g>; })}
    <polygon className="radar-shape" points={profile.values.map((v, i) => point(i, v / 100).join(',')).join(' ')} />
    {profile.values.map((v, i) => { const p = point(i, v / 100); return <circle key={i} className="radar-dot" cx={p[0]} cy={p[1]} r="3" />; })}
  </svg>;
}

function App() {
  const [profile, setProfile] = useState<Profile | null>(null);
  const [trips, setTrips] = useState<TripSummary[]>([]);
  const [screen, setScreen] = useState<Screen>('profile');
  const [booting, setBooting] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [answers, setAnswers] = useState<(number | null)[]>(Array(6).fill(null));
  const [step, setStep] = useState(0);
  const [draft, setDraft] = useState<TripRequest>(initialDraft);
  const [selectedTrip, setSelectedTrip] = useState<Trip | null>(null);
  const [selectedVersion, setSelectedVersion] = useState(1);
  const inFlight = useRef(false);
  const createKey = useRef(crypto.randomUUID());
  const replanKey = useRef(crypto.randomUUID());

  async function bootstrap() {
    setBooting(true); setError('');
    try {
      const data = await api<Bootstrap>('/api/bootstrap');
      setProfile(data.profile); setTrips(data.trips);
      setScreen(data.profile ? (data.trips.length ? 'trips' : 'profile') : 'profile');
    } catch (e) { setError(message(e)); }
    finally { setBooting(false); }
  }
  useEffect(() => { void bootstrap(); }, []);
  function message(e: unknown) { return e instanceof Error ? e.message : '操作未完成，请稍后重试。'; }
  function navigate(next: Screen) { setError(''); setScreen(next); }
  function addSummary(trip: Trip) {
    setTrips(prev => [{ id: trip.id, title: trip.title, status: trip.status, createdAt: trip.createdAt, latestVersion: trip.latestVersion, request: trip.request }, ...prev.filter(t => t.id !== trip.id)]);
  }
  async function createProfile() {
    if (inFlight.current || answers.some(a => a === null)) return;
    inFlight.current = true; setBusy(true); setError('');
    try { const p = await api<Profile>('/api/profiles', { answers }); setProfile(p); }
    catch (e) { setError(message(e)); }
    finally { inFlight.current = false; setBusy(false); }
  }
  function newTask(request?: TripRequest) {
    createKey.current = crypto.randomUUID();
    setDraft(request ? { ...request, title: request.title + ' · 调整' } : initialDraft());
    navigate('new');
  }
  async function createTrip(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!profile || inFlight.current) return;
    if (draft.endTime <= draft.startTime) { setError('最晚返回时间需要晚于出发时间；第一版仅支持当天出发与返回。'); return; }
    if (!draft.title.trim()) { setError('请填写任务名称。'); return; }
    inFlight.current = true; setBusy(true); setError('');
    try {
      const trip = await api<Trip>('/api/trips', { ...draft, profileId: profile.id, title: draft.title.trim(), idempotencyKey: createKey.current });
      setSelectedTrip(trip); setSelectedVersion(trip.latestVersion); addSummary(trip); setScreen('itinerary');
      createKey.current = crypto.randomUUID();
    } catch (e) { setError(message(e)); }
    finally { inFlight.current = false; setBusy(false); }
  }
  async function openTrip(id: string) {
    if (inFlight.current) return;
    inFlight.current = true; setBusy(true); setError('');
    try { const trip = await api<Trip>(`/api/trips/${id}`); setSelectedTrip(trip); setSelectedVersion(trip.latestVersion); setScreen('itinerary'); }
    catch (e) { setError(message(e)); }
    finally { inFlight.current = false; setBusy(false); }
  }
  async function replan() {
    if (!selectedTrip || inFlight.current) return;
    inFlight.current = true; setBusy(true); setError('');
    try {
      const trip = await api<Trip>(`/api/trips/${selectedTrip.id}/replan`, { easier: true, idempotencyKey: replanKey.current });
      setSelectedTrip(trip); setSelectedVersion(trip.latestVersion); addSummary(trip); replanKey.current = crypto.randomUUID();
    } catch (e) { setError(message(e)); }
    finally { inFlight.current = false; setBusy(false); }
  }
  const chosen = selectedTrip?.versions.find(v => v.version === selectedVersion) ?? selectedTrip?.versions.at(-1);
  const active = screen === 'itinerary' || screen === 'trips' ? 'trips' : screen;
  const breadcrumbs = { profile: '旅行角色 / ' + (profile ? '你的专属画像' : '初次见面'), new: '规划任务 / 新建任务', trips: '我的任务 / 行程档案', itinerary: '我的行程 / 悉尼一日游' };

  return <div id="roam-app"><div className="shell">
    <aside><div><div className="brand">漫游<span>.</span></div><div className="small">ROAM · 自在出发</div></div>
      <nav className="navitems" aria-label="主要导航">
        <button className={'navitem ' + (active === 'profile' ? 'active' : '')} onClick={() => navigate('profile')} disabled={busy || booting}><span>01</span><b>旅行角色</b></button>
        <button className={'navitem ' + (active === 'new' ? 'active' : '')} onClick={() => newTask()} disabled={!profile || busy || booting}><span>02</span><b>创建任务</b></button>
        <button className={'navitem ' + (active === 'trips' ? 'active' : '')} onClick={() => navigate('trips')} disabled={!profile || busy || booting}><span>03</span><b>我的行程</b><span className="nav-count">{trips.length || ''}</span></button>
      </nav><div className="side-bottom"><div className="avatar">R</div><div>{profile?.character ?? '一个人，也刚刚好'}</div><div className="small">{profile ? '画像已保存 · 不可调分' : 'SOLO EXPLORER'}</div></div>
    </aside>
    <main><div className="topbar"><div className="small">{breadcrumbs[screen]}</div><span className="pill">本地 MVP · 示例数据</span></div>
      {error && <div className="error-banner" role="alert">{error}{!profile && !booting && <button className="text-button" onClick={() => void bootstrap()}>重新连接</button>}</div>}
      {booting ? <div className="loading"><div className="dotloading">✦</div><h1>正在打开你的旅行档案。</h1><p className="subtitle">从本地服务读取画像与行程。</p></div> : <>
        {screen === 'profile' && !profile && <>
          <div className="intro"><div className="eyebrow">MEET YOUR TRAVEL SELF</div><h1>先认识你，再安排旅途。</h1><p className="subtitle">六个小问题，生成属于你的旅行角色。完成后画像将固定保存。</p></div>
          <section className="questionbox" aria-label="创建旅行画像"><div className="row small"><span>关于你的旅行习惯</span><span className="push num">{step + 1} / 6</span></div><div className="qprogress" role="progressbar" aria-label="答题进度" aria-valuemin={0} aria-valuemax={6} aria-valuenow={step + 1}><span style={{ width: (step + 1) / 6 * 100 + '%' }} /></div><h2>{questions[step].title}</h2><p className="subtitle question-hint">{questions[step].hint}</p><div className="choices">{questions[step].options.map((o, i) => <button key={o} type="button" className="choice" aria-pressed={answers[step] === i} onClick={() => setAnswers(prev => prev.map((a, index) => index === step ? i : a))} disabled={busy}><span className="choice-letter">{['A', 'B', 'C'][i]}</span><span>{o}</span></button>)}</div><div className="question-footer"><button className="button" onClick={() => setStep(s => s - 1)} disabled={step === 0 || busy}>上一题</button><button className="button primary" disabled={answers[step] === null || busy} onClick={() => step === 5 ? void createProfile() : setStep(s => s + 1)}>{busy ? '正在保存角色…' : step === 5 ? '生成并保存我的角色 →' : '下一题 →'}</button></div></section>
        </>}
        {screen === 'profile' && profile && <>
          <div className="intro"><div className="eyebrow">YOUR TRAVEL IDENTITY</div><h1>原来，你是这样的旅行者。</h1><p className="subtitle">画像已根据回答生成。接下来的安排，会记得你的节奏。</p></div><div className="two"><section className="profile-visual"><div className="eyebrow">TRAVELER CARD · {profile.id.slice(0, 6).toUpperCase()}</div><Radar profile={profile} /><div className="center"><span className="pill">已保存 · 只读展示</span></div></section><section className="panel"><span className="pill">你的旅行角色</span><div className="profile-name">{profile.character}</div><p className="subtitle">{profile.values[2] < 40 ? '喜欢慢一点，把时间留给真正喜欢的地方。' : '想看城市，也想给咖啡和风景留一点时间。'}</p><div className="details">{[['节奏', profile.values[2] < 40 ? '悠闲留白' : profile.values[2] > 70 ? '丰富紧凑' : '松紧适中'], ['步行', profile.values[0] < 40 ? '短途优先' : '边走边停'], ['路线', profile.values[1] < 40 ? '尽量平缓' : '接受少量坡道'], ['探索', profile.values[4] > 70 ? '喜欢小众发现' : '经典与发现兼顾']].map(([label, text]) => <div className="detail" key={label}><span className="small">{label}</span><span>{text}</span></div>)}</div></section></div><p className="subtle-line">画像固定保存。本次精力、预算和时间在创建任务时填写，不会改变你的六维画像。</p><div className="wide-action"><span className="small">准备好，就出发吧</span><button className="button primary" onClick={() => newTask()}>带着这个角色，创建任务 →</button></div>
        </>}
        {screen === 'new' && profile && <>
          <div className="intro"><div className="eyebrow">A LITTLE BRIEF, A WHOLE DAY</div><h1>这一次，你想怎么出发？</h1><p className="subtitle">告诉我们要求，完整的一天交给我们安排。</p></div>
          <form onSubmit={e => void createTrip(e)}><div className="task-layout"><section className="panel"><div className="form-grid">
            <label className="full">任务名称<input name="title" value={draft.title} onChange={e => setDraft({ ...draft, title: e.target.value })} required maxLength={80} disabled={busy} /></label>
            <div className="full row"><span className="pill">悉尼 Sydney · 单人一日游</span><span className="small">示例城市</span></div>
            <label className="full">出行日期<input name="date" type="date" value={draft.date} onChange={e => setDraft({ ...draft, date: e.target.value })} required disabled={busy} /></label>
            <label>出发时间<input name="startTime" type="time" value={draft.startTime} onChange={e => setDraft({ ...draft, startTime: e.target.value })} required disabled={busy} /></label>
            <label>最晚返回<input name="endTime" type="time" value={draft.endTime} onChange={e => setDraft({ ...draft, endTime: e.target.value })} required disabled={busy} /></label>
            <label>单日预算 · A$<input name="budget" type="number" min={0} max={10000} step="0.01" value={draft.budget} onChange={e => setDraft({ ...draft, budget: Number(e.target.value) })} required disabled={busy} /></label>
            <label>本次精力<select name="energy" value={draft.energy} onChange={e => setDraft({ ...draft, energy: Number(e.target.value) })} disabled={busy}><option value={100}>充足 · HP 100</option><option value={70}>一般 · HP 70</option><option value={45}>疲惫 · HP 45</option></select></label>
            <div className="full"><div className="small mode-label">这次的主题</div><div className="mode-buttons">{[['harbour', '海港与城市'], ['culture', '文化与咖啡'], ['nature', '公园与自然']].map(([id, label]) => <button type="button" key={id} className="mode" aria-pressed={draft.theme === id} onClick={() => setDraft({ ...draft, theme: id })} disabled={busy}>{label}</button>)}</div></div>
            <label className="full">旅行备注<input name="notes" value={draft.notes} onChange={e => setDraft({ ...draft, notes: e.target.value })} placeholder="例如：和海风待一会儿，别太赶。" maxLength={500} disabled={busy} /><span className="small">本版不接 AI：备注仅保存，不参与规划；主题、时间、预算与精力会参与计算。</span></label>
          </div></section><section className="task-brief"><div className="panel"><div className="row"><div className="avatar">R</div><div><h3>{profile.character}</h3><div className="small">已载入固定旅行画像</div></div></div><div className="divider" /><ul className="list"><li><span className="check">✓</span><span>考虑步行与爬坡习惯</span></li><li><span className="check">✓</span><span>安排休息，预留返程时间</span></li><li><span className="check">✓</span><span>校验每一步的预计体力</span></li><li><span className="check">✓</span><span>保留任务与行程版本</span></li></ul></div><div className="readonly">你只需要提出要求。<br />收到的是一份完整行程。</div><div className="notice">地点费用、开放时段和交通均为示例数据，用于验证产品逻辑。</div></section></div><div className="wide-action"><span className="small">交给后端计算，无需逐个挑景点</span><button className="button primary" type="submit" disabled={busy}>{busy ? '正在计算并保存行程…' : '创建任务，生成行程 →'}</button></div></form>
        </>}
        {screen === 'trips' && <>
          <div className="result-top"><div className="intro"><div className="eyebrow">YOUR TRAVEL JOURNAL</div><h1>下一次出发，有迹可循。</h1><p className="subtitle">任务与行程保存在本地 PostgreSQL，刷新页面也能再打开。</p></div><button className="button primary" onClick={() => newTask()} disabled={busy}>创建新任务 ＋</button></div>
          {!trips.length ? <div className="panel empty"><div className="empty-mark">✦</div><h2>第一段旅程，还没开始。</h2><p className="subtitle">用你的旅行角色，试着安排一个舒服的一天。</p><button className="button primary" onClick={() => newTask()}>创建第一个任务 →</button></div> : <div className="trip-list">{trips.map(t => <button key={t.id} className="trip-card" disabled={busy} onClick={() => void openTrip(t.id)}><div className="trip-card-number">{dateLabel(t.request.date)}<span>{t.request.theme === 'harbour' ? '海港与城市' : t.request.theme === 'culture' ? '文化与咖啡' : '公园与自然'}</span></div><div className="trip-card-main"><div className="row"><h2>{t.title}</h2><span className={'status ' + t.status}>{statusLabel(t.status)}</span></div><p className="small">{shortTime(t.request.startTime)}–{shortTime(t.request.endTime)} · 预算 A${currency(t.request.budget)} · 起始 HP {t.request.energy}</p></div><div className="trip-card-arrow"><span className="small">V{t.latestVersion}</span><span>↗</span></div></button>)}</div>}
        </>}
        {screen === 'itinerary' && selectedTrip && chosen && <Itinerary trip={selectedTrip} version={chosen} selectedVersion={selectedVersion} onVersion={setSelectedVersion} onReplan={() => void replan()} onAdjust={() => newTask(selectedTrip.request)} busy={busy} />}
      </>}
      <footer>本地测试版 · 结构化示例数据 · HP 为规划估算 · 未接实时交通、实际票价或预订服务</footer>
    </main>
  </div></div>;
}

function Itinerary({ trip, version, selectedVersion, onVersion, onReplan, onAdjust, busy }: { trip: Trip; version: TripVersion; selectedVersion: number; onVersion: (v: number) => void; onReplan: () => void; onAdjust: () => void; busy: boolean }) {
  const plan = version.plan;
  const request = trip.request;
  const failed = trip.status === 'failed' && version.version === trip.latestVersion;
  return <>
    <div className="result-top"><div className="intro"><div className="eyebrow">YOUR DAY, THOUGHTFULLY PLANNED</div><h1>{plan ? '这一天，替你安排好了。' : failed ? '这次生成，没有完成。' : '这次，需要留一点余地。'}</h1><p className="subtitle">{trip.title} · {dateLabel(request.date)} · 悉尼一日游</p></div><span className="pill">{plan ? '行程已保存' : '任务已保存'} · V{version.version}{version.easier ? ' 轻松版' : ''}</span></div>
    <div className="version-row"><div className="row small"><span>任务 {trip.id.slice(0, 8).toUpperCase()}</span><span>／</span><span>{shortTime(request.startTime)} 出发</span>{plan && <><span>／</span><span>步行约 {plan.walkKm.toFixed(1)} km</span></>}</div>{trip.versions.length > 1 && <label className="version-picker">查看版本<select aria-label="查看行程版本" value={selectedVersion} onChange={e => onVersion(Number(e.target.value))} disabled={busy}>{trip.versions.map(v => <option value={v.version} key={v.version}>V{v.version}{v.easier ? ' · 轻松版' : ' · 初始版'}{v.plan ? '' : ' · 无可行解'}</option>)}</select></label>}</div>
    {plan ? <>
      <div className="stats"><Metric label="预计结束时的余力" value={`HP ${plan.remainingHp}`} suffix={` / ${request.energy}`} detail="全过程均保留预计精力余量" ratio={plan.remainingHp / request.energy} /><Metric label="预计总花费" value={`A$${currency(plan.totalCost)}`} detail={`预算 A$${currency(request.budget)} · 余 A$${currency(request.budget - plan.totalCost)}`} ratio={request.budget > 0 ? plan.totalCost / request.budget : 0} /><Metric label="预计返回时间" value={shortTime(plan.returnTime)} detail={`要求 ${shortTime(request.endTime)} 前返回`} ratio={(minuteOfDay(plan.returnTime) - minuteOfDay(request.startTime)) / Math.max(1, minuteOfDay(request.endTime) - minuteOfDay(request.startTime))} /></div>
      <div className="itinerary-grid"><section className="panel"><div className="row"><h2>今天，就这样走</h2><span className="small push">按顺序出发</span></div><ol className="timeline">{plan.stops.map((stop, index) => <li key={`${stop.id}-${index}`}><div className="time num">{shortTime(stop.startTime)}</div><div className="stop"><h3>{stop.name}</h3><p className="small">{stop.description}</p><div className="stop-meta num">至 {shortTime(stop.endTime)} · 转场约 {stop.transferMinutes} 分钟 · 本段 A${currency(stop.cost)} · 预计余力 HP {stop.remainingHp}</div></div></li>)}</ol></section><section className="why"><div className="panel"><span className="pill">为什么这样安排</span><div className="reason-list">{plan.reasons.map(reason => <div key={reason}><h3>{reasonLabel(reason)}</h3><p>{reason}</p></div>)}</div></div><div className="notice">{plan.dataNotice || '所有地点、开放时段、路线和费用为示例估算，真实出发前需核验。'}</div>{request.notes && <div className="panel"><h3>本次备注</h3><p className="small note-value">{request.notes}</p><p className="small note-caption">仅保存，未通过 AI 解析。</p></div>}</section></div>
    </> : <section className="panel infeasible"><span className="pill">{failed ? '生成失败' : '当前条件下无可行解'}</span><h2>{failed ? '任务已保留，可以重新尝试。' : '不勉强，换一种安排。'}</h2><p className="subtitle">{failed ? '服务没有返回完整行程，请稍后重试或调整要求。' : '没有把超预算、超时或体力不足的路线当成成功结果。'}</p><ul>{(version.conflicts.length ? version.conflicts : ['暂时无法在当前条件下找到合适行程，请调整预算、时间或精力后重试。']).map(c => <li key={c}>{c}</li>)}</ul></section>}
    <div className="wide-action"><button className="button" onClick={onAdjust} disabled={busy}>调整要求，创建新任务</button><button className="button primary" onClick={onReplan} disabled={busy || version.easier || !plan}>{busy ? '正在重新计算…' : version.easier ? '已生成轻松版' : '再轻松一点，重新安排'}</button></div>
  </>;
}
function Metric({ label, value, suffix, detail, ratio }: { label: string; value: string; suffix?: string; detail: string; ratio: number }) {
  return <div className="stat"><div className="small">{label}</div><strong>{value}{suffix && <span className="small">{suffix}</span>}</strong><div className="small">{detail}</div><div className="meter"><span style={{ width: Math.max(0, Math.min(100, ratio * 100)) + '%' }} /></div></div>;
}

createRoot(document.getElementById('root')!).render(<App />);
