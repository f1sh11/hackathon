import type { Dispatch, FormEvent, SetStateAction } from 'react';
import type { Profile, TripRequest } from '../../types/contracts';
import { Icon, type IconName } from '../../components/Icon';
import { TravelBuddy as Buddy } from '../../components/TravelBuddy';

type TaskFormProps = {
  profile: Profile;
  draft: TripRequest;
  setDraft: Dispatch<SetStateAction<TripRequest>>;
  busy: boolean;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
};

export function TaskForm({ profile, draft, setDraft, busy, onSubmit }: TaskFormProps) {
  return <>
          <div className="intro"><div className="eyebrow">新建冒险任务</div><h1>配置这次冒险！</h1><p className="subtitle">带上预算与精力，漫漫来安排路线。</p></div>
          <div className="profile-strip"><span className="mini-avatar"><Buddy size={47} /></span><div><strong>{profile.character}</strong><span>固定画像已载入</span></div><Icon name="lock" size={15} /></div>
          <form onSubmit={onSubmit} className="task-form"><section className="panel form-section"><div className="section-heading"><span className="section-number">01</span><h2>这一程，去哪里</h2></div><div className="destination"><Icon name="pin" size={23} /><div><strong>悉尼 Sydney</strong><span>单人一日游 · 当前示例城市</span></div><span className="pill">示例</span></div><label>给旅程起个名字<input name="title" value={draft.title} onChange={e => setDraft({ ...draft, title: e.target.value })} required maxLength={80} disabled={busy} /></label></section>
            <section className="panel form-section"><div className="section-heading"><span className="section-number">02</span><h2>留多少时间与余力</h2></div><div className="form-grid"><label className="full">出行日期<input name="date" type="date" value={draft.date} onInput={e => { const value = e.currentTarget.value; setDraft(prev => ({ ...prev, date: value })); }} onChange={e => setDraft({ ...draft, date: e.target.value })} required disabled={busy} /></label><label>出发时间<input name="startTime" type="time" value={draft.startTime} onInput={e => { const value = e.currentTarget.value; setDraft(prev => ({ ...prev, startTime: value })); }} onChange={e => setDraft({ ...draft, startTime: e.target.value })} required disabled={busy} /></label><label>最晚返回<input name="endTime" type="time" value={draft.endTime} onInput={e => { const value = e.currentTarget.value; setDraft(prev => ({ ...prev, endTime: value })); }} onChange={e => setDraft({ ...draft, endTime: e.target.value })} required disabled={busy} /></label><label>单日预算 · A$<input name="budget" type="number" inputMode="decimal" min={0} max={10000} step="0.01" value={draft.budget} onChange={e => setDraft({ ...draft, budget: Number(e.target.value) })} required disabled={busy} /></label><label>今天的精力<select name="energy" value={draft.energy} onChange={e => setDraft({ ...draft, energy: Number(e.target.value) })} disabled={busy}><option value={100}>充足 · HP 100</option><option value={70}>一般 · HP 70</option><option value={45}>疲惫 · HP 45</option></select></label></div></section>
            <section className="panel form-section"><div className="section-heading"><span className="section-number">03</span><h2>给这一天一点偏爱</h2></div><div className="mode-buttons" aria-label="行程主题">{([['harbour', '海港与城市', 'sun'], ['culture', '文化与咖啡', 'wallet'], ['nature', '公园与自然', 'leaf']] as [string, string, IconName][]).map(([id, label, icon]) => <button type="button" key={id} className="mode" aria-pressed={draft.theme === id} onClick={() => setDraft({ ...draft, theme: id })} disabled={busy}><Icon name={icon} size={21} /><span>{label}</span></button>)}</div><label>还有想记下的吗？<textarea name="notes" value={draft.notes} onChange={e => setDraft({ ...draft, notes: e.target.value })} placeholder="和海风待一会儿，别太赶。" maxLength={500} disabled={busy} /><span className="field-hint">备注仅保存；本版根据上面的结构化选项规划，未接入 AI。</span></label></section>
            <p className="plan-promise"><Icon name="check" size={16} />校验体力与预算 · 安排休息 · 预留返程</p><button className="button primary full-button" type="submit" disabled={busy}>{busy ? '正在计算并保存行程…' : '生成我的行程'}{!busy && <Icon name="arrow" size={18} />}</button><p className="privacy-note">地点、费用、营业时间和交通均为演示估算</p></form>
        </>;
}
