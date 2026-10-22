import type { FormEvent } from 'react';
import { clock, ENDPOINTS, INTERESTS, parseClock } from '../domain';
import type { Health, Place, Profile } from '../types';
import { Icon } from './Icon';

interface Props {
  profile: Profile;
  onChange: (profile: Profile) => void;
  online: boolean;
  onOnlineChange: (online: boolean) => void;
  health: Health | null;
  catalog: Place[];
  busy: boolean;
  editing: boolean;
  onGenerate: () => void;
}
/** 负责收集偏好；不会调用接口或保存行程。 / Collects preferences; never sends requests or saves trips. */
export function PlannerForm({
  profile: p,
  onChange,
  online,
  onOnlineChange,
  health,
  catalog,
  busy,
  editing,
  onGenerate,
}: Props) {
  function set<K extends keyof Profile>(key: K, value: Profile[K]) {
    onChange({ ...p, [key]: value });
  }
  function submit(e: FormEvent) {
    e.preventDefault();
    onGenerate();
  }
  const modeDescription = !online
    ? '本地规则与示例数据，不调用付费服务'
    : health?.googleConfigured
      ? 'NVIDIA 推荐 + Google 地点、路线与天气'
      : 'NVIDIA 推荐，其他数据仍为示例估算';
  return (
    <form className="planner-card card" onSubmit={submit}>
      <div className="section-heading">
        <div>
          <span className="eyebrow">YOUR WAY, YOUR PACE</span>
          <h2>{editing ? '调整一下，继续发现' : '想怎么认识悉尼？'}</h2>
        </div>
        <span className="round-icon">
          <Icon name="sparkle" />
        </span>
      </div>
      <fieldset disabled={busy}>
        <div className="form-grid">
          <label>
            出发日期
            <input
              required
              type="date"
              value={p.date}
              onChange={(e) => set('date', e.target.value)}
            />
          </label>
          <label>
            留几天
            <select value={p.days} onChange={(e) => set('days', Number(e.target.value))}>
              <option value={1}>1 天 · 小小出走</option>
              <option value={2}>2 天 · 慢慢探索</option>
              <option value={3}>3 天 · 好好认识</option>
            </select>
          </label>
        </div>
        <div className="field-label">
          你对什么感兴趣？<small>可以多选</small>
        </div>
        <div className="interest-chips">
          {INTERESTS.map((interest, i) => (
            <button
              type="button"
              key={interest}
              className={`interest ${p.interests.includes(interest) ? 'selected' : ''}`}
              aria-pressed={p.interests.includes(interest)}
              onClick={() =>
                set(
                  'interests',
                  p.interests.includes(interest)
                    ? p.interests.filter((v) => v !== interest)
                    : [...p.interests, interest],
                )
              }
            >
              <span aria-hidden="true">{['◒', '◡', '♧', '◇', '▤', '▧'][i]}</span>
              {interest}
              {p.interests.includes(interest) && <Icon name="check" size={13} />}
            </button>
          ))}
        </div>
        <div className="form-grid">
          <label>
            每日活动预算 · AUD
            <div className="input-prefix">
              <span>A$</span>
              <input
                required
                aria-label="每日活动预算"
                type="number"
                min="0"
                max="100000"
                step="1"
                value={p.budget}
                onChange={(e) => set('budget', e.target.value === '' ? 0 : Number(e.target.value))}
              />
            </div>
          </label>
          <label>
            每日步行上限
            <select value={p.maxSteps} onChange={(e) => set('maxSteps', Number(e.target.value))}>
              <option value={5000}>5,000 步 · 轻松一点</option>
              <option value={7000}>7,000 步 · 悠闲漫步</option>
              <option value={10000}>10,000 步 · 刚刚好</option>
              <option value={15000}>15,000 步 · 多走一点</option>
              <option value={20000}>20,000 步 · 探索模式</option>
              {![5000, 7000, 10000, 15000, 20000].includes(p.maxSteps) && (
                <option value={p.maxSteps}>{p.maxSteps.toLocaleString()} 步 · 已保存</option>
              )}
            </select>
          </label>
        </div>
        <details className="more-options">
          <summary>
            出发地点、时间与必去景点<span>＋</span>
          </summary>
          <div className="form-grid">
            <label>
              从哪里出发
              <select
                value={ENDPOINTS.findIndex((v) => v.name === p.start.name)}
                onChange={(e) => set('start', { ...ENDPOINTS[Number(e.target.value)] })}
              >
                {ENDPOINTS.map((v, i) => (
                  <option key={v.name} value={i}>
                    {v.name}
                  </option>
                ))}
                {!ENDPOINTS.some((v) => v.name === p.start.name) && (
                  <option value={-1}>{p.start.name}</option>
                )}
              </select>
            </label>
            <label>
              返回哪里
              <select
                value={ENDPOINTS.findIndex((v) => v.name === p.end.name)}
                onChange={(e) => set('end', { ...ENDPOINTS[Number(e.target.value)] })}
              >
                {ENDPOINTS.map((v, i) => (
                  <option key={v.name} value={i}>
                    {v.name}
                  </option>
                ))}
                {!ENDPOINTS.some((v) => v.name === p.end.name) && (
                  <option value={-1}>{p.end.name}</option>
                )}
              </select>
            </label>
            <label>
              出发时间
              <input
                required
                type="time"
                value={clock(p.startMinute)}
                onChange={(e) => set('startMinute', parseClock(e.target.value))}
              />
            </label>
            <label>
              最晚返回
              <input
                required
                type="time"
                value={clock(p.endMinute)}
                onChange={(e) => set('endMinute', parseClock(e.target.value))}
              />
            </label>
          </div>
          <div className="field-label">有没有一定想去的地方？</div>
          <div className="must-visits">
            {catalog.map((place) => (
              <label className="check-label" key={place.id}>
                <input
                  type="checkbox"
                  checked={p.mustVisit.includes(place.id)}
                  onChange={() =>
                    set(
                      'mustVisit',
                      p.mustVisit.includes(place.id)
                        ? p.mustVisit.filter((id) => id !== place.id)
                        : [...p.mustVisit, place.id],
                    )
                  }
                />
                {place.name}
              </label>
            ))}
          </div>
        </details>
        <label className="request-label">
          还想告诉 Buddy 什么？
          <textarea
            rows={2}
            maxLength={1500}
            value={p.request ?? ''}
            onChange={(e) => set('request', e.target.value)}
            placeholder="比如：想多看艺术馆，下午轻松一点……"
          />
        </label>
        <label className="online-control">
          <span>
            <strong>在线推荐</strong>
            <small>{modeDescription}</small>
          </span>
          <input
            type="checkbox"
            role="switch"
            checked={online}
            onChange={(e) => onOnlineChange(e.target.checked)}
          />
          <span className="switch-track" aria-hidden="true" />
        </label>
        <button className="button primary generate" type="submit" disabled={busy || !health}>
          {busy ? (
            <>
              <span className="spinner" />
              正在整理路线…
            </>
          ) : (
            <>
              <Icon name="sparkle" />
              {editing ? '生成调整草案' : '为我规划小冒险'}
              <Icon name="arrow" size={18} />
            </>
          )}
        </button>
        <p className="form-footnote">草案会先给你预览，确认之后才保存。</p>
      </fieldset>
    </form>
  );
}
